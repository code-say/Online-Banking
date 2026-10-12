namespace OnlineBanking.Web.Entities;

public static class Roles
{
    public const string Admin = "Admin";
    public const string Customer = "Customer";
}

public enum AccountType
{
    Savings,
    Current,
}

public enum AccountStatus
{
    Active,
    Frozen,
    Closed,
}

public enum ApplicationStatus
{
    Pending,
    Approved,
    Rejected,
}

public enum TransactionType
{
    Deposit,
    Withdrawal,
    TransferIn,
    TransferOut,
}

public class User
{
    public int Id { get; set; }
    public required string Email { get; set; }
    public string PasswordHash { get; set; } = string.Empty;
    public required string Role { get; set; }
    public required string FullName { get; set; }
    public string Phone { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Hashed 4-digit PIN that authorises money leaving the customer's accounts.</summary>
    public string? TransactionPinHash { get; set; }
    public int PinFailedAttempts { get; set; }
    public DateTime? PinLockedUntil { get; set; }

    public List<Account> Accounts { get; set; } = [];
    public List<AccountApplication> Applications { get; set; } = [];
}

/// <summary>A request to open an account (KYC details), reviewed by an admin.</summary>
public class AccountApplication
{
    public int Id { get; set; }
    public AccountType AccountType { get; set; } = AccountType.Savings;
    public ApplicationStatus Status { get; set; } = ApplicationStatus.Pending;

    public string Title { get; set; } = string.Empty;
    public required string FirstName { get; set; }
    public string MiddleName { get; set; } = string.Empty;
    public required string LastName { get; set; }
    public string FatherName { get; set; } = string.Empty;
    public DateOnly DateOfBirth { get; set; }
    public string Mobile { get; set; } = string.Empty;

    /// <summary>Only the last four digits of the Aadhaar number are kept.</summary>
    public string AadhaarLast4 { get; set; } = string.Empty;

    public string AddressLine1 { get; set; } = string.Empty;
    public string AddressLine2 { get; set; } = string.Empty;
    public string Landmark { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Pincode { get; set; } = string.Empty;

    public string Occupation { get; set; } = string.Empty;
    public string SourceOfIncome { get; set; } = string.Empty;
    public decimal AnnualIncome { get; set; }

    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReviewedAt { get; set; }
    public string ReviewNote { get; set; } = string.Empty;

    public int UserId { get; set; }
    public User? User { get; set; }

    public int? AccountId { get; set; }
    public Account? Account { get; set; }
}

public class Account
{
    public int Id { get; set; }
    public required string AccountNumber { get; set; }
    public AccountType Type { get; set; }
    public AccountStatus Status { get; set; } = AccountStatus.Active;
    public decimal Balance { get; set; }
    public DateTime OpenedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Optimistic concurrency token: bumped on every balance change, so two writers can't both spend the same money.</summary>
    public int Version { get; set; }

    public int UserId { get; set; }
    public User? User { get; set; }

    public List<Transaction> Transactions { get; set; } = [];
}

/// <summary>One ledger line on one account. A transfer writes two lines (out and in) sharing a reference.</summary>
public class Transaction
{
    public int Id { get; set; }
    public required string Reference { get; set; }
    public TransactionType Type { get; set; }
    public decimal Amount { get; set; }
    public decimal BalanceAfter { get; set; }
    public string Description { get; set; } = string.Empty;
    public string CounterpartyAccountNumber { get; set; } = string.Empty;
    public string CounterpartyName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Client-generated id for a transfer, so a retried request can't move money twice.</summary>
    public Guid? RequestId { get; set; }

    public int AccountId { get; set; }
    public Account? Account { get; set; }
}

/// <summary>Message from the public contact form, read by admins (replaces the old SMTP mailer).</summary>
public class ContactMessage
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string Email { get; set; }
    public required string Subject { get; set; }
    public required string Message { get; set; }
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
