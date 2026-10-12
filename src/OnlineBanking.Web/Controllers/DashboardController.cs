using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OnlineBanking.Web.Data;
using OnlineBanking.Web.Entities;
using OnlineBanking.Web.Infrastructure;
using OnlineBanking.Web.Models;

namespace OnlineBanking.Web.Controllers;

[Authorize(Roles = Roles.Customer)]
public class DashboardController(AppDbContext db) : Controller
{
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var userId = User.GetUserId();
        var user = await db.Users.AsNoTracking().FirstAsync(u => u.Id == userId, ct);

        var accounts = await db.Accounts.AsNoTracking()
            .Where(a => a.UserId == userId && a.Status != AccountStatus.Closed)
            .OrderBy(a => a.OpenedAt)
            .ToListAsync(ct);

        var ids = accounts.Select(a => a.Id).ToList();
        var recent = await db.Transactions.AsNoTracking()
            .Include(t => t.Account)
            .Where(t => ids.Contains(t.AccountId))
            .OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id)
            .Take(8)
            .ToListAsync(ct);

        var open = await db.Applications.AsNoTracking()
            .Where(a => a.UserId == userId && (a.Status == ApplicationStatus.Pending || (a.Status == ApplicationStatus.Rejected && a.ReviewedAt > DateTime.UtcNow.AddDays(-30))))
            .OrderByDescending(a => a.SubmittedAt)
            .ToListAsync(ct);

        return View(new DashboardViewModel
        {
            Name = user.FullName,
            Accounts = accounts,
            Recent = recent,
            OpenApplications = open,
            HasPin = user.TransactionPinHash is not null,
        });
    }
}
