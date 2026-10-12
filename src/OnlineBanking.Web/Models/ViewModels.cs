using System.ComponentModel.DataAnnotations;
using OnlineBanking.Web.Entities;

namespace OnlineBanking.Web.Models;

// ---------------- Auth ----------------

public sealed class LoginViewModel
{
    [Required, EmailAddress, Display(Name = "Email")]
    public string Email { get; set; } = "";

    [Required, DataType(DataType.Password)]
    public string Password { get; set; } = "";

    public string? ReturnUrl { get; set; }
}

public sealed class RegisterViewModel
{
    [Required, StringLength(100, MinimumLength = 2), Display(Name = "Full name")]
    public string FullName { get; set; } = "";

    [Required, EmailAddress, StringLength(256)]
    public string Email { get; set; } = "";

    [Required, Phone, StringLength(20), Display(Name = "Mobile number")]
    public string Phone { get; set; } = "";

    [Required, StringLength(100, MinimumLength = 8, ErrorMessage = "Use at least 8 characters.")]
    [RegularExpression(@"^(?=.*[A-Za-z])(?=.*\d).+$", ErrorMessage = "Use letters and at least one number.")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = "";

    [Required, DataType(DataType.Password), Compare(nameof(Password), ErrorMessage = "The passwords don't match."), Display(Name = "Confirm password")]
    public string ConfirmPassword { get; set; } = "";
}

public sealed class ChangePasswordViewModel
{
    [Required, DataType(DataType.Password), Display(Name = "Current password")]
    public string CurrentPassword { get; set; } = "";

    [Required, StringLength(100, MinimumLength = 8, ErrorMessage = "Use at least 8 characters.")]
    [RegularExpression(@"^(?=.*[A-Za-z])(?=.*\d).+$", ErrorMessage = "Use letters and at least one number.")]
    [DataType(DataType.Password), Display(Name = "New password")]
    public string NewPassword { get; set; } = "";

    [Required, DataType(DataType.Password), Compare(nameof(NewPassword), ErrorMessage = "The passwords don't match."), Display(Name = "Confirm new password")]
    public string ConfirmPassword { get; set; } = "";
}

public sealed class SetPinViewModel
{
    [Required, DataType(DataType.Password), Display(Name = "Account password")]
    public string CurrentPassword { get; set; } = "";

    [Required, RegularExpression(@"^\d{4}$", ErrorMessage = "Enter exactly 4 digits."), DataType(DataType.Password), Display(Name = "New 4-digit PIN")]
    public string Pin { get; set; } = "";

    [Required, Compare(nameof(Pin), ErrorMessage = "The PINs don't match."), DataType(DataType.Password), Display(Name = "Confirm PIN")]
    public string ConfirmPin { get; set; } = "";
}

public sealed class ProfileViewModel
{
    public required User User { get; init; }
    public ChangePasswordViewModel Password { get; init; } = new();
    public SetPinViewModel Pin { get; init; } = new();
}

// ---------------- Public ----------------

public sealed class ContactViewModel
{
    [Required, StringLength(100)]
    public string Name { get; set; } = "";

    [Required, EmailAddress, StringLength(256)]
    public string Email { get; set; } = "";

    [Required, StringLength(150)]
    public string Subject { get; set; } = "";

    [Required, StringLength(4000, MinimumLength = 10, ErrorMessage = "Write at least 10 characters.")]
    public string Message { get; set; } = "";
}

// ---------------- Customer ----------------

public sealed class DashboardViewModel
{
    public required string Name { get; init; }
    public required IReadOnlyList<Account> Accounts { get; init; }
    public required IReadOnlyList<Transaction> Recent { get; init; }
    public required IReadOnlyList<AccountApplication> OpenApplications { get; init; }
    public bool HasPin { get; init; }
    public decimal TotalBalance => Accounts.Where(a => a.Status != AccountStatus.Closed).Sum(a => a.Balance);
}

public sealed class StatementFilter
{
    [DataType(DataType.Date)]
    public DateOnly? From { get; set; }

    [DataType(DataType.Date)]
    public DateOnly? To { get; set; }

    public string? Type { get; set; } // "credit" | "debit" | null

    public int Page { get; set; } = 1;
}

public sealed class StatementViewModel
{
    public required Account Account { get; init; }
    public required StatementFilter Filter { get; init; }
    public required IReadOnlyList<Transaction> Transactions { get; init; }
    public decimal TotalIn { get; init; }
    public decimal TotalOut { get; init; }
    public int Page { get; init; }
    public int PageCount { get; init; }
    public int TotalCount { get; init; }
    public bool IsAdminView { get; init; }
}

public sealed class TransferViewModel
{
    [Required(ErrorMessage = "Choose the account to pay from."), Display(Name = "From account")]
    public int? FromAccountId { get; set; }

