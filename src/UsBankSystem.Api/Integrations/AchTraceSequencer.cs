using Microsoft.EntityFrameworkCore;
using UsBankSystem.Infrastructure.Persistence;

namespace UsBankSystem.Api.Integrations;

/// <summary>
/// DB-backed per-day sequence counter for NACHA trace numbers and file_id_modifier.
/// Uses a PostgreSQL atomic upsert so multiple API instances never issue duplicate
/// sequence numbers on the same business day.
/// </summary>
public sealed class AchTraceSequencer(IServiceScopeFactory scopeFactory) : IAchTraceSequencer
{
    public async Task<int> NextAsync(CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // SqlQueryRaw<T> wraps DML in a subquery which PostgreSQL rejects.
        // Use raw ADO.NET so INSERT...RETURNING executes directly.
        // CloseConnectionAsync in finally: explicit OpenConnectionAsync marks the connection
        // as externally owned, so EF Core will NOT close it on DbContext disposal.
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            await using var cmd = db.Database.GetDbConnection().CreateCommand();
            // WHERE clause prevents incrementing past 36 (NACHA daily limit).
            // When the condition is false PostgreSQL treats it as DO NOTHING and
            // RETURNING returns no rows — ExecuteScalarAsync returns DBNull.
            // Use Eastern Time date to match FedACH business day boundaries.
            // CURRENT_DATE uses the PostgreSQL server's UTC date, which rolls at 00:00 UTC —
            // 4–5 hours before FedACH's day boundary — causing file_id_modifier collisions
            // in the overnight window.
            cmd.CommandText = @"
                INSERT INTO ""AchDailyCounters"" (""Date"", ""Value"")
                VALUES ((NOW() AT TIME ZONE 'America/New_York')::date, 1)
                ON CONFLICT (""Date"") DO UPDATE
                    SET ""Value"" = ""AchDailyCounters"".""Value"" + 1
                WHERE ""AchDailyCounters"".""Value"" < 36
                RETURNING ""Value""";

            var result = await cmd.ExecuteScalarAsync(ct);
            if (result is null or DBNull)
                throw new InvalidOperationException(
                    "NACHA daily file limit reached (36 files today, max per spec). " +
                    "Try again tomorrow or batch multiple entries per file to increase throughput.");
            return Convert.ToInt32(result);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    // NACHA allows A–Z (1–26) then 0–9 (27–36) as file_id_modifier per originator per day.
    // After 36, there are no unique single-char values left in the NACHA spec.
    public static char FileIdModifier(int seq) =>
        seq is >= 1 and <= 26 ? (char)('A' + seq - 1) :
        seq is >= 27 and <= 36 ? (char)('0' + seq - 27) :
        throw new InvalidOperationException(
            $"NACHA daily file limit exceeded ({seq} files today, max 36 per spec). " +
            "Batch multiple entries per file to increase daily throughput.");
}
