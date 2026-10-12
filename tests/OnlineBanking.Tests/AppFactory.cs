using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OnlineBanking.Web.Data;
using OnlineBanking.Web.Entities;
using OnlineBanking.Web.Services;

namespace OnlineBanking.Tests;

/// <summary>Runs the real app in memory against a throwaway SQLite file with the demo seed data.</summary>
public partial class AppFactory : WebApplicationFactory<Program>
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"onlinebanking-test-{Guid.NewGuid():N}.db");

    /// <summary>Extra configuration, e.g. ["Assistant:DailyMessageLimit"] = "2". Set before the first request.</summary>
    public Dictionary<string, string> Settings { get; } = [];

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", $"Data Source={_dbPath}");
        foreach (var (key, value) in Settings)
            builder.UseSetting(key, value);
    }

    /// <summary>A browser-like client: keeps cookies, doesn't follow redirects (so tests can assert them).</summary>
    public HttpClient Browser() => CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

    public async Task<HttpClient> SignedInAsync(string email, string password)
    {
        var client = Browser();
        var res = await PostFormAsync(client, "/Account/Login", new() { ["Email"] = email, ["Password"] = password });
        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
        return client;
    }

    public Task<HttpClient> CustomerAsync() => SignedInAsync(SeedData.CustomerEmail, SeedData.CustomerPassword);
    public Task<HttpClient> AdminAsync() => SignedInAsync(SeedData.AdminEmail, SeedData.AdminPassword);

    /// <summary>GETs the page holding the form, then posts it with the antiforgery token, like a browser.</summary>
    public static async Task<HttpResponseMessage> PostFormAsync(HttpClient client, string url, Dictionary<string, string> fields, string? formPage = null)
    {
        var page = await client.GetStringAsync(formPage ?? url);
        var token = TokenRegex().Match(page);
        Assert.True(token.Success, $"No antiforgery token on {formPage ?? url}");
        fields["__RequestVerificationToken"] = WebUtility.HtmlDecode(token.Groups[1].Value);
        return await client.PostAsync(url, new FormUrlEncodedContent(fields));
    }

    public async Task<T> WithScopeAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        using var scope = Services.CreateScope();
        return await action(scope.ServiceProvider);
    }

    public Task<Account> AccountAsync(string number) =>
        WithScopeAsync(sp => sp.GetRequiredService<AppDbContext>().Accounts.AsNoTracking().Include(a => a.User).FirstAsync(a => a.AccountNumber == number));

    public Task<int> UserIdAsync(string email) =>
        WithScopeAsync(sp => sp.GetRequiredService<AppDbContext>().Users.Where(u => u.Email == email).Select(u => u.Id).FirstAsync());

    /// <summary>Runs a banking operation in its own scope (= its own DbContext, like a separate request).</summary>
    public Task<T> BankAsync<T>(Func<BankingService, Task<T>> action) =>
        WithScopeAsync(sp => action(sp.GetRequiredService<BankingService>()));

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenRegex();

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch { /* best effort */ }
    }
}