    [Required(ErrorMessage = "Enter the beneficiary's account number.")]
    [RegularExpression(@"^[\d\s]{12,16}$", ErrorMessage = "Enter a 12-digit account number.")]
    [Display(Name = "To account number")]
    public string ToAccountNumber { get; set; } = "";

    [Required(ErrorMessage = "Enter an amount."), Range(typeof(decimal), "1", "10000000", ErrorMessage = "Enter an amount between ₹1 and ₹1,00,00,000.")]
    public decimal? Amount { get; set; }

    [StringLength(60), Display(Name = "Remarks (optional)")]
    public string? Remarks { get; set; }

    [Required(ErrorMessage = "Enter your 4-digit transaction PIN."), RegularExpression(@"^\d{4}$", ErrorMessage = "Enter your 4-digit transaction PIN.")]
    [DataType(DataType.Password), Display(Name = "Transaction PIN")]
    public string Pin { get; set; } = "";

    /// <summary>Generated when the form is shown; makes resubmitting the same form harmless.</summary>
    public Guid RequestId { get; set; }

    public IReadOnlyList<Account> Accounts { get; set; } = [];
    public IReadOnlyDictionary<int, decimal> RemainingToday { get; set; } = new Dictionary<int, decimal>();
    public decimal DailyLimit { get; set; }
    public bool HasPin { get; set; }
}

public sealed class ReceiptViewModel
{
    public required Transaction Transaction { get; init; }
    public required Account From { get; init; }
}

public sealed class ApplicationViewModel
{
    [Display(Name = "Account type")]
    public AccountType AccountType { get; set; } = AccountType.Savings;

    [Required, StringLength(10), Display(Name = "Title")]
    public string Salutation { get; set; } = "Mr";

    [Required, StringLength(50), Display(Name = "First name")]
    public string FirstName { get; set; } = "";

    [StringLength(50), Display(Name = "Middle name")]
    public string? MiddleName { get; set; }

    [Required, StringLength(50), Display(Name = "Last name")]
    public string LastName { get; set; } = "";

    [Required, StringLength(100), Display(Name = "Father's name")]
    public string FatherName { get; set; } = "";

    [Required, DataType(DataType.Date), Display(Name = "Date of birth")]
    public DateOnly? DateOfBirth { get; set; }

    [Required, Phone, StringLength(20)]
    public string Mobile { get; set; } = "";

    [Required, RegularExpression(@"^\d{4}\s?\d{4}\s?\d{4}$", ErrorMessage = "Enter the 12-digit Aadhaar number.")]
    [Display(Name = "Aadhaar number")]
    public string Aadhaar { get; set; } = "";

    [Required, StringLength(120), Display(Name = "Address line 1")]
    public string AddressLine1 { get; set; } = "";

    [StringLength(120), Display(Name = "Address line 2")]
    public string? AddressLine2 { get; set; }

    [StringLength(120)]
    public string? Landmark { get; set; }

    [Required, StringLength(60)]
    public string City { get; set; } = "";

    [Required, StringLength(60)]
    public string State { get; set; } = "";

    [Required, RegularExpression(@"^[1-9]\d{5}$", ErrorMessage = "Enter a 6-digit PIN code.")]
    [Display(Name = "PIN code")]
    public string Pincode { get; set; } = "";

    [Required, StringLength(80)]
    public string Occupation { get; set; } = "";

    [Required, StringLength(80), Display(Name = "Source of income")]
    public string SourceOfIncome { get; set; } = "Salary";

    [Required, Range(typeof(decimal), "0", "1000000000"), Display(Name = "Annual income (₹)")]
    public decimal? AnnualIncome { get; set; }

    [Range(typeof(bool), "true", "true", ErrorMessage = "Please confirm the details are correct.")]
    [Display(Name = "I confirm these details are correct")]
    public bool Confirm { get; set; }
}

// ---------------- Admin ----------------

public sealed class AdminDashboardViewModel
{
    public int Customers { get; init; }
    public int ActiveAccounts { get; init; }
    public int FrozenAccounts { get; init; }
    public decimal TotalDeposits { get; init; }
    public int PendingApplications { get; init; }
    public int UnreadMessages { get; init; }
    public int TransactionsToday { get; init; }
    public decimal VolumeToday { get; init; }
    public required IReadOnlyList<Transaction> Recent { get; init; }
}

public sealed class CashViewModel
{
    [Required(ErrorMessage = "Enter an amount."), Range(typeof(decimal), "1", "10000000", ErrorMessage = "Enter an amount between ₹1 and ₹1,00,00,000.")]
    public decimal? Amount { get; set; }

    [StringLength(60)]
    public string? Description { get; set; }
}

public sealed class AdminAccountViewModel
{
    public required Account Account { get; init; }
    public required IReadOnlyList<Transaction> Recent { get; init; }
    public CashViewModel Cash { get; init; } = new();
}

public sealed class CustomerRow
{
    public required User User { get; init; }
    public int Accounts { get; init; }
    public decimal Balance { get; init; }
    public bool HasPendingApplication { get; init; }
}
