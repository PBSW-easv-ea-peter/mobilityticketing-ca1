using System.Text.Json;
using MobilityTicketing;
using MobilityTicketing.Models;

Console.WriteLine("Hello, World!");

string city = "CPH";
string fromStop = "STOP-NORREPORT";
string toStop = "STOP-AIRPORT";
DateTimeOffset start = new(2026, 10, 2, 6, 0, 0, TimeSpan.Zero);
DateTimeOffset end = new(2026, 10, 2, 7, 0, 0, TimeSpan.Zero);

SearchRequest request = new(city, fromStop, toStop, start, end);

MongoJourneySearch source = new();


List<Journey> result = await source.Fetch(request);

Console.WriteLine($"Found {result.Count} journeys");

JsonSerializerOptions options = new()
{
    WriteIndented = true
};

string json = JsonSerializer.Serialize(result, options);

Console.WriteLine(json);
Console.WriteLine($"MongoDB calls: {source.Calls}");