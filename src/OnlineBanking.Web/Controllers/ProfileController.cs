using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OnlineBanking.Web.Data;
using OnlineBanking.Web.Infrastructure;
using OnlineBanking.Web.Models;
using OnlineBanking.Web.Services;

namespace OnlineBanking.Web.Controllers;

/// <summary>Profile and security: password and transaction PIN.</summary>
[Authorize]
public class ProfileController(AppDbContext db, AuthService auth, BankingService banking) : Controller
{
    public async Task<IActionResult> Index(CancellationToken ct) => View(await BuildAsync(null, null, ct));

    [HttpPost]
    public async Task<IActionResult> ChangePassword([Bind(Prefix = "Password")] ChangePasswordViewModel form, CancellationToken ct)
    {
        if (ModelState.IsValid)
        {
            try
            {
                await auth.ChangePasswordAsync(User.GetUserId(), form.CurrentPassword, form.NewPassword, ct);
                TempData.Flash("Password changed.");
                return RedirectToAction(nameof(Index));
            }
            catch (BankingException ex)
            {
                ModelState.AddModelError("Password." + nameof(form.CurrentPassword), ex.Message);
            }
        }
        return View(nameof(Index), await BuildAsync(new ChangePasswordViewModel(), null, ct));
    }

    [HttpPost]
    public async Task<IActionResult> SetPin([Bind(Prefix = "Pin")] SetPinViewModel form, CancellationToken ct)
    {
        if (ModelState.IsValid)
        {
            try
            {
                await banking.SetPinAsync(User.GetUserId(), form.CurrentPassword, form.NewPin, ct);
                TempData.Flash("Transaction PIN saved. You'll need it for every transfer.");
                return RedirectToAction(nameof(Index));
            }
            catch (BankingException ex)
            {
                ModelState.AddModelError("Pin." + nameof(form.NewPin), ex.Message);
            }
        }
        return View(nameof(Index), await BuildAsync(null, new SetPinViewModel(), ct));
    }

    private async Task<ProfileViewModel> BuildAsync(ChangePasswordViewModel? password, SetPinViewModel? pin, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var user = await db.Users.AsNoTracking().FirstAsync(u => u.Id == userId, ct);
        return new ProfileViewModel { User = user, Password = password ?? new(), Pin = pin ?? new() };
    }
}
