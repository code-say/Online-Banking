using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OnlineBanking.Web.Data;
using OnlineBanking.Web.Entities;
using OnlineBanking.Web.Infrastructure;
using OnlineBanking.Web.Models;

namespace OnlineBanking.Web.Controllers;

/// <summary>Customers apply for a new savings or current account; an admin reviews it.</summary>
[Authorize(Roles = Roles.Customer)]
public class ApplicationsController(AppDbContext db, TimeProvider clock) : Controller
{
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var userId = User.GetUserId();
        var apps = await db.Applications.AsNoTracking()
            .Include(a => a.Account)
            .Where(a => a.UserId == userId)
            .OrderByDescending(a => a.SubmittedAt)
            .ToListAsync(ct);
        return View(apps);
    }

    [HttpGet]
    public async Task<IActionResult> Create(CancellationToken ct)
    {
        var userId = User.GetUserId();
        var user = await db.Users.AsNoTracking().FirstAsync(u => u.Id == userId, ct);

        // Pre-fill from the last application (KYC rarely changes) or from the profile
        var last = await db.Applications.AsNoTracking().Where(a => a.UserId == userId).OrderByDescending(a => a.SubmittedAt).FirstOrDefaultAsync(ct);
        var parts = user.FullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var model = last is null
            ? new ApplicationViewModel
            {
                FirstName = parts.FirstOrDefault() ?? "",
                LastName = parts.Length > 1 ? parts[^1] : "",
                MiddleName = parts.Length > 2 ? string.Join(' ', parts[1..^1]) : null,
                Mobile = user.Phone,
            }
            : new ApplicationViewModel
            {
                AccountType = last.AccountType == AccountType.Savings ? AccountType.Current : AccountType.Savings,
                Salutation = last.Title, FirstName = last.FirstName, MiddleName = last.MiddleName, LastName = last.LastName,
                FatherName = last.FatherName, DateOfBirth = last.DateOfBirth, Mobile = last.Mobile,
                AddressLine1 = last.AddressLine1, AddressLine2 = last.AddressLine2, Landmark = last.Landmark,
                City = last.City, State = last.State, Pincode = last.Pincode,
                Occupation = last.Occupation, SourceOfIncome = last.SourceOfIncome, AnnualIncome = last.AnnualIncome,
            };
        return View(model);
    }

    [HttpPost]
    public async Task<IActionResult> Create(ApplicationViewModel model, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var today = DateOnly.FromDateTime(Format.ToIst(clock.GetUtcNow().UtcDateTime));

        if (model.DateOfBirth is { } dob && (dob > today.AddYears(-18) || dob < today.AddYears(-110)))
            ModelState.AddModelError(nameof(model.DateOfBirth), "You must be at least 18 to open an account online.");

        if (await db.Applications.AnyAsync(a => a.UserId == userId && a.Status == ApplicationStatus.Pending && a.AccountType == model.AccountType, ct))
            ModelState.AddModelError("", $"You already have a {model.AccountType.ToString().ToLowerInvariant()} account application under review.");

        if (!ModelState.IsValid)
        {
            ModelState.Remove(nameof(model.Aadhaar));
            model.Aadhaar = ""; // don't echo the full Aadhaar number back
            return View(model);
        }

        var aadhaar = new string(model.Aadhaar.Where(char.IsAsciiDigit).ToArray());
        db.Applications.Add(new AccountApplication
        {
            UserId = userId,
            AccountType = model.AccountType,
            Title = model.Salutation.Trim(),
            FirstName = model.FirstName.Trim(),
            MiddleName = model.MiddleName?.Trim() ?? "",
            LastName = model.LastName.Trim(),
            FatherName = model.FatherName.Trim(),
            DateOfBirth = model.DateOfBirth!.Value,
            Mobile = model.Mobile.Trim(),
            AadhaarLast4 = aadhaar[^4..],
            AddressLine1 = model.AddressLine1.Trim(),
            AddressLine2 = model.AddressLine2?.Trim() ?? "",
            Landmark = model.Landmark?.Trim() ?? "",
            City = model.City.Trim(),
            State = model.State.Trim(),
            Pincode = model.Pincode.Trim(),
            Occupation = model.Occupation.Trim(),
            SourceOfIncome = model.SourceOfIncome.Trim(),
            AnnualIncome = model.AnnualIncome!.Value,
            SubmittedAt = clock.GetUtcNow().UtcDateTime,
        });
        await db.SaveChangesAsync(ct);

        TempData.Flash("Application submitted. You'll see your new account here as soon as the bank approves it.");
        return RedirectToAction(nameof(Index));
    }
}
