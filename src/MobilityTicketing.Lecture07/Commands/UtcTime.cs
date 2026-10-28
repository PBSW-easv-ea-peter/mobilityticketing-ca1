namespace MobilityTicketing.Lecture07.Commands;

public static class UtcTime
{
    // PostgreSQL stores microseconds; .NET ticks are 100 ns. Truncate so a saved value matches what is read back.
    public static DateTime NowToMicroseconds(TimeProvider clock)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        return new DateTime(now.Ticks - now.Ticks % TimeSpan.TicksPerMicrosecond, DateTimeKind.Utc);
    }
}
