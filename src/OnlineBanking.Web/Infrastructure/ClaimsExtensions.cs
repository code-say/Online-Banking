using System.Globalization;
using System.Security.Claims;
using OnlineBanking.Web.Entities;

namespace OnlineBanking.Web.Infrastructure;

public static class ClaimsExtensions
{
    public static int GetUserId(this ClaimsPrincipal user) =>
        int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new InvalidOperationException("Not signed in."), CultureInfo.InvariantCulture);

    public static bool IsAdmin(this ClaimsPrincipal user) => user.IsInRole(Roles.Admin);

    public static string DisplayName(this ClaimsPrincipal user) => user.FindFirstValue(ClaimTypes.Name) ?? "";
}

/// <summary>Display helpers shared by views.</summary>
public static class Format
{
    private static readonly CultureInfo India = CultureInfo.GetCultureInfo("en-IN");

    /// <summary>India Standard Time (no DST), used for "today" and the daily transfer limit.</summary>
    public static readonly TimeSpan IstOffset = TimeSpan.FromHours(5.5);

    public static string Money(decimal amount) =>
        (amount < 0 ? "-₹" : "₹") + Math.Abs(amount).ToString("N2", India);

    public static string AccountNumber(string number) =>
        number.Length == 12 ? $"{number[..4]} {number[4..8]} {number[8..]}" : number;

    public static string Masked(string number) => number.Length > 4 ? "XXXX" + number[^4..] : number;

    public static DateTime ToIst(DateTime utc) => System.DateTime.SpecifyKind(utc, DateTimeKind.Utc).Add(IstOffset);

    public static string Date(DateTime utc) => ToIst(utc).ToString("d MMM yyyy", India);

    public static string DateAndTime(DateTime utc) => ToIst(utc).ToString("d MMM yyyy, h:mm tt", India);

    public static string Date(DateOnly date) => date.ToString("d MMM yyyy", India);

    public static bool IsCredit(TransactionType type) => type is TransactionType.Deposit or TransactionType.TransferIn;

    /// <summary>Signed amount for statements: +₹500.00 / -₹500.00.</summary>
    public static string Signed(Transaction t) => (IsCredit(t.Type) ? "+" : "-") + Money(t.Amount);

    public static string TypeLabel(TransactionType type) => type switch
    {
        TransactionType.Deposit => "Deposit",
        TransactionType.Withdrawal => "Withdrawal",
        TransactionType.TransferIn => "Received",
        TransactionType.TransferOut => "Sent",
        _ => type.ToString(),
    };

    /// <summary>"From Rahul Verma · XXXX7654" style detail line for transfers.</summary>
    public static string Counterparty(Transaction t) => t.Type switch
    {
        TransactionType.TransferIn => $"From {t.CounterpartyName} · {Masked(t.CounterpartyAccountNumber)}",
        TransactionType.TransferOut => $"To {t.CounterpartyName} · {Masked(t.CounterpartyAccountNumber)}",
        _ => TypeLabel(t.Type),
    };

    public static string BadgeClass(Enum status) => "badge badge-" + status.ToString().ToLowerInvariant();
}
