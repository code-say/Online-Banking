using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OnlineBanking.Web.Data;
using OnlineBanking.Web.Entities;
using OnlineBanking.Web.Infrastructure;
using OnlineBanking.Web.Models;
using OnlineBanking.Web.Services;

namespace OnlineBanking.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = Roles.Admin)]
public abstract class AdminController : Controller;

public class DashboardController(AppDbContext db, TimeProvider clock) : AdminController
{
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var istToday = Format.ToIst(clock.GetUtcNow().UtcDateTime).Date;
        var startUtc = DateTime.SpecifyKind(istToday - Format.IstOffset, DateTimeKind.Utc);

        var accounts = await db.Accounts.AsNoTracking().Select(a => new { a.Status, a.Balance }).ToListAsync(ct);
        var today = await db.Transactions.AsNoTracking()
            .Where(t => t.CreatedAt >= startUtc && t.Type != TransactionType.TransferIn) // count each transfer once
            .Select(t => t.Amount).ToListAsync(ct);

        return View(new AdminDashboardViewModel
        {
            Customers = await db.Users.CountAsync(u => u.Role == Roles.Customer, ct),
            ActiveAccounts = accounts.Count(a => a.Status == AccountStatus.Active),
            FrozenAccounts = accounts.Count(a => a.Status == AccountStatus.Frozen),
            TotalDeposits = accounts.Where(a => a.Status != AccountStatus.Closed).Sum(a => a.Balance),
            PendingApplications = await db.Applications.CountAsync(a => a.Status == ApplicationStatus.Pending, ct),
            UnreadMessages = await db.ContactMessages.CountAsync(m => !m.IsRead, ct),
            TransactionsToday = today.Count,
            VolumeToday = today.Sum(),
            Recent = await db.Transactions.AsNoTracking()
                .Include(t => t.Account).ThenInclude(a => a!.User)
                .Where(t => t.Type != TransactionType.TransferIn)
                .OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id)
                .Take(10).ToListAsync(ct),
        });
    }
}

public class ApplicationsController(AppDbContext db, BankingService banking) : AdminController
{
    /// <param name="status">Pending (default), Approved, Rejected or "all".</param>
    public async Task<IActionResult> Index(string? status, CancellationToken ct)
    {
        ApplicationStatus? filter = string.Equals(status, "all", StringComparison.OrdinalIgnoreCase) ? null
            : Enum.TryParse<ApplicationStatus>(status, true, out var parsed) ? parsed : ApplicationStatus.Pending;
        var q = db.Applications.AsNoTracking().Include(a => a.User).Include(a => a.Account).AsQueryable();
        if (filter is { } s)
            q = q.Where(a => a.Status == s);
        ViewBag.Status = filter;
        return View(await q.OrderByDescending(a => a.SubmittedAt).ToListAsync(ct));
    }

    public async Task<IActionResult> Details(int id, CancellationToken ct) =>
        View(await db.Applications.AsNoTracking().Include(a => a.User).Include(a => a.Account)
                 .FirstOrDefaultAsync(a => a.Id == id, ct) ?? throw new NotFoundException());

    [HttpPost]
    public async Task<IActionResult> Approve(int id, decimal? openingDeposit, string? note, CancellationToken ct)
    {
        try
        {
            var account = await banking.ApproveApplicationAsync(id, openingDeposit ?? 0, note, ct);
            TempData.Flash($"Approved. Account {Format.AccountNumber(account.AccountNumber)} is open.");
            return RedirectToAction("Details", "Accounts", new { id = account.Id });
        }
        catch (BankingException ex) when (ex is not NotFoundException)
        {
            TempData.FlashError(ex.Message);
            return RedirectToAction(nameof(Details), new { id });
        }
    }

    [HttpPost]
    public async Task<IActionResult> Reject(int id, string? note, CancellationToken ct)
    {
        try
        {
            await banking.RejectApplicationAsync(id, note ?? "", ct);
            TempData.Flash("Application rejected. The customer will see your reason.");
            return RedirectToAction(nameof(Index));
        }
        catch (BankingException ex) when (ex is not NotFoundException)
        {
            TempData.FlashError(ex.Message);
            return RedirectToAction(nameof(Details), new { id });
        }
    }
}

public class AccountsController(AppDbContext db, BankingService banking) : AdminController
{
    public async Task<IActionResult> Index(string? q, AccountStatus? status, CancellationToken ct)
    {
        var query = db.Accounts.AsNoTracking().Include(a => a.User).AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim().ToLower();
            var digits = BankingService.Normalize(q);
            query = query.Where(a => a.User!.FullName.ToLower().Contains(term) || a.User.Email.Contains(term) ||
                                     (digits.Length >= 4 && a.AccountNumber.Contains(digits)));
        }
        if (status is { } s)
            query = query.Where(a => a.Status == s);

