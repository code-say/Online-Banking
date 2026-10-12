using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using OnlineBanking.Web.Entities;

namespace OnlineBanking.Web.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<AccountApplication> Applications => Set<AccountApplication>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<ContactMessage> ContactMessages => Set<ContactMessage>();

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        // Money is stored as whole paise (INTEGER) so SQLite can compare and sort it exactly
        builder.Properties<decimal>().HaveConversion<PaiseConverter>();
        builder.Properties<string>().HaveMaxLength(500);
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            e.HasIndex(u => u.Email).IsUnique();
            e.Property(u => u.Email).HasMaxLength(256);
            e.Property(u => u.PasswordHash).HasMaxLength(1000);
            e.Property(u => u.TransactionPinHash).HasMaxLength(1000);
        });

        b.Entity<Account>(e =>
        {
            e.HasIndex(a => a.AccountNumber).IsUnique();
            e.Property(a => a.Type).HasConversion<string>();
            e.Property(a => a.Status).HasConversion<string>();
            e.Property(a => a.Version).IsConcurrencyToken();
            e.HasOne(a => a.User).WithMany(u => u.Accounts).HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<AccountApplication>(e =>
        {
            e.Property(a => a.AccountType).HasConversion<string>();
            e.Property(a => a.Status).HasConversion<string>();
            e.HasOne(a => a.User).WithMany(u => u.Applications).HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(a => a.Account).WithMany().HasForeignKey(a => a.AccountId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<Transaction>(e =>
        {
            e.Property(t => t.Type).HasConversion<string>();
            e.HasIndex(t => new { t.AccountId, t.CreatedAt });
            e.HasIndex(t => t.Reference);
            // A retried transfer with the same request id hits this index instead of moving money twice
            e.HasIndex(t => new { t.AccountId, t.RequestId }).IsUnique().HasFilter("RequestId IS NOT NULL");
            e.HasOne(t => t.Account).WithMany(a => a.Transactions).HasForeignKey(t => t.AccountId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ContactMessage>(e => e.Property(m => m.Message).HasMaxLength(4000));
    }

    /// <summary>Bump the concurrency version of every account whose balance or status changed.</summary>
    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries<Account>())
        {
            if (entry.State == EntityState.Modified)
                entry.Entity.Version++;
        }
        return base.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>Rupees (decimal) &lt;-&gt; paise (long).</summary>
public sealed class PaiseConverter() : ValueConverter<decimal, long>(
    rupees => (long)decimal.Round(rupees * 100m, 0, MidpointRounding.AwayFromZero),
    paise => paise / 100m);
