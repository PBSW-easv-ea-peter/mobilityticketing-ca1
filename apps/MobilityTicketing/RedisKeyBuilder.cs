using System.Globalization;

namespace MobilityTicketing;

public static class RedisKeyBuilder
{
    static string Part(string value) =>
        Uri.EscapeDataString(value);

    static string Instant(DateTimeOffset value) =>
        value.UtcDateTime.ToString(
            "O",
            CultureInfo.InvariantCulture);

    public static string BuildSearchKey(
        string cityId,
        string fromStopId,
        string toStopId,
        DateTimeOffset start,
        DateTimeOffset end)
    {
        if (string.IsNullOrEmpty(cityId) ||
            string.IsNullOrEmpty(fromStopId) ||
            string.IsNullOrEmpty(toStopId))
        {
            throw new ArgumentException("Search IDs cannot be empty.");
        }

        if (end <= start)
        {
            throw new ArgumentException(
                "End time must be after start time.");
        }

        return string.Join(":",
            "search",
            "LAB06",
            "v2",
            Part(cityId),
            Part(fromStopId),
            Part(toStopId),
            Part(Instant(start)),
            Part(Instant(end)));
    }
}
