using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OnlineBanking.Web.Entities;
using OnlineBanking.Web.Infrastructure;
using OnlineBanking.Web.Models;
using OnlineBanking.Web.Services;

namespace OnlineBanking.Web.Controllers;

/// <summary>Sign in, sign up and sign out (the "Account" here is the login, not a bank account).</summary>
public class AccountController(AuthService auth) : Controller
{
    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToLocal(returnUrl);
        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    [HttpPost]
    public async Task<IActionResult> Login(LoginViewModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return View(model);

        var user = await auth.ValidateAsync(model.Email, model.Password, ct);
        if (user is null)
        {
            ModelState.AddModelError("", "Incorrect email or password.");
            model.Password = "";
            return View(model);
        }

        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, AuthService.CreatePrincipal(user));
        if (!string.IsNullOrEmpty(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
            return LocalRedirect(model.ReturnUrl);
        return user.Role == Roles.Admin
            ? RedirectToAction("Index", "Dashboard", new { area = "Admin" })
            : RedirectToAction("Index", "Dashboard");
    }

    [HttpGet]
    public IActionResult Register() =>
        User.Identity?.IsAuthenticated == true ? RedirectToLocal(null) : View(new RegisterViewModel());

    [HttpPost]
    public async Task<IActionResult> Register(RegisterViewModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return View(model);

        try
        {
            var user = await auth.RegisterCustomerAsync(model.FullName, model.Email, model.Phone, model.Password, ct);
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, AuthService.CreatePrincipal(user));
            TempData.Flash($"Welcome, {user.FullName}! Apply for your first account to get started.");
            return RedirectToAction("Create", "Applications");
        }
        catch (BankingException ex)
        {
            ModelState.AddModelError(nameof(model.Email), ex.Message);
            return View(model);
        }
    }

    [HttpPost]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        TempData.Flash("You've signed out securely.");
        return RedirectToAction("Index", "Home");
    }

    public IActionResult AccessDenied() => View();

    private IActionResult RedirectToLocal(string? returnUrl)
    {
        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            return LocalRedirect(returnUrl);
        return RedirectToAction("Index", "Home");
    }
}
