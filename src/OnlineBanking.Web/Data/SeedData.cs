using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OnlineBanking.Web.Entities;
using OnlineBanking.Web.Infrastructure;

namespace OnlineBanking.Web.Data;

/// <summary>Demo customers, accounts and two months of history, so a fresh database is immediately usable.</summary>
public static class SeedData
{
    public const string AdminEmail = "admin@onlinebank.demo";
    public const string AdminPassword = "Admin@12345";
    public const string CustomerEmail = "customer@onlinebank.demo";
    public const string CustomerPassword = "Customer@12345";
    public const string CustomerPin = "2580";
    public const string PayeeEmail = "rahul@onlinebank.demo";
    public const string ApplicantEmail = "neha@onlinebank.demo";

    public const string CustomerSavings = "501000123456";
    public const string CustomerCurrent = "501000123457";
    public const string PayeeSavings = "501000987654";

    public static async Task InitializeAsync(AppDbContext db, IPasswordHasher<User> hasher, TimeProvider clock, CancellationToken ct = default)
    {
        await db.Database.EnsureCreatedAsync(ct);
        if (await db.Users.AnyAsync(ct))
            return;

        var now = clock.GetUtcNow().UtcDateTime;

        User NewUser(string email, string password, string role, string name, string phone)
        {
            var u = new User { Email = email, Role = role, FullName = name, Phone = phone, CreatedAt = now.AddDays(-90) };
            u.PasswordHash = hasher.HashPassword(u, password);
            return u;
        }

        var admin = NewUser(AdminEmail, AdminPassword, Roles.Admin, "Branch Admin", "+91 20 6600 1000");
        var aarav = NewUser(CustomerEmail, CustomerPassword, Roles.Customer, "Aarav Mehta", "+91 98220 11223");
        aarav.TransactionPinHash = hasher.HashPassword(aarav, CustomerPin);
        var rahul = NewUser(PayeeEmail, CustomerPassword, Roles.Customer, "Rahul Verma", "+91 99220 67890");
        rahul.TransactionPinHash = hasher.HashPassword(rahul, CustomerPin);
        var neha = NewUser(ApplicantEmail, CustomerPassword, Roles.Customer, "Neha Kapoor", "+91 90040 55667");

        db.Users.AddRange(admin, aarav, rahul, neha);

        var savings = new Account { AccountNumber = CustomerSavings, Type = AccountType.Savings, User = aarav, OpenedAt = now.AddDays(-86) };
        var current = new Account { AccountNumber = CustomerCurrent, Type = AccountType.Current, User = aarav, OpenedAt = now.AddDays(-81) };
        var rahulSavings = new Account { AccountNumber = PayeeSavings, Type = AccountType.Savings, User = rahul, OpenedAt = now.AddDays(-86) };
        db.Accounts.AddRange(savings, current, rahulSavings);

        // Approved applications behind the existing accounts, and one waiting for review
        db.Applications.AddRange(
            Application(aarav, AccountType.Savings, "Mr", "Aarav", "Mehta", "Rajesh Mehta", new DateOnly(1994, 3, 14), "4821",
                "B-402, Silver Oak Residency", "Baner Road", "Pune", "Maharashtra", "411045", "Software Engineer", "Salary", 1_450_000m,
                now.AddDays(-82), ApplicationStatus.Approved, savings),
            Application(aarav, AccountType.Current, "Mr", "Aarav", "Mehta", "Rajesh Mehta", new DateOnly(1994, 3, 14), "4821",
                "B-402, Silver Oak Residency", "Baner Road", "Pune", "Maharashtra", "411045", "Freelance consultant", "Business", 600_000m,
                now.AddDays(-82), ApplicationStatus.Approved, current),
            Application(rahul, AccountType.Savings, "Mr", "Rahul", "Verma", "Suresh Verma", new DateOnly(1985, 11, 2), "7310",
                "21 Lake Town", "Block A", "Kolkata", "West Bengal", "700089", "Teacher", "Salary", 720_000m,
                now.AddDays(-86), ApplicationStatus.Approved, rahulSavings),
            Application(neha, AccountType.Savings, "Ms", "Neha", "Kapoor", "Anil Kapoor", new DateOnly(1998, 7, 21), "1956",
                "9 Residency Road", "Ashok Nagar", "Bengaluru", "Karnataka", "560025", "Product Designer", "Salary", 1_100_000m,
                now.AddHours(-20), ApplicationStatus.Pending, null));

        // Two months of activity; balances are computed line by line so every BalanceAfter is consistent
        var ledger = new Ledger(now);
        ledger.Deposit(savings, 85, 50_000m, "Opening deposit");
        ledger.Deposit(current, 80, 25_000m, "Opening deposit");
        ledger.Deposit(rahulSavings, 85, 30_000m, "Opening deposit");

        for (var month = 2; month >= 1; month--)
        {
            var d = month * 30;
            ledger.Deposit(savings, d, 92_500m, "Salary credit - Infotech Solutions");
            ledger.Transfer(savings, rahulSavings, d - 2, 18_000m, "Rent share");
            ledger.Withdraw(savings, d - 4, 5_000m, "ATM withdrawal - Baner");
            ledger.Transfer(savings, current, d - 6, 15_000m, "Move to current account");
            ledger.Deposit(current, d - 9, 42_000m, "Client payment - Design project");
            ledger.Withdraw(current, d - 11, 12_499.50m, "GST payment");
            ledger.Transfer(rahulSavings, savings, d - 14, 2_350m, "Dinner split");
            ledger.Deposit(rahulSavings, d - 1, 61_000m, "Salary credit - St. Xavier's School");
            ledger.Withdraw(savings, d - 17, 3_249.25m, "Electricity bill - MSEDCL");
            ledger.Withdraw(rahulSavings, d - 20, 8_000m, "ATM withdrawal - Salt Lake");
        }
        ledger.Transfer(savings, rahulSavings, 1, 1_200m, "Movie tickets");
        db.Transactions.AddRange(ledger.Entries);

        db.ContactMessages.AddRange(
            new ContactMessage { Name = "Priya Nair", Email = "priya@example.com", Subject = "Debit card delivery", Message = "When will my new debit card be delivered? I applied last week.", CreatedAt = now.AddDays(-2) },
            new ContactMessage { Name = "Imran Shaikh", Email = "imran@example.com", Subject = "Branch timings", Message = "Is the Baner branch open on the second Saturday?", CreatedAt = now.AddHours(-5), IsRead = true });

        await db.SaveChangesAsync(ct);
    }

    private static AccountApplication Application(User user, AccountType type, string title, string first, string last, string father,
        DateOnly dob, string aadhaarLast4, string line1, string line2, string city, string state, string pincode,
        string occupation, string income, decimal annualIncome, DateTime submitted, ApplicationStatus status, Account? account) => new()
    {
        User = user,
        AccountType = type,
        Title = title,
        FirstName = first,
        LastName = last,
        FatherName = father,
        DateOfBirth = dob,
        Mobile = user.Phone,
        AadhaarLast4 = aadhaarLast4,
        AddressLine1 = line1,
        AddressLine2 = line2,
        City = city,
        State = state,
        Pincode = pincode,
        Occupation = occupation,
        SourceOfIncome = income,
        AnnualIncome = annualIncome,
        SubmittedAt = submitted,
        Status = status,
        ReviewedAt = status == ApplicationStatus.Pending ? null : submitted.AddDays(1),
        ReviewNote = status == ApplicationStatus.Approved ? "KYC verified at branch." : "",
        Account = account,
    };

    /// <summary>
    /// Builds consistent ledger lines for seeding (bypasses PIN and limits, which only apply to customers).
    /// Operations are collected first, then applied in date order so every running balance is right.
    /// </summary>
    private sealed class Ledger(DateTime now)
    {
        private int _seq;
        private readonly List<(DateTime At, int Seq, Action<string> Apply)> _ops = [];
        private readonly List<Transaction> _entries = [];

        public List<Transaction> Entries
        {
            get
            {
                if (_entries.Count == 0)
                    foreach (var (at, seq, apply) in _ops.OrderBy(o => o.At).ThenBy(o => o.Seq))
                        apply($"TXN{Format.ToIst(at):yyMMddHHmm}{seq:D6}");
                return _entries;
            }
        }

        private void Add(int daysAgo, Action<DateTime, string> apply)
        {
            var seq = ++_seq;
            var at = now.Date.AddDays(-daysAgo).AddHours(4 + seq % 9).AddMinutes(seq * 7 % 60);
            _ops.Add((at, seq, reference => apply(at, reference)));
        }

        public void Deposit(Account a, int daysAgo, decimal amount, string description) => Post(a, daysAgo, amount, TransactionType.Deposit, description);

        public void Withdraw(Account a, int daysAgo, decimal amount, string description) => Post(a, daysAgo, amount, TransactionType.Withdrawal, description);

        private void Post(Account a, int daysAgo, decimal amount, TransactionType type, string description) => Add(daysAgo, (at, reference) =>
        {
            a.Balance += type == TransactionType.Deposit ? amount : -amount;
            _entries.Add(new Transaction { Account = a, Reference = reference, Type = type, Amount = amount, BalanceAfter = a.Balance, Description = description, CreatedAt = at });
        });

        public void Transfer(Account from, Account to, int daysAgo, decimal amount, string description) => Add(daysAgo, (at, reference) =>
        {
            from.Balance -= amount;
            to.Balance += amount;
            _entries.Add(new Transaction
            {
                Account = from, Reference = reference, Type = TransactionType.TransferOut, Amount = amount, BalanceAfter = from.Balance,
                Description = description, CounterpartyAccountNumber = to.AccountNumber, CounterpartyName = to.User!.FullName, CreatedAt = at,
            });
            _entries.Add(new Transaction
            {
                Account = to, Reference = reference, Type = TransactionType.TransferIn, Amount = amount, BalanceAfter = to.Balance,
                Description = description, CounterpartyAccountNumber = from.AccountNumber, CounterpartyName = from.User!.FullName, CreatedAt = at,
            });
        });
    }
}
