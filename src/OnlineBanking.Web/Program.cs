using System.Text.Encodings.Web;
using System.Text.Unicode;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.WebEncoders;
using OnlineBanking.Web.Data;
using OnlineBanking.Web.Entities;
using OnlineBanking.Web.Infrastructure;
using OnlineBanking.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// ---------- Services ----------

builder.Services.AddControllersWithViews(o =>
{
    // Every POST form must carry the antiforgery token (CSRF protection)
    o.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
    o.Filters.Add<NotFoundFilter>();
})
// Validation runs on the server only; this also stops the empty <li style="display:none"> placeholder,
// which the Content-Security-Policy (no inline styles) would block
.AddViewOptions(o => o.HtmlHelperOptions.ClientValidationEnabled = false);

// Read the connection string lazily so tests and hosting can override it
builder.Services.AddDbContext<AppDbContext>((sp, o) =>
    o.UseSqlite(sp.GetRequiredService<IConfiguration>().GetConnectionString("Default") ?? "Data Source=onlinebanking.db"));

// Write ₹ and Indian-language names as-is in HTML instead of &#x20B9; escapes
builder.Services.Configure<WebEncoderOptions>(o => o.TextEncoderSettings = new TextEncoderSettings(UnicodeRanges.All));

builder.Services.AddOptions<BankingOptions>().BindConfiguration(BankingOptions.Section);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<BankingService>();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath = "/Account/Login";
        o.LogoutPath = "/Account/Logout";
        o.AccessDeniedPath = "/Account/AccessDenied";
        o.Cookie.Name = "ob.auth";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        // Banking sessions are short: 20 minutes of inactivity signs you out
        o.ExpireTimeSpan = TimeSpan.FromMinutes(20);
        o.SlidingExpiration = true;
    });
builder.Services.AddAuthorization();
builder.Services.AddAntiforgery(o => o.Cookie.Name = "ob.csrf");
builder.Services.AddHealthChecks();

var app = builder.Build();

// ---------- Pipeline ----------

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/Home/Status/{0}");

app.Use(async (context, next) =>
{
    var h = context.Response.Headers;
    h["X-Content-Type-Options"] = "nosniff";
    h["X-Frame-Options"] = "DENY";
    h["Referrer-Policy"] = "same-origin";
    h["Content-Security-Policy"] =
        "default-src 'self'; img-src 'self' data:; style-src 'self' https://fonts.googleapis.com; font-src https://fonts.gstatic.com; " +
        "script-src 'self'; form-action 'self'; frame-ancestors 'none'; base-uri 'self'";
    await next();
});

app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health");
app.MapControllerRoute("areas", "{area:exists}/{controller=Dashboard}/{action=Index}/{id?}");
app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");

// Create the database and demo data on first start
using (var scope = app.Services.CreateScope())
{
    var sp = scope.ServiceProvider;
    await SeedData.InitializeAsync(
        sp.GetRequiredService<AppDbContext>(),
        sp.GetRequiredService<IPasswordHasher<User>>(),
        sp.GetRequiredService<TimeProvider>());
}

app.Run();

/// <summary>Exposed for integration tests (WebApplicationFactory).</summary>
public partial class Program { }
