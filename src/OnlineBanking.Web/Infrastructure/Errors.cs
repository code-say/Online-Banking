namespace OnlineBanking.Web.Infrastructure;

/// <summary>A business rule was broken (insufficient funds, wrong PIN, ...). The message is safe to show to the user.</summary>
public class BankingException(string message) : Exception(message);

/// <summary>The record doesn't exist, or belongs to someone else (both look the same, so ids can't be probed).</summary>
public sealed class NotFoundException(string message = "Not found.") : BankingException(message);
