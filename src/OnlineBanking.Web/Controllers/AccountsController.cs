using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OnlineBanking.Web.Data;
using OnlineBanking.Web.Entities;
using OnlineBanking.Web.Infrastructure;
using OnlineBanking.Web.Models;
using OnlineBanking.Web.Services;

namespace OnlineBanking.Web.Controllers;

/// <summary>A customer's own bank accounts: statement with filters and CSV download.</summary>
[Authorize(Roles = Roles.Customer)]
public class AccountsController(AppDbContext db, BankingService banking) : Controller
{
    public async Task<IActionResult> Details(int id, [FromQuery] StatementFilter filter, CancellationToken ct)
    {
        var account = await banking.GetOwnedAccountAsync(User.GetUserId(), id, ct);
        return View(await Statements.BuildAsync(db, account, filter, admin: false, ct));
    }

    public async Task<IActionResult> Download(int id, [FromQuery] StatementFilter filter, CancellationToken ct)
    {
        var account = await banking.GetOwnedAccountAsync(User.GetUserId(), id, ct);
        var bytes = await Statements.CsvAsync(db, account, filter, ct);
        return File(bytes, "text/csv; charset=utf-8", $"statement-{Format.Masked(account.AccountNumber)}-{DateTime.UtcNow:yyyyMMdd}.csv");
    }
}
