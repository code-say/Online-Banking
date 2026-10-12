using System.Net;
using System.Net.Http.Json;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using OnlineBanking.Web.Data;

namespace OnlineBanking.Tests;

/// <summary>
/// A stand-in for the real model. It asks for tools based on keywords and echoes tool results back,
/// so tests exercise the real function-calling pipeline, tools, scoping and guardrails without an API key.
/// </summary>
public sealed class ScriptedChatClient : IChatClient
{
    private static readonly JsonSerializerOptions Relaxed = new(AIJsonUtilities.DefaultOptions) { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var list = messages.ToList();
        var last = list[^1];

        // Second round: the pipeline has run our tool and sent its result back
        var results = last.Contents.OfType<FunctionResultContent>().ToList();
        if (results.Count > 0)
            return Text("RESULT " + string.Join(" | ", results.Select(r => JsonSerializer.Serialize(r.Result, Relaxed))));

        var text = last.Text ?? "";
        return text switch
        {
            _ when text.Contains("balance") => Call("get_accounts", []),
            _ when text.Contains("someone else") => Call("get_transactions", new() { ["accountNumber"] = SeedData.PayeeSavings }),
            _ when text.Contains("spend") => Call("get_spending_summary", new() { ["fromDate"] = "2020-01-01" }),
            _ when text.Contains("too much") => Call("prepare_transfer", new()
            {
                ["fromAccount"] = "3456", ["toAccountNumber"] = SeedData.PayeeSavings, ["amount"] = 99_999_999,
            }),
            _ when text.Contains("send") => Call("prepare_transfer", new()
            {
                ["fromAccount"] = "3456", ["toAccountNumber"] = "5010 0098 7654", ["amount"] = 500, ["remarks"] = "Books",
            }),
            _ when text.Contains("busy") => throw new InvalidOperationException("wrapper", new HttpRequestException("429 RESOURCE_EXHAUSTED: quota exceeded")),
            _ when text.Contains("explode") => throw new HttpRequestException("provider said: key=SECRET-123 invalid"),
            _ => Text($"ECHO {text} (system prompt: {list.Count(m => m.Role == ChatRole.System)}, turns: {list.Count(m => m.Role != ChatRole.System)}, tools: {options?.Tools?.Count ?? 0})"),
        };
    }

    private static Task<ChatResponse> Text(string text) => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, text)));

    private static Task<ChatResponse> Call(string name, Dictionary<string, object?> args) =>
        Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, [new FunctionCallContent(Guid.NewGuid().ToString("N"), name, args)])));

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}

/// <summary>The app with the scripted model plugged in, exactly where the Gemini client goes in production.</summary>
public sealed class AssistantAppFactory : AppFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(s => s.AddChatClient(new ScriptedChatClient()).UseFunctionInvocation());
    }
}

public class AssistantTests
{
    private static async Task<(HttpResponseMessage Res, JsonElement Body)> AskAsync(HttpClient client, string message, bool withToken = true)
    {
        var page = await client.GetStringAsync("/Dashboard");
        var token = System.Text.RegularExpressions.Regex.Match(page, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
        using var req = new HttpRequestMessage(HttpMethod.Post, "/Assistant/Send") { Content = JsonContent.Create(new { message }) };
        if (withToken) req.Headers.Add("RequestVerificationToken", WebUtility.HtmlDecode(token));
        var res = await client.SendAsync(req);
        var raw = await res.Content.ReadAsStringAsync();
        var body = raw.Length > 0 && raw.TrimStart().StartsWith('{') ? JsonDocument.Parse(raw).RootElement.Clone() : default;
        return (res, body);
    }

    private static string Reply(JsonElement body) => body.GetProperty("reply").GetString()!;

    [Fact]
    public async Task Without_a_model_the_assistant_is_hidden_and_off()
    {
        using var app = new AppFactory();
        var client = await app.CustomerAsync();
        Assert.DoesNotContain("assistant-panel", await client.GetStringAsync("/Dashboard"));

        var (res, body) = await AskAsync(client, "hello");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, res.StatusCode);
        Assert.Contains("isn't switched on", Reply(body));
    }

    [Fact]
    public async Task Panel_shows_for_customers_and_the_model_gets_a_system_prompt_and_tools()
    {
        using var app = new AssistantAppFactory();
        var client = await app.CustomerAsync();
        Assert.Contains("assistant-panel", await client.GetStringAsync("/Dashboard"));

        var (res, body) = await AskAsync(client, "hello");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Contains("ECHO hello (system prompt: 1, turns: 1, tools: 5)", Reply(body));
    }

    [Fact]
    public async Task Balance_question_uses_only_the_customers_accounts()
    {
        using var app = new AssistantAppFactory();
        var client = await app.CustomerAsync();
        var (_, body) = await AskAsync(client, "what is my balance?");
        var reply = Reply(body);
        Assert.StartsWith("RESULT", reply);
        Assert.Contains(SeedData.CustomerSavings, reply);
        Assert.Contains(SeedData.CustomerCurrent, reply);
        Assert.DoesNotContain(SeedData.PayeeSavings, reply);
    }

