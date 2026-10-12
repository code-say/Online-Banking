using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using OnlineBanking.Web.Data;
using OnlineBanking.Web.Entities;
using OnlineBanking.Web.Models;

namespace OnlineBanking.Web.Infrastructure;

/// <summary>Filtering, paging and CSV export of an account's ledger (shared by customer and admin views).</summary>
public static class Statements
{
    public const int PageSize = 15;

    public static IQueryable<Transaction> Filter(AppDbContext db, int accountId, StatementFilter f)
    {
        var q = db.Transactions.AsNoTracking().Where(t => t.AccountId == accountId);

        if (f.From is { } from)
            q = q.Where(t => t.CreatedAt >= StartOfIstDay(from));
        if (f.To is { } to)
            q = q.Where(t => t.CreatedAt < StartOfIstDay(to.AddDays(1)));

        q = f.Type switch
        {
            "credit" => q.Where(t => t.Type == TransactionType.Deposit || t.Type == TransactionType.TransferIn),
            "debit" => q.Where(t => t.Type == TransactionType.Withdrawal || t.Type == TransactionType.TransferOut),
            _ => q,
        };
        return q;
    }

    public static async Task<StatementViewModel> BuildAsync(AppDbContext db, Account account, StatementFilter f, bool admin, CancellationToken ct)
    {
        var q = Filter(db, account.Id, f);
        var all = await q.Select(t => new { t.Type, t.Amount }).ToListAsync(ct);
        var pageCount = Math.Max(1, (int)Math.Ceiling(all.Count / (double)PageSize));
        var page = Math.Clamp(f.Page, 1, pageCount);

        var rows = await q.OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id)
            .Skip((page - 1) * PageSize).Take(PageSize)
            .ToListAsync(ct);

        return new StatementViewModel
        {
            Account = account,
            Filter = f,
            Transactions = rows,
            TotalIn = all.Where(t => IsCredit(t.Type)).Sum(t => t.Amount),
            TotalOut = all.Where(t => !IsCredit(t.Type)).Sum(t => t.Amount),
            Page = page,
            PageCount = pageCount,
            TotalCount = all.Count,
            IsAdminView = admin,
        };
    }

    public static async Task<byte[]> CsvAsync(AppDbContext db, Account account, StatementFilter f, CancellationToken ct)
    {
        var rows = await Filter(db, account.Id, f).OrderBy(t => t.CreatedAt).ThenBy(t => t.Id).ToListAsync(ct);
        var inv = CultureInfo.InvariantCulture;

        var sb = new StringBuilder();
        sb.AppendLine($"Account,{account.AccountNumber}");
        sb.AppendLine($"Holder,{Csv(account.User?.FullName ?? "")}");
        sb.AppendLine($"Period,{f.From?.ToString("yyyy-MM-dd", inv) ?? "start"} to {f.To?.ToString("yyyy-MM-dd", inv) ?? "today"}");
        sb.AppendLine();
        sb.AppendLine("Date (IST),Reference,Description,Counterparty,Debit,Credit,Balance");
        foreach (var t in rows)
        {
            var credit = IsCredit(t.Type);
            var party = string.IsNullOrEmpty(t.CounterpartyAccountNumber) ? "" : $"{t.CounterpartyName} ({Format.Masked(t.CounterpartyAccountNumber)})";
            sb.Append(Format.ToIst(t.CreatedAt).ToString("yyyy-MM-dd HH:mm", inv)).Append(',')
              .Append(t.Reference).Append(',')
              .Append(Csv(t.Description)).Append(',')
              .Append(Csv(party)).Append(',')
              .Append(credit ? "" : t.Amount.ToString("0.00", inv)).Append(',')
              .Append(credit ? t.Amount.ToString("0.00", inv) : "").Append(',')
              .AppendLine(t.BalanceAfter.ToString("0.00", inv));
        }

        // UTF-8 BOM so Excel shows ₹ and names correctly
        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(sb.ToString())];
    }

    public static bool IsCredit(TransactionType type) => type is TransactionType.Deposit or TransactionType.TransferIn;

    private static DateTime StartOfIstDay(DateOnly day) =>
        DateTime.SpecifyKind(day.ToDateTime(TimeOnly.MinValue) - Format.IstOffset, DateTimeKind.Utc);

    /// <summary>Quote a CSV field, and neutralise spreadsheet formulas (CSV injection).</summary>
    private static string Csv(string value)
    {
        if (value.Length > 0 && "=+-@\t\r".Contains(value[0]))
            value = "'" + value;
        return value.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
    }
}
