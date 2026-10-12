using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OnlineBanking.Web.Data;

namespace OnlineBanking.Tests;

/// <summary>Pages, auth and security through the real HTTP pipeline.</summary>
public class WebTests
{
    [Fact]
    public async Task Home_and_health_are_up()
    {
        using var app = new AppFactory();
        var client = app.Browser();

        var home = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, home.StatusCode);
        Assert.Contains("Your bank account, online.", await home.Content.ReadAsStringAsync());
        Assert.Equal("DENY", home.Headers.GetValues("X-Frame-Options").Single());

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
    }

    [Fact]
    public async Task Customer_pages_need_a_login()
    {
        using var app = new AppFactory();
        var res = await app.Browser().GetAsync("/Dashboard");
        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
        Assert.StartsWith("/Account/Login", res.Headers.Location!.PathAndQuery);
    }

    [Fact]
    public async Task Forms_without_an_antiforgery_token_are_rejected()
    {
        using var app = new AppFactory();
        var res = await app.Browser().PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = SeedData.CustomerEmail,
            ["Password"] = SeedData.CustomerPassword,
        }));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Wrong_password_shows_an_error()
    {
        using var app = new AppFactory();
        var res = await AppFactory.PostFormAsync(app.Browser(), "/Account/Login", new() { ["Email"] = SeedData.CustomerEmail, ["Password"] = "nope-12345" });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Contains("Incorrect email or password.", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Customer_signs_in_and_sees_accounts()
    {
        using var app = new AppFactory();
        var client = await app.CustomerAsync();
        var html = await client.GetStringAsync("/Dashboard");
        Assert.Contains("Aarav", html);
        Assert.Contains("5010 0012 3456", html);
        Assert.Contains("Recent activity", html);
    }

    [Fact]
    public async Task Customer_cant_open_admin_pages_or_other_peoples_accounts()
    {
        using var app = new AppFactory();
        var client = await app.CustomerAsync();

        var admin = await client.GetAsync("/Admin");
        Assert.Equal(HttpStatusCode.Redirect, admin.StatusCode);
        Assert.StartsWith("/Account/AccessDenied", admin.Headers.Location!.PathAndQuery);

        var rahuls = await app.AccountAsync(SeedData.PayeeSavings);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/Accounts/Details/{rahuls.Id}")).StatusCode);
    }

    [Fact]
    public async Task Transfer_through_the_form_shows_a_receipt()
    {
        using var app = new AppFactory();
        var client = await app.CustomerAsync();
        var savings = await app.AccountAsync(SeedData.CustomerSavings);

        var form = await client.GetStringAsync("/Transfer");
        var requestId = System.Text.RegularExpressions.Regex.Match(form, "name=\"RequestId\" value=\"([^\"]+)\"").Groups[1].Value;
        Assert.NotEmpty(requestId);

        var res = await AppFactory.PostFormAsync(client, "/Transfer", new()
        {
            ["FromAccountId"] = savings.Id.ToString(),
            ["ToAccountNumber"] = SeedData.PayeeSavings,
            ["Amount"] = "750.25",
            ["Remarks"] = "Books",
            ["Pin"] = SeedData.CustomerPin,
            ["RequestId"] = requestId,
        });
        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);

        var receipt = await client.GetStringAsync(res.Headers.Location!.OriginalString);
        Assert.Contains("Money sent", receipt);
        Assert.Contains("₹750.25", receipt);
        Assert.Contains("Rahul Verma", receipt);
    }

    [Fact]
    public async Task Payee_lookup_shows_only_a_masked_name()
    {
        using var app = new AppFactory();
        var client = await app.CustomerAsync();
        var json = await client.GetStringAsync($"/Transfer/Lookup?number={SeedData.PayeeSavings}");
        Assert.Contains("\"Rahul V.\"", json);
        Assert.DoesNotContain("Verma", json);
    }

    [Fact]
    public async Task Statement_downloads_as_csv()
    {
        using var app = new AppFactory();
        var client = await app.CustomerAsync();
        var savings = await app.AccountAsync(SeedData.CustomerSavings);

        // The statement page links to the CSV for the same account and filters
        var page = await client.GetStringAsync($"/Accounts/Details/{savings.Id}?Type=credit");
        Assert.Contains($"href=\"/Accounts/Download/{savings.Id}?Type=credit\"", page);

        var res = await client.GetAsync($"/Accounts/Download/{savings.Id}?Type=credit");
        Assert.Equal("text/csv", res.Content.Headers.ContentType!.MediaType);
        var csv = await res.Content.ReadAsStringAsync();
        Assert.Contains("Date (IST),Reference,Description,Counterparty,Debit,Credit,Balance", csv);
        Assert.Contains("Salary credit", csv);
        Assert.DoesNotContain("ATM withdrawal", csv);
    }

    [Fact]
    public async Task New_customer_registers_and_applies_then_admin_approves()
    {
        using var app = new AppFactory();
        var customer = app.Browser();

        var reg = await AppFactory.PostFormAsync(customer, "/Account/Register", new()
        {
            ["FullName"] = "Kavya Iyer",
            ["Email"] = "kavya@example.com",
            ["Phone"] = "+91 98450 22334",
            ["Password"] = "Kavya@2026",
            ["ConfirmPassword"] = "Kavya@2026",
        });
        Assert.Equal(HttpStatusCode.Redirect, reg.StatusCode);

        var apply = await AppFactory.PostFormAsync(customer, "/Applications/Create", new()
        {
            ["AccountType"] = "Savings",
            ["Salutation"] = "Ms",
            ["FirstName"] = "Kavya",
            ["LastName"] = "Iyer",
            ["FatherName"] = "Ramesh Iyer",
            ["DateOfBirth"] = "1996-05-12",
            ["Mobile"] = "+91 98450 22334",
            ["Aadhaar"] = "1234 5678 9012",
            ["AddressLine1"] = "14 MG Road",
            ["City"] = "Bengaluru",
            ["State"] = "Karnataka",
            ["Pincode"] = "560001",
            ["Occupation"] = "Data analyst",
            ["SourceOfIncome"] = "Salary",
            ["AnnualIncome"] = "900000",
            ["Confirm"] = "true",
        });
        Assert.Equal(HttpStatusCode.Redirect, apply.StatusCode);

        var admin = await app.AdminAsync();
        var list = await admin.GetStringAsync("/Admin/Applications");
        Assert.Contains("Kavya", list);
        var appId = await app.WithScopeAsync(sp => sp.GetRequiredService<AppDbContext>().Applications
            .Where(a => a.FirstName == "Kavya").Select(a => a.Id).FirstAsync());

        var approve = await AppFactory.PostFormAsync(admin, $"/Admin/Applications/Approve/{appId}",
            new() { ["openingDeposit"] = "5000", ["note"] = "KYC verified." }, formPage: $"/Admin/Applications/Details/{appId}");
        Assert.Equal(HttpStatusCode.Redirect, approve.StatusCode);
        Assert.StartsWith("/Admin/Accounts/Details/", approve.Headers.Location!.OriginalString);

        var dashboard = await customer.GetStringAsync("/Dashboard");
        var at = Math.Max(0, dashboard.IndexOf("grid-cards", StringComparison.Ordinal));
        var snippet = dashboard.Substring(at, Math.Min(900, dashboard.Length - at)).Replace('\n', ' ');
        Assert.True(dashboard.Contains("₹5,000.00"), "DASHBOARD SNIPPET: " + snippet);
    }
}