    [Fact]
    public async Task Tools_cant_read_another_customers_account()
    {
        using var app = new AssistantAppFactory();
        var client = await app.CustomerAsync();
        var (_, body) = await AskAsync(client, "show transactions of someone else");
        var reply = Reply(body);
        Assert.Contains("Account not found among the customer's accounts", reply);
        Assert.DoesNotContain("St. Xavier", reply); // Rahul's salary line
    }

    [Fact]
    public async Task Spending_summary_leaves_out_moves_between_own_accounts()
    {
        using var app = new AssistantAppFactory();
        var client = await app.CustomerAsync();
        var (_, body) = await AskAsync(client, "how much did I spend?");
        var reply = Reply(body);
        Assert.Matches("\"transfersBetweenOwnAccountsExcluded\":\\s*4", reply); // 2 moves x 2 legs
        Assert.Contains("Transfers to Rahul Verma", reply);
    }

    [Fact]
    public async Task Prepared_transfer_moves_no_money_and_opens_a_prefilled_form()
    {
        using var app = new AssistantAppFactory();
        var client = await app.CustomerAsync();
        var before = (await app.AccountAsync(SeedData.CustomerSavings)).Balance;

        var (res, body) = await AskAsync(client, "send 500 to Rahul");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var transfer = body.GetProperty("transfer");
        Assert.Equal("Rahul V.", transfer.GetProperty("payee").GetString());
        Assert.Equal("₹500.00", transfer.GetProperty("amount").GetString());
        var url = transfer.GetProperty("url").GetString()!;
        Assert.StartsWith("/Transfer?from=", url);

        Assert.Equal(before, (await app.AccountAsync(SeedData.CustomerSavings)).Balance);

        var form = await client.GetStringAsync(url);
        Assert.Contains("The assistant filled this in for you", form);
        Assert.Contains("value=\"500\"", form);
        Assert.Contains("value=\"Books\"", form);
        Assert.Contains($"value=\"{SeedData.PayeeSavings}\"", form);
    }

    [Fact]
    public async Task Impossible_transfers_are_not_prepared()
    {
        using var app = new AssistantAppFactory();
        var client = await app.CustomerAsync();
        var (_, body) = await AskAsync(client, "send way too much");
        Assert.Equal(JsonValueKind.Null, body.GetProperty("transfer").ValueKind);
        Assert.Matches("\"ready\":\\s*false", Reply(body));
    }

    [Fact]
    public async Task Daily_message_limit_is_enforced()
    {
        using var app = new AssistantAppFactory();
        app.Settings["Assistant:DailyMessageLimit"] = "2";
        var client = await app.CustomerAsync();
        Assert.Equal(HttpStatusCode.OK, (await AskAsync(client, "one")).Res.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await AskAsync(client, "two")).Res.StatusCode);
        var (res, body) = await AskAsync(client, "three");
        Assert.Equal(HttpStatusCode.TooManyRequests, res.StatusCode);
        Assert.Contains("today's limit", Reply(body));
    }

    [Fact]
    public async Task Provider_errors_are_hidden_from_the_customer()
    {
        using var app = new AssistantAppFactory();
        var client = await app.CustomerAsync();
        var (res, body) = await AskAsync(client, "explode");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, res.StatusCode);
        Assert.DoesNotContain("SECRET", Reply(body));
        Assert.Contains("unavailable right now", Reply(body));
    }

    [Fact]
    public async Task Provider_quota_errors_ask_the_customer_to_wait()
    {
        using var app = new AssistantAppFactory();
        var client = await app.CustomerAsync();
        var (res, body) = await AskAsync(client, "are you busy");
        Assert.Equal(HttpStatusCode.TooManyRequests, res.StatusCode);
        Assert.Contains("wait about a minute", Reply(body));
        Assert.DoesNotContain("RESOURCE_EXHAUSTED", Reply(body));
    }

    [Fact]
    public async Task Conversation_is_kept_on_the_server_and_can_be_reset()
    {
        using var app = new AssistantAppFactory();
        var client = await app.CustomerAsync();
        await AskAsync(client, "first");
        var (_, body) = await AskAsync(client, "second");
        Assert.Contains("turns: 3", Reply(body)); // first question + answer + this question

        var history = await client.GetFromJsonAsync<JsonElement>("/Assistant/History");
        Assert.Equal(4, history.GetArrayLength());
    }

    [Fact]
    public async Task Requests_need_an_antiforgery_token_and_a_customer_login()
    {
        using var app = new AssistantAppFactory();
        var customer = await app.CustomerAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await AskAsync(customer, "hello", withToken: false)).Res.StatusCode);

        var admin = await app.AdminAsync();
        var res = await admin.PostAsJsonAsync("/Assistant/Send", new { message = "hello" });
        Assert.NotEqual(HttpStatusCode.OK, res.StatusCode);
        Assert.DoesNotContain("assistant-panel", await admin.GetStringAsync("/Admin"));
    }
}
