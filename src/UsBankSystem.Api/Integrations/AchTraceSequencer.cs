namespace UsBankSystem.Api.Integrations;

/// <summary>
/// Thread-safe per-day sequence counter for NACHA trace numbers and file_id_modifier.
/// NACHA requires trace number uniqueness per originator per business day.
/// Seeded from the current second-of-day on startup so a mid-day restart won't
/// reuse sequence numbers issued before the restart.
/// For multi-instance deployments replace with a DB-backed atomic sequence.
/// </summary>
public sealed class AchTraceSequencer
{
    private int _lastDayKey;
    private int _counter;
    private readonly object _lock = new();

    public AchTraceSequencer()
    {
        var now = DateTime.UtcNow;
        _lastDayKey = now.Year * 1000 + now.DayOfYear;
        // Seed from second-of-day (0–86 399) so mid-day restarts don't collide
        // with sequence numbers already issued earlier the same business day.
        _counter = (int)now.TimeOfDay.TotalSeconds;
    }

    public int Next()
    {
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            var dayKey = now.Year * 1000 + now.DayOfYear;
            if (dayKey != _lastDayKey)
            {
                _lastDayKey = dayKey;
                _counter = 0;
            }
            return ++_counter; // fits in NACHA's 7-digit sequence field (max 9,999,999/day)
        }
    }

    // Maps a 1-based daily sequence to a single NACHA file_id_modifier char.
    // NACHA allows A–Z (1–26) then 0–9 (27–36). Wraps A–Z after 36.
    public static char FileIdModifier(int seq) =>
        seq is >= 1 and <= 26 ? (char)('A' + seq - 1) :
        seq is >= 27 and <= 36 ? (char)('0' + seq - 27) :
        (char)('A' + (seq - 1) % 26);
}
