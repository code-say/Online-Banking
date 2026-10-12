using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OnlineBanking.Web.Data;
using OnlineBanking.Web.Entities;
using OnlineBanking.Web.Infrastructure;

namespace OnlineBanking.Web.Services;

public sealed class BankingOptions
{
    public const string Section = "Banking";

    /// <summary>Most a customer can send from one account per day (IST), in rupees.</summary>
    public decimal DailyTransferLimit { get; set; } = 200_000m;
    public int MaxPinAttempts { get; set; } = 3;
    public int PinLockMinutes { get; set; } = 15;
}

public sealed record TransferCommand(int FromAccountId, string ToAccountNumber, decimal Amount, string? Remarks, string Pin, Guid RequestId);

/// <summary>All money movement goes through here, so every rule is enforced in one place.</summary>
public sealed class BankingService(
    AppDbContext db,
    IPasswordHasher<User> hasher,
    TimeProvider clock,
    IOptions<BankingOptions> options)
{
    public const decimal MaxSingleAmount = 10_000_000m;
    private readonly BankingOptions _opt = options.Value;

    private DateTime UtcNow => clock.GetUtcNow().UtcDateTime;

    // ---------------- Queries ----------------

    /// <summary>An account the user owns; anything else is "not found".</summary>
    public async Task<Account> GetOwnedAccountAsync(int userId, int accountId, CancellationToken ct = default) =>
        await db.Accounts.Include(a => a.User).FirstOrDefaultAsync(a => a.Id == accountId && a.UserId == userId, ct)
        ?? throw new NotFoundException("Account not found.");

    /// <summary>Total sent from an account today (India time), for the daily limit.</summary>
    public async Task<decimal> SentTodayAsync(int accountId, CancellationToken ct = default)
    {
        var istToday = Format.ToIst(UtcNow).Date;
        var startUtc = DateTime.SpecifyKind(istToday - Format.IstOffset, DateTimeKind.Utc);
        var amounts = await db.Transactions
            .Where(t => t.AccountId == accountId && t.Type == TransactionType.TransferOut && t.CreatedAt >= startUtc)
            .Select(t => t.Amount)
            .ToListAsync(ct);
        return amounts.Sum();
    }

    public decimal DailyLimit => _opt.DailyTransferLimit;

    /// <summary>Confirms who an account number belongs to before sending, showing only a masked name.</summary>
    public async Task<string?> LookupPayeeAsync(string accountNumber, CancellationToken ct = default)
    {
        var number = Normalize(accountNumber);
        var account = await db.Accounts.AsNoTracking().Include(a => a.User)
            .FirstOrDefaultAsync(a => a.AccountNumber == number && a.Status == AccountStatus.Active, ct);
        return account is null ? null : MaskName(account.User!.FullName);
    }

    // ---------------- Customer: transfers and PIN ----------------

    public async Task<Transaction> TransferAsync(int userId, TransferCommand cmd, CancellationToken ct = default)
    {
        var amount = ValidateAmount(cmd.Amount);

        // Same request submitted twice (double click, refresh, retry): return the first result
        var existing = await FindByRequestIdAsync(cmd.FromAccountId, cmd.RequestId, ct);
        if (existing is not null)
            return existing;

        var from = await GetOwnedAccountAsync(userId, cmd.FromAccountId, ct);
        if (from.Status != AccountStatus.Active)
            throw new BankingException("This account is frozen. Please contact the bank.");

        var toNumber = Normalize(cmd.ToAccountNumber);
        if (toNumber == from.AccountNumber)
            throw new BankingException("You can't transfer to the same account.");

        var to = await db.Accounts.Include(a => a.User).FirstOrDefaultAsync(a => a.AccountNumber == toNumber, ct);
        if (to is null || to.Status == AccountStatus.Closed)
            throw new BankingException("No account found with that number.");
        if (to.Status != AccountStatus.Active)
            throw new BankingException("The receiving account can't accept money right now.");

        await VerifyPinAsync(from.User!, cmd.Pin, ct);

        if (amount > from.Balance)
            throw new BankingException($"Insufficient balance. Available: {Format.Money(from.Balance)}.");

        var sentToday = await SentTodayAsync(from.Id, ct);
        if (sentToday + amount > _opt.DailyTransferLimit)
            throw new BankingException(
                $"This exceeds your daily transfer limit of {Format.Money(_opt.DailyTransferLimit)}. " +
                $"You can send up to {Format.Money(Math.Max(0, _opt.DailyTransferLimit - sentToday))} more today.");

        var reference = NewReference();
        var remarks = string.IsNullOrWhiteSpace(cmd.Remarks) ? "Fund transfer" : cmd.Remarks.Trim();
        var now = UtcNow;

        from.Balance -= amount;
        to.Balance += amount;

        var outLeg = new Transaction
        {
            Reference = reference,
            Type = TransactionType.TransferOut,
            Amount = amount,
            BalanceAfter = from.Balance,
            Description = remarks,
            CounterpartyAccountNumber = to.AccountNumber,
            CounterpartyName = to.User!.FullName,
            CreatedAt = now,
            RequestId = cmd.RequestId,
            AccountId = from.Id,
        };
        db.Transactions.Add(outLeg);
        db.Transactions.Add(new Transaction
        {
            Reference = reference,
            Type = TransactionType.TransferIn,
            Amount = amount,
            BalanceAfter = to.Balance,
            Description = remarks,
            CounterpartyAccountNumber = from.AccountNumber,
            CounterpartyName = from.User!.FullName,
            CreatedAt = now,
            AccountId = to.Id,
        });

        try
        {
            await db.SaveChangesAsync(ct); // one SaveChanges = one database transaction: both legs or neither
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            throw new BankingException("Your balance changed while this transfer was processing. Nothing was sent; please try again.");
        }
        catch (DbUpdateException)
        {
            // The unique (account, request id) index fired: a parallel copy of this request already went through
            db.ChangeTracker.Clear();
            var duplicate = await FindByRequestIdAsync(cmd.FromAccountId, cmd.RequestId, ct);
            if (duplicate is null)
                throw;
            return duplicate;
        }

        return outLeg;
    }

    private Task<Transaction?> FindByRequestIdAsync(int accountId, Guid requestId, CancellationToken ct) =>
        db.Transactions.AsNoTracking().FirstOrDefaultAsync(t => t.AccountId == accountId && t.RequestId == requestId, ct);

    public async Task SetPinAsync(int userId, string currentPassword, string newPin, CancellationToken ct = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct) ?? throw new NotFoundException();
        if (hasher.VerifyHashedPassword(user, user.PasswordHash, currentPassword) == PasswordVerificationResult.Failed)
            throw new BankingException("Your password is incorrect.");
        if (newPin.Length != 4 || !newPin.All(char.IsAsciiDigit))
            throw new BankingException("The PIN must be exactly 4 digits.");
        if (newPin.Distinct().Count() == 1 || "0123456789".Contains(newPin) || "9876543210".Contains(newPin))
            throw new BankingException("Choose a less guessable PIN (not 1111 or 1234).");

        user.TransactionPinHash = hasher.HashPassword(user, newPin);
        user.PinFailedAttempts = 0;
        user.PinLockedUntil = null;
        await db.SaveChangesAsync(ct);
    }

    private async Task VerifyPinAsync(User user, string pin, CancellationToken ct)
    {
        if (user.TransactionPinHash is null)
            throw new BankingException("Set your transaction PIN under Security before sending money.");

        var now = UtcNow;
        if (user.PinLockedUntil is { } until && until > now)
            throw new BankingException($"Too many wrong PIN attempts. Try again after {Format.DateAndTime(until)}.");

        if (hasher.VerifyHashedPassword(user, user.TransactionPinHash, pin ?? "") != PasswordVerificationResult.Failed)
        {
            if (user.PinFailedAttempts > 0 || user.PinLockedUntil is not null)
            {
                user.PinFailedAttempts = 0;
                user.PinLockedUntil = null;
            }
            return;
        }

        user.PinFailedAttempts++;
        string message;
        if (user.PinFailedAttempts >= _opt.MaxPinAttempts)
        {
            user.PinFailedAttempts = 0;
            user.PinLockedUntil = now.AddMinutes(_opt.PinLockMinutes);
            message = $"Wrong PIN. Transfers are locked for {_opt.PinLockMinutes} minutes.";
        }
        else
        {
            var left = _opt.MaxPinAttempts - user.PinFailedAttempts;
            message = $"Wrong PIN. {left} attempt{(left == 1 ? "" : "s")} left.";
        }

        // Save only the attempt counter, never a half-built transfer
        foreach (var entry in db.ChangeTracker.Entries().Where(e => e.Entity is not User && e.State != EntityState.Unchanged).ToList())
            entry.State = EntityState.Unchanged;
        await db.SaveChangesAsync(ct);
        throw new BankingException(message);
    }

    // ---------------- Admin: cash, applications, status ----------------

    public Task<Transaction> DepositAsync(int accountId, decimal amount, string? description, CancellationToken ct = default) =>
        PostCashAsync(accountId, ValidateAmount(amount), TransactionType.Deposit, string.IsNullOrWhiteSpace(description) ? "Cash deposit" : description.Trim(), ct);

    public Task<Transaction> WithdrawAsync(int accountId, decimal amount, string? description, CancellationToken ct = default) =>
        PostCashAsync(accountId, ValidateAmount(amount), TransactionType.Withdrawal, string.IsNullOrWhiteSpace(description) ? "Cash withdrawal" : description.Trim(), ct);

    private async Task<Transaction> PostCashAsync(int accountId, decimal amount, TransactionType type, string description, CancellationToken ct)
    {
        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == accountId, ct) ?? throw new NotFoundException("Account not found.");
        if (account.Status != AccountStatus.Active)
            throw new BankingException("Unfreeze the account first.");

        if (type == TransactionType.Withdrawal)
        {
            if (amount > account.Balance)
                throw new BankingException($"Insufficient balance. Available: {Format.Money(account.Balance)}.");
            account.Balance -= amount;
        }
        else
        {
            account.Balance += amount;
        }

        var tx = new Transaction
        {
            Reference = NewReference(),
            Type = type,
            Amount = amount,
            BalanceAfter = account.Balance,
            Description = description,
            CreatedAt = UtcNow,
            AccountId = account.Id,
        };
        db.Transactions.Add(tx);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            throw new BankingException("The balance changed at the same time. Nothing was posted; please try again.");
        }
        return tx;
    }

    public async Task<Account> ApproveApplicationAsync(int applicationId, decimal openingDeposit, string? note, CancellationToken ct = default)
    {
        var app = await db.Applications.FirstOrDefaultAsync(a => a.Id == applicationId, ct) ?? throw new NotFoundException("Application not found.");
        if (app.Status != ApplicationStatus.Pending)
            throw new BankingException($"This application was already {app.Status.ToString().ToLowerInvariant()}.");
        if (openingDeposit < 0)
            throw new BankingException("The opening deposit can't be negative.");
        if (openingDeposit > 0)
            ValidateAmount(openingDeposit);

        var account = new Account
        {
            AccountNumber = await NewAccountNumberAsync(ct),
            Type = app.AccountType,
            UserId = app.UserId,
            OpenedAt = UtcNow,
            Balance = openingDeposit,
        };
        if (openingDeposit > 0)
        {
            account.Transactions.Add(new Transaction
            {
                Reference = NewReference(),
                Type = TransactionType.Deposit,
                Amount = openingDeposit,
                BalanceAfter = openingDeposit,
                Description = "Opening deposit",
                CreatedAt = UtcNow,
            });
        }

        app.Status = ApplicationStatus.Approved;
        app.ReviewedAt = UtcNow;
        app.ReviewNote = note?.Trim() ?? "";
        app.Account = account;
        db.Accounts.Add(account);
        await db.SaveChangesAsync(ct);
        return account;
    }

    public async Task RejectApplicationAsync(int applicationId, string note, CancellationToken ct = default)
    {
        var app = await db.Applications.FirstOrDefaultAsync(a => a.Id == applicationId, ct) ?? throw new NotFoundException("Application not found.");
        if (app.Status != ApplicationStatus.Pending)
            throw new BankingException($"This application was already {app.Status.ToString().ToLowerInvariant()}.");
        if (string.IsNullOrWhiteSpace(note))
            throw new BankingException("Give the customer a reason for the rejection.");

        app.Status = ApplicationStatus.Rejected;
        app.ReviewedAt = UtcNow;
        app.ReviewNote = note.Trim();
        await db.SaveChangesAsync(ct);
    }

    public async Task SetStatusAsync(int accountId, AccountStatus status, CancellationToken ct = default)
    {
        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == accountId, ct) ?? throw new NotFoundException("Account not found.");
        if (account.Status == AccountStatus.Closed)
            throw new BankingException("Closed accounts can't be changed.");
        if (status == AccountStatus.Closed && account.Balance != 0)
            throw new BankingException($"Pay out the remaining balance ({Format.Money(account.Balance)}) before closing the account.");
        account.Status = status;
        await db.SaveChangesAsync(ct);
    }

    // ---------------- Helpers ----------------

    public static decimal ValidateAmount(decimal amount)
    {
        if (amount <= 0)
            throw new BankingException("Enter an amount greater than zero.");
        if (decimal.Round(amount, 2) != amount)
            throw new BankingException("Amounts can have at most two decimal places.");
        if (amount > MaxSingleAmount)
            throw new BankingException($"The most you can move in one go is {Format.Money(MaxSingleAmount)}.");
        return amount;
    }

    public static string Normalize(string? accountNumber) =>
        new((accountNumber ?? "").Where(char.IsAsciiDigit).ToArray());

    public static string MaskName(string fullName)
    {
        var parts = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => "Account holder",
            1 => parts[0],
            _ => $"{parts[0]} {parts[^1][0]}.",
        };
    }

    private string NewReference() =>
        $"TXN{Format.ToIst(UtcNow):yyMMddHHmm}{RandomNumberGenerator.GetInt32(0, 1_000_000):D6}";

    private async Task<string> NewAccountNumberAsync(CancellationToken ct)
    {
        while (true)
        {
            var number = "5010" + RandomNumberGenerator.GetInt32(0, 100_000_000).ToString("D8");
            if (!await db.Accounts.AnyAsync(a => a.AccountNumber == number, ct) &&
                !db.Accounts.Local.Any(a => a.AccountNumber == number))
                return number;
        }
    }
}
