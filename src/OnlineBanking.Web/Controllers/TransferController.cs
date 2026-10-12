using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OnlineBanking.Web.Data;
using OnlineBanking.Web.Entities;
using OnlineBanking.Web.Infrastructure;
using OnlineBanking.Web.Models;
using OnlineBanking.Web.Services;

namespace OnlineBanking.Web.Controllers;

/// <summary>Send money to any account in the bank (own accounts included).</summary>
[Authorize(Roles = Roles.Customer)]
public class TransferController(AppDbContext db, BankingService banking) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(int? from, string? to, decimal? amount, string? remarks, string? src, CancellationToken ct)
    {
        // from/to/amount/remarks can be prefilled (e.g. by the assistant); the customer still reviews and enters the PIN
        var model = new TransferViewModel
        {
            FromAccountId = from,
            ToAccountNumber = to ?? "",
            Amount = amount is > 0 ? amount : null,
            Remarks = remarks is { Length: > 0 } r ? r[..Math.Min(r.Length, 60)] : null,
            RequestId = Guid.NewGuid(),
        };
        ViewBag.FromAssistant = src == "assistant";
        await FillAsync(model, ct);
        if (model.FromAccountId is null && model.Accounts.Count > 0)
            model.FromAccountId = model.Accounts[0].Id;
        return View(model);
    }

    [HttpPost]
    public async Task<IActionResult> Index(TransferViewModel model, CancellationToken ct)
    {
        if (ModelState.IsValid)
        {
            try
            {
                var tx = await banking.TransferAsync(User.GetUserId(), new TransferCommand(
                    model.FromAccountId!.Value, model.ToAccountNumber, model.Amount!.Value, model.Remarks, model.Pin, model.RequestId), ct);
                return RedirectToAction(nameof(Receipt), new { id = tx.Id });
            }
            catch (BankingException ex) when (ex is not NotFoundException)
            {
                ModelState.AddModelError("", ex.Message);
            }
        }

        // Never send the PIN back to the browser
        ModelState.Remove(nameof(model.Pin));
        model.Pin = "";
        await FillAsync(model, ct);
        return View(model);
    }

    public async Task<IActionResult> Receipt(int id, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var tx = await db.Transactions.AsNoTracking().Include(t => t.Account)
                     .FirstOrDefaultAsync(t => t.Id == id && t.Type == TransactionType.TransferOut && t.Account!.UserId == userId, ct)
                 ?? throw new NotFoundException();
        return View(new ReceiptViewModel { Transaction = tx, From = tx.Account! });
    }

    /// <summary>Payee name check before sending (masked, e.g. "Rahul V."). Used by the transfer form.</summary>
    [HttpGet]
    public async Task<IActionResult> Lookup(string number, CancellationToken ct)
    {
        var normalized = BankingService.Normalize(number);
        if (normalized.Length != 12)
            return Json(new { found = false });
        var name = await banking.LookupPayeeAsync(normalized, ct);
        return Json(new { found = name is not null, name });
    }

    private async Task FillAsync(TransferViewModel model, CancellationToken ct)
    {
        var userId = User.GetUserId();
        model.Accounts = await db.Accounts.AsNoTracking()
            .Where(a => a.UserId == userId && a.Status == AccountStatus.Active)
            .OrderBy(a => a.OpenedAt)
            .ToListAsync(ct);

        var remaining = new Dictionary<int, decimal>();
        foreach (var a in model.Accounts)
            remaining[a.Id] = Math.Max(0, banking.DailyLimit - await banking.SentTodayAsync(a.Id, ct));
        model.RemainingToday = remaining;
        model.DailyLimit = banking.DailyLimit;
        model.HasPin = await db.Users.AnyAsync(u => u.Id == userId && u.TransactionPinHash != null, ct);
    }
}
