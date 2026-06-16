using Microsoft.EntityFrameworkCore;
using UsBankSystem.Infrastructure.Persistence;

namespace UsBankSystem.Api.Integrations;

/// <summary>
/// DB-backed per-day sequence counter for NACHA trace numbers and file_id_modifier.
/// Uses a PostgreSQL atomic upsert so multiple API instances never issue duplicate
/// sequence numbers on the same business day.
/// </summary>
public sealed class AchTraceSequencer(IServiceScopeFactory scopeFactory)
{
    public async Task<int> NextAsync(CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var result = await db.Database
            .SqlQueryRaw<int>(@"
                INSERT INTO ""AchDailyCounters"" (""Date"", ""Value"")
                VALUES (CURRENT_DATE, 1)
                ON CONFLICT (""Date"") DO UPDATE
                    SET ""Value"" = ""AchDailyCounters"".""Value"" + 1
                RETURNING ""Value""")
            .ToListAsync(ct);
        return result[0];
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
