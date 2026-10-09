using System.Globalization;
using MobilityTicketing.Models;

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
        SearchRequest request)
    {
        return string.Join(":",
            "search",
            "LAB06",
            "v2",
            Part(request.CityId),
            Part(request.FromStopId),
            Part(request.ToStopId),
            Part(Instant(request.Start)),
            Part(Instant(request.End)));
    }
}
