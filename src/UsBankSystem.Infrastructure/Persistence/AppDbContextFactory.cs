using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace UsBankSystem.Infrastructure.Persistence;

public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var host = Environment.GetEnvironmentVariable("POSTGRES_HOST") ?? "localhost";
        var port = Environment.GetEnvironmentVariable("POSTGRES_PORT") ?? "5433";
        var db = Environment.GetEnvironmentVariable("POSTGRES_DB") ?? "usbank";
        var user = Environment.GetEnvironmentVariable("POSTGRES_USER") ?? "app";
        var password = Environment.GetEnvironmentVariable("POSTGRES_PASSWORD") ?? "secret";

        var connectionString = $"Host={host};Port={port};Database={db};Username={user};Password={password}";

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseNpgsql(connectionString);
        return new AppDbContext(optionsBuilder.Options);
    }
}