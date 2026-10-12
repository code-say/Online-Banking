using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OnlineBanking.Web.Data;
using OnlineBanking.Web.Entities;
using OnlineBanking.Web.Infrastructure;
using OnlineBanking.Web.Services;

namespace OnlineBanking.Tests;

/// <summary>The money rules, run against the real database and services.</summary>
public class BankingRulesTests
{
    private static TransferCommand Send(Account from, string to, decimal amount, string pin = SeedData.CustomerPin, Guid? requestId = null) =>
        new(from.Id, to, amount, "Test", pin, requestId ?? Guid.NewGuid());

    [Fact]
    public async Task Seeded_ledgers_are_consistent()
    {
        using var app = new AppFactory();
        var problems = await app.WithScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<AppDbContext>();
            var list = new List<string>();
            foreach (var account in await db.Accounts.Include(a => a.Transactions).ToListAsync())
            {
                var running = 0m;
                foreach (var t in account.Transactions.OrderBy(t => t.CreatedAt).ThenBy(t => t.Id))
                {
                    running += Format.IsCredit(t.Type) ? t.Amount : -t.Amount;
                    if (running != t.BalanceAfter) list.Add($"{account.AccountNumber} {t.Reference}: {running} != {t.BalanceAfter}");
                    if (running < 0) list.Add($"{account.AccountNumber} went negative at {t.Reference}");
                }
                if (running != account.Balance) list.Add($"{account.AccountNumber}: ledger {running} != balance {account.Balance}");
            }
            return list;
        });
        Assert.Empty(problems);
    }

    [Fact]
    public async Task Transfer_moves_money_between_accounts_and_writes_both_legs()
    {
        using var app = new AppFactory();
        var from = await app.AccountAsync(SeedData.CustomerSavings);
        var to = await app.AccountAsync(SeedData.PayeeSavings);
        var userId = await app.UserIdAsync(SeedData.CustomerEmail);

        var tx = await app.BankAsync(b => b.TransferAsync(userId, Send(from, "5010 0098 7654", 1_234.56m)));

        var fromAfter = await app.AccountAsync(SeedData.CustomerSavings);
        var toAfter = await app.AccountAsync(SeedData.PayeeSavings);
        Assert.Equal(from.Balance - 1_234.56m, fromAfter.Balance);
        Assert.Equal(to.Balance + 1_234.56m, toAfter.Balance);
        Assert.Equal(fromAfter.Balance, tx.BalanceAfter);
        Assert.Equal("Rahul Verma", tx.CounterpartyName);

        var legs = await app.WithScopeAsync(sp => sp.GetRequiredService<AppDbContext>().Transactions.Where(t => t.Reference == tx.Reference).ToListAsync());
        Assert.Equal(2, legs.Count);
        Assert.Contains(legs, l => l.Type == TransactionType.TransferIn && l.AccountId == to.Id && l.BalanceAfter == toAfter.Balance);
    }

    [Fact]
    public async Task Transfer_more_than_the_balance_is_refused_and_nothing_changes()
    {
        using var app = new AppFactory();
        var from = await app.AccountAsync(SeedData.CustomerCurrent);
        var userId = await app.UserIdAsync(SeedData.CustomerEmail);

        var ex = await Assert.ThrowsAsync<BankingException>(() =>
            app.BankAsync(b => b.TransferAsync(userId, Send(from, SeedData.PayeeSavings, from.Balance + 0.01m))));
        Assert.Contains("Insufficient balance", ex.Message);
        Assert.Equal(from.Balance, (await app.AccountAsync(SeedData.CustomerCurrent)).Balance);
    }

    [Fact]
    public async Task Same_request_id_never_sends_twice()
    {
        using var app = new AppFactory();
        var from = await app.AccountAsync(SeedData.CustomerSavings);
        var userId = await app.UserIdAsync(SeedData.CustomerEmail);
        var cmd = Send(from, SeedData.PayeeSavings, 500m, requestId: Guid.NewGuid());

        var first = await app.BankAsync(b => b.TransferAsync(userId, cmd));
        var second = await app.BankAsync(b => b.TransferAsync(userId, cmd));

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(from.Balance - 500m, (await app.AccountAsync(SeedData.CustomerSavings)).Balance);
    }

    [Fact]
    public async Task Three_wrong_pins_lock_transfers_even_with_the_right_pin()
    {
        using var app = new AppFactory();
        var from = await app.AccountAsync(SeedData.CustomerSavings);
        var userId = await app.UserIdAsync(SeedData.CustomerEmail);

        var e1 = await Assert.ThrowsAsync<BankingException>(() => app.BankAsync(b => b.TransferAsync(userId, Send(from, SeedData.PayeeSavings, 10m, "0000"))));
        Assert.Contains("2 attempts left", e1.Message);
        await Assert.ThrowsAsync<BankingException>(() => app.BankAsync(b => b.TransferAsync(userId, Send(from, SeedData.PayeeSavings, 10m, "0000"))));
        var e3 = await Assert.ThrowsAsync<BankingException>(() => app.BankAsync(b => b.TransferAsync(userId, Send(from, SeedData.PayeeSavings, 10m, "0000"))));
        Assert.Contains("locked", e3.Message);

        var locked = await Assert.ThrowsAsync<BankingException>(() => app.BankAsync(b => b.TransferAsync(userId, Send(from, SeedData.PayeeSavings, 10m))));
        Assert.Contains("Too many wrong PIN attempts", locked.Message);
        Assert.Equal(from.Balance, (await app.AccountAsync(SeedData.CustomerSavings)).Balance);
    }

    [Fact]
    public async Task Daily_limit_is_enforced()
    {
        using var app = new AppFactory();
        var savings = await app.AccountAsync(SeedData.CustomerSavings);
        var userId = await app.UserIdAsync(SeedData.CustomerEmail);

        // Top up so the balance isn't what stops us
        await app.BankAsync(b => b.DepositAsync(savings.Id, 500_000m, "Test top-up"));
        await app.BankAsync(b => b.TransferAsync(userId, Send(savings, SeedData.PayeeSavings, 150_000m)));

        var ex = await Assert.ThrowsAsync<BankingException>(() => app.BankAsync(b => b.TransferAsync(userId, Send(savings, SeedData.PayeeSavings, 60_000m))));
        Assert.Contains("daily transfer limit", ex.Message);
        await app.BankAsync(b => b.TransferAsync(userId, Send(savings, SeedData.PayeeSavings, 50_000m))); // exactly up to the limit is fine
    }

    [Fact]
    public async Task Frozen_accounts_cant_send_or_receive()
    {
        using var app = new AppFactory();
        var savings = await app.AccountAsync(SeedData.CustomerSavings);
        var payee = await app.AccountAsync(SeedData.PayeeSavings);
        var userId = await app.UserIdAsync(SeedData.CustomerEmail);

        await app.BankAsync(async b => { await b.SetStatusAsync(payee.Id, AccountStatus.Frozen); return 0; });
        var ex = await Assert.ThrowsAsync<BankingException>(() => app.BankAsync(b => b.TransferAsync(userId, Send(savings, SeedData.PayeeSavings, 10m))));
        Assert.Contains("can't accept", ex.Message);

        await app.BankAsync(async b => { await b.SetStatusAsync(savings.Id, AccountStatus.Frozen); return 0; });
        ex = await Assert.ThrowsAsync<BankingException>(() => app.BankAsync(b => b.TransferAsync(userId, Send(savings, SeedData.CustomerCurrent, 10m))));
        Assert.Contains("frozen", ex.Message);
    }

    [Fact]
    public async Task Customers_cant_send_from_someone_elses_account()
    {
        using var app = new AppFactory();
        var rahuls = await app.AccountAsync(SeedData.PayeeSavings);
        var aaravId = await app.UserIdAsync(SeedData.CustomerEmail);

        await Assert.ThrowsAsync<NotFoundException>(() => app.BankAsync(b => b.TransferAsync(aaravId, Send(rahuls, SeedData.CustomerSavings, 10m))));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("10.001")]
    [InlineData("10000000.01")]
    public void Invalid_amounts_are_rejected(string amount) =>
        Assert.Throws<BankingException>(() => BankingService.ValidateAmount(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture)));

    [Fact]
    public async Task Approving_an_application_opens_an_account_once()
    {
        using var app = new AppFactory();
        var appId = await app.WithScopeAsync(sp => sp.GetRequiredService<AppDbContext>().Applications
            .Where(a => a.Status == ApplicationStatus.Pending).Select(a => a.Id).FirstAsync());

        var account = await app.BankAsync(b => b.ApproveApplicationAsync(appId, 2_500m, "KYC ok"));
        Assert.Equal(12, account.AccountNumber.Length);
        Assert.StartsWith("5010", account.AccountNumber);
        Assert.Equal(2_500m, account.Balance);

        var ex = await Assert.ThrowsAsync<BankingException>(() => app.BankAsync(b => b.ApproveApplicationAsync(appId, 0, null)));
        Assert.Contains("already approved", ex.Message);
    }

    [Fact]
    public async Task Pin_must_not_be_guessable()
    {
        using var app = new AppFactory();
        var userId = await app.UserIdAsync(SeedData.CustomerEmail);
        var ex = await Assert.ThrowsAsync<BankingException>(() => app.BankAsync(async b => { await b.SetPinAsync(userId, SeedData.CustomerPassword, "1234"); return 0; }));
        Assert.Contains("less guessable", ex.Message);
        await app.BankAsync(async b => { await b.SetPinAsync(userId, SeedData.CustomerPassword, "7391"); return 0; });
    }

    [Fact]
    public void Money_is_formatted_the_indian_way() =>
        Assert.Equal("₹1,84,250.75", Format.Money(184_250.75m));
}
