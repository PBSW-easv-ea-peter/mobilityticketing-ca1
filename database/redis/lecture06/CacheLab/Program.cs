using System.Text.Json;
var source = new MongoJourneySearch();

// Task 2: cache key tests
var morning = new SearchRequest("CPH", "STOP-NORREPORT", "STOP-AIRPORT",
    DateTimeOffset.Parse("2026-10-02T06:00:00Z"),
    DateTimeOffset.Parse("2026-10-02T07:00:00Z"));

var evening = morning with {
    Start = DateTimeOffset.Parse("2026-10-02T18:00:00Z"),
    End   = DateTimeOffset.Parse("2026-10-02T19:00:00Z")
};
var reversed = morning with { FromStopId = morning.ToStopId, ToStopId = morning.FromStopId };
var morningLocal = morning with {
    Start = DateTimeOffset.Parse("2026-10-02T08:00:00+02:00"),
    End   = DateTimeOffset.Parse("2026-10-02T09:00:00+02:00")
};
var colonFirst  = morning with { FromStopId = "A:B", ToStopId = "C" };
var colonSecond = morning with { FromStopId = "A",   ToStopId = "B:C" };

Console.WriteLine("Task 2: Cache keys");
CheckKeys("Morning vs evening",        morning,    evening,      expectSame: false);
CheckKeys("Origin/destination swapped", morning,    reversed,     expectSame: false);
CheckKeys("06:00Z vs 08:00+02:00",      morning,    morningLocal, expectSame: true);
CheckKeys("A:B/C vs A/B:C",             colonFirst, colonSecond,  expectSame: false);
Rejects("Empty origin",  morning with { FromStopId = "" });
Rejects("End == start",  morning with { End = morning.Start });

static void CheckKeys(string name, SearchRequest a, SearchRequest b, bool expectSame)
{
    var keyA = CacheKeys.Search(a);
    var keyB = CacheKeys.Search(b);
    var ok = (keyA == keyB) == expectSame;
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}");
    Console.WriteLine($"        {keyA}");
    Console.WriteLine($"        {keyB}");
}

static void Rejects(string name, SearchRequest q)
{
    try
    {
        CacheKeys.Search(q);
        Console.WriteLine($"FAIL  {name}: not rejected");
    }
    catch (ArgumentException e)
    {
        Console.WriteLine($"PASS  {name}: {e.Message}");
    }
}

Console.WriteLine();
Console.WriteLine("Task 3: Cache the Journey search");

var request = new SearchRequest("CPH", "STOP-NORREPORT", "STOP-AIRPORT",
    DateTimeOffset.Parse("2026-10-02T06:00:00Z"),
    DateTimeOffset.Parse("2026-10-02T07:00:00Z"));

var result = await source.Fetch(request);
Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
Console.WriteLine($"MongoDB calls: {source.Calls}");
// Next: replace the direct Fetch call with your cached search.
