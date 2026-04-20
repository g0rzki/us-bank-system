using Microsoft.EntityFrameworkCore;
using UsBankSystem.Core.Entities;

namespace UsBankSystem.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<Transfer> Transfers => Set<Transfer>();
    public DbSet<Card> Cards => Set<Card>();
    public DbSet<BlikCode> BlikCodes => Set<BlikCode>();
    
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>(e =>
        {
            e.HasKey(u => u.Id);
            e.HasIndex(u => u.Email).IsUnique();
            e.Property(u => u.Email).IsRequired().HasMaxLength(256);
            e.Property(u => u.PasswordHash).IsRequired();
            e.Property(u => u.FirstName).IsRequired().HasMaxLength(100);
            e.Property(u => u.LastName).IsRequired().HasMaxLength(100);
        });

        modelBuilder.Entity<Account>(e =>
        {
            e.HasKey(a => a.Id);
            e.HasIndex(a => a.AccountNumber).IsUnique();
            e.Property(a => a.AccountNumber).IsRequired().HasMaxLength(20);
            e.Property(a => a.Currency).HasMaxLength(3).HasDefaultValue("USD");
            e.Property(a => a.Balance).HasPrecision(18, 2);
            e.Property(a => a.ReservedBalance).HasPrecision(18, 2);
            e.HasOne(a => a.User)
             .WithMany(u => u.Accounts)
             .HasForeignKey(a => a.UserId);
        });

        modelBuilder.Entity<Transaction>(e =>
        {
            e.HasKey(t => t.Id);
            e.Property(t => t.Amount).HasPrecision(18, 2);
            e.Property(t => t.Type).IsRequired().HasMaxLength(10);
            e.HasOne(t => t.Account)
             .WithMany(a => a.Transactions)
             .HasForeignKey(t => t.AccountId);
            e.HasIndex(t => new { t.AccountId, t.CreatedAt });
        });

        modelBuilder.Entity<Transfer>(e =>
        {
            e.HasKey(t => t.Id);
            e.Property(t => t.Amount).HasPrecision(18, 2);
            e.Property(t => t.Channel).IsRequired().HasMaxLength(20);
            e.Property(t => t.Currency).HasMaxLength(3).HasDefaultValue("USD");
            e.HasOne(t => t.FromAccount)
             .WithMany(a => a.Transfers)
             .HasForeignKey(t => t.FromAccountId)
             .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(t => t.ToAccount)
             .WithMany()
             .HasForeignKey(t => t.ToAccountId)
             .OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(t => t.FromAccountId);
            e.HasIndex(t => t.Status);
        });
        
        modelBuilder.Entity<Card>(e =>
        {
            e.HasKey(c => c.Id);
            e.Property(c => c.Last4).IsRequired().HasMaxLength(4);
            e.Property(c => c.Type).IsRequired().HasMaxLength(10);
            e.Property(c => c.DailyLimit).HasPrecision(18, 2);
            e.Property(c => c.MonthlyLimit).HasPrecision(18, 2);
            e.HasOne(c => c.Account)
                .WithMany(a => a.Cards)
                .HasForeignKey(c => c.AccountId);
            e.HasIndex(c => c.AccountId);
        });

        modelBuilder.Entity<BlikCode>(e =>
        {
            e.HasKey(b => b.Id);
            e.Property(b => b.CodeHash).IsRequired();
            e.HasOne(b => b.Account)
                .WithMany(a => a.BlikCodes)
                .HasForeignKey(b => b.AccountId);
            e.HasIndex(b => new { b.AccountId, b.ExpiresAt });
        });
    }
}