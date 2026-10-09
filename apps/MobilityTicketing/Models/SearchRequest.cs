namespace MobilityTicketing.Models;


public class SearchRequest
{
    public string CityId { get; }
    public string FromStopId { get; }
    public string ToStopId { get; }
    public DateTimeOffset Start { get; }
    public DateTimeOffset End { get; }
    
    public SearchRequest(
        string cityId,
        string fromStopId,
        string toStopId,
        DateTimeOffset start,
        DateTimeOffset end)
    {
        // Check if any of the search parameters are empty
        if (string.IsNullOrEmpty(cityId) ||
            string.IsNullOrEmpty(fromStopId) ||
            string.IsNullOrEmpty(toStopId))
        {
            throw new ArgumentException("Search IDs cannot be empty.");
        }

        // Check if end time is after start time
        if (end <= start)
        {
            throw new ArgumentException(
                "End time must be after start time.");
        }
        
        
        CityId = cityId;
        FromStopId = fromStopId;
        ToStopId = toStopId;
        Start = start;
        End = end;
    }
}