        ViewBag.Q = q;
        ViewBag.Status = status;
        return View(await query.OrderBy(a => a.User!.FullName).ThenBy(a => a.AccountNumber).ToListAsync(ct));
    }

    public async Task<IActionResult> Details(int id, CancellationToken ct) => View(await BuildAsync(id, ct));

    public async Task<IActionResult> Statement(int id, [FromQuery] StatementFilter filter, CancellationToken ct)
    {
        var account = await LoadAsync(id, ct);
        return View("~/Views/Accounts/Details.cshtml", await Statements.BuildAsync(db, account, filter, admin: true, ct));
    }

    public async Task<IActionResult> Download(int id, [FromQuery] StatementFilter filter, CancellationToken ct)
    {
        var account = await LoadAsync(id, ct);
        return File(await Statements.CsvAsync(db, account, filter, ct), "text/csv; charset=utf-8", $"statement-{account.AccountNumber}.csv");
    }

    [HttpPost]
    public Task<IActionResult> Deposit(int id, [Bind(Prefix = "Cash")] CashViewModel model, CancellationToken ct) =>
        CashAsync(id, model, deposit: true, ct);

    [HttpPost]
    public Task<IActionResult> Withdraw(int id, [Bind(Prefix = "Cash")] CashViewModel model, CancellationToken ct) =>
        CashAsync(id, model, deposit: false, ct);

    private async Task<IActionResult> CashAsync(int id, CashViewModel model, bool deposit, CancellationToken ct)
    {
        if (ModelState.IsValid)
        {
            try
            {
                var tx = deposit
                    ? await banking.DepositAsync(id, model.Amount!.Value, model.Description, ct)
                    : await banking.WithdrawAsync(id, model.Amount!.Value, model.Description, ct);
                TempData.Flash($"{(deposit ? "Deposited" : "Withdrew")} {Format.Money(tx.Amount)}. New balance {Format.Money(tx.BalanceAfter)}. Ref {tx.Reference}.");
                return RedirectToAction(nameof(Details), new { id });
            }
            catch (BankingException ex) when (ex is not NotFoundException)
            {
                ModelState.AddModelError("Cash", ex.Message);
            }
        }

        var vm = await BuildAsync(id, ct);
        return View(nameof(Details), new AdminAccountViewModel { Account = vm.Account, Recent = vm.Recent, Cash = model });
    }

    [HttpPost]
    public async Task<IActionResult> SetStatus(int id, AccountStatus status, CancellationToken ct)
    {
        try
        {
            await banking.SetStatusAsync(id, status, ct);
            TempData.Flash(status switch
            {
                AccountStatus.Frozen => "Account frozen. No money can move in or out until it's unfrozen.",
                AccountStatus.Closed => "Account closed.",
                _ => "Account is active again.",
            });
        }
        catch (BankingException ex) when (ex is not NotFoundException)
        {
            TempData.FlashError(ex.Message);
        }
        return RedirectToAction(nameof(Details), new { id });
    }

    private async Task<Account> LoadAsync(int id, CancellationToken ct) =>
        await db.Accounts.AsNoTracking().Include(a => a.User).FirstOrDefaultAsync(a => a.Id == id, ct) ?? throw new NotFoundException();

    private async Task<AdminAccountViewModel> BuildAsync(int id, CancellationToken ct)
    {
        var account = await LoadAsync(id, ct);
        var recent = await db.Transactions.AsNoTracking().Where(t => t.AccountId == id)
            .OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id).Take(10).ToListAsync(ct);
        return new AdminAccountViewModel { Account = account, Recent = recent };
    }
}

public class CustomersController(AppDbContext db) : AdminController
{
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var users = await db.Users.AsNoTracking()
            .Include(u => u.Accounts)
            .Include(u => u.Applications)
            .Where(u => u.Role == Roles.Customer)
            .OrderBy(u => u.FullName)
            .ToListAsync(ct);

        return View(users.Select(u => new CustomerRow
        {
            User = u,
            Accounts = u.Accounts.Count(a => a.Status != AccountStatus.Closed),
            Balance = u.Accounts.Where(a => a.Status != AccountStatus.Closed).Sum(a => a.Balance),
            HasPendingApplication = u.Applications.Any(a => a.Status == ApplicationStatus.Pending),
        }).ToList());
    }

    public async Task<IActionResult> Details(int id, CancellationToken ct) =>
        View(await db.Users.AsNoTracking()
                 .Include(u => u.Accounts)
                 .Include(u => u.Applications)
                 .FirstOrDefaultAsync(u => u.Id == id && u.Role == Roles.Customer, ct) ?? throw new NotFoundException());
}

public class MessagesController(AppDbContext db) : AdminController
{
    public async Task<IActionResult> Index(CancellationToken ct) =>
        View(await db.ContactMessages.AsNoTracking().OrderBy(m => m.IsRead).ThenByDescending(m => m.CreatedAt).ToListAsync(ct));

    [HttpPost]
    public async Task<IActionResult> MarkRead(int id, bool read, CancellationToken ct)
    {
        var m = await db.ContactMessages.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException();
        m.IsRead = read;
        await db.SaveChangesAsync(ct);
        return RedirectToAction(nameof(Index));
    }
}
