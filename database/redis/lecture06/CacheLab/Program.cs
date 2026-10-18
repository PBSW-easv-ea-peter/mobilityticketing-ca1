using System.Diagnostics;
using StackExchange.Redis;
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

// Task 3 + 4: cached search
var options = ConfigurationOptions.Parse("127.0.0.1:6379");
options.AbortOnConnectFail = false;
options.ConnectTimeout = 500;
options.AsyncTimeout = 500;
options.ConnectRetry = 1;

using var redis = await ConnectionMultiplexer.ConnectAsync(options);
var cache = redis.GetDatabase();
// "dotnet run -- morning" runs one cached search with a longer expiry (task 5)
var singleMorning = args is ["morning"];
var cacheTtl = singleMorning ? TimeSpan.FromMinutes(5) : TimeSpan.FromSeconds(20);
var cached = new CachedJourneySearch(cache, source, cacheTtl);

var morningKey = CacheKeys.Search(morning);
var eveningKey = CacheKeys.Search(evening);

// Runs one cached search and prints what the lab asks us to record.
async Task<(int Calls, List<Journey> Items)> Run(string label, SearchRequest q)
{
    var before = cached.MongoCalls;
    var sw = Stopwatch.StartNew();
    var items = await cached.Search(q);
    sw.Stop();
    var calls = cached.MongoCalls - before;
    var trips = items.Count == 0 ? "[]" : string.Join(", ", items.Select(j => j.TripId));
    Console.WriteLine($"        {label}: trips={trips}  mongoCalls=+{calls} (total {cached.MongoCalls})  {sw.ElapsedMilliseconds} ms");
    return (calls, items);
}

static void Check(string name, bool ok) => Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}");

if (singleMorning)
{
    Console.WriteLine($"Single search   key={morningKey}   ttl={cacheTtl}");
    await Run("morning", morning);
    return;
}

Console.WriteLine();
Console.WriteLine("Task 4: Test the cached search");

// Test A: first search misses, second hits
Console.WriteLine($"Test A: repeat a search   key={morningKey}");
await cache.KeyDeleteAsync(morningKey);
var a1 = await Run("1st", morning);
var a2 = await Run("2nd", morning);
Check("Test A: 1st calls MongoDB, 2nd is served from Redis with the same trips",
    a1.Calls == 1 && a2.Calls == 0 &&
    a1.Items.Select(j => j.TripId).SequenceEqual(a2.Items.Select(j => j.TripId)));

// Test B: an empty result is cached too
Console.WriteLine($"Test B: cache an empty result   key={eveningKey}");
await cache.KeyDeleteAsync(eveningKey);
var b1 = await Run("1st", evening);
var b2 = await Run("2nd", evening);
Check("Test B: 2nd evening search is a hit returning []",
    b1.Calls == 1 && b2.Calls == 0 && b2.Items.Count == 0);

// Test C: expired key goes back to MongoDB and is stored again with an expiry
Console.WriteLine($"Test C: expire a result   key={morningKey}");
await cache.KeyExpireAsync(morningKey, TimeSpan.FromSeconds(1));
while (await cache.KeyExistsAsync(morningKey)) await Task.Delay(200);
Console.WriteLine("        key expired (TTL -2)");
var c1 = await Run("after expiry", morning);
var ttl = await cache.KeyTimeToLiveAsync(morningKey);
Console.WriteLine($"        new TTL: {ttl?.TotalSeconds:0} s");
Check("Test C: expired key calls MongoDB and is saved with a new expiry",
    c1.Calls == 1 && ttl is { TotalSeconds: > 0 });

// Test D: invalid cached values count as a miss and are replaced
Console.WriteLine($"Test D: store an invalid value   key={morningKey}");
var invalidValues = new (string Name, string Value)[]
{
    ("not JSON",             "not-json"),
    ("wrong schemaVersion",  """{"schemaVersion":1,"cachedAtUtc":"2026-10-09T08:00:00Z","items":[]}"""),
    ("missing items",        """{"schemaVersion":2,"cachedAtUtc":"2026-10-09T08:00:00Z"}"""),
};
foreach (var (name, value) in invalidValues)
{
    await cache.StringSetAsync(morningKey, value, TimeSpan.FromSeconds(20));
    var d = await Run(name, morning);
    var stored = (string?)await cache.StringGetAsync(morningKey);
    Check($"Test D ({name}): invalid value triggers MongoDB and is replaced",
        d.Calls == 1 && stored != value);
}
