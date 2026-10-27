using System.Globalization;

public static class CacheKeys
{
    static string Part(string value) => Uri.EscapeDataString(value);

    static string Instant(DateTimeOffset value) =>
        value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
    
    // example input parameters:
    // public record SearchRequest(string CityId, string FromStopId, string ToStopId,
    //DateTimeOffset Start, DateTimeOffset End);
    public static string Search(SearchRequest q)
    {
        // 1. Reject blank ID's          -> ArgumentException
        if (string.IsNullOrWhiteSpace(q.CityId) ||
            string.IsNullOrWhiteSpace(q.FromStopId) ||
            string.IsNullOrWhiteSpace(q.ToStopId)) 
            throw new ArgumentException("CityId, FromStopId and ToStopId are required and cannot be blank", nameof(q));
        
        // 2. Reject End <= Start         -> ArgumentException
        if(q.End <= q.Start)
        {
            throw new ArgumentException("START and END must be valid UTC times with END > START");
        }
        // 3. Byg: search:LAB06:v2:<city>:<from>:<to>:<start>:<end>

        // tested with: Console.WriteLine(CacheKeys.Search(request));
        // from program.cs
        return $"search:LAB06:v2:{Part(q.CityId)}:{Part(q.FromStopId)}:{Part(q.ToStopId)}:{Instant(q.Start)}:{Instant(q.End)}";
    }
}