using Microsoft.EntityFrameworkCore;
using UsBankSystem.Core.Entities;

namespace UsBankSystem.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<Transfer> Transfers => Set<Transfer>();

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
        });
    }
}