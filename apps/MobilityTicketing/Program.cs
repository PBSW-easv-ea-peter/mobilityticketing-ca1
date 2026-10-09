using System.Diagnostics;
using System.Text.Json;
using MobilityTicketing;
using MobilityTicketing.Models;
using StackExchange.Redis;

// Setup test parameters
string city = "CPH";
string fromStop = "STOP-NORREPORT";
string toStop = "STOP-AIRPORT";
DateTimeOffset morningStart = new(2026, 10, 2, 6, 0, 0, TimeSpan.Zero);
DateTimeOffset morningEnd = new(2026, 10, 2, 7, 0, 0, TimeSpan.Zero);

SearchRequest morningRequest = new(city, fromStop, toStop, morningStart, morningEnd);

MongoJourneySearch source = new();

// Helper method to get cache key
string GetCacheKey(SearchRequest request) => RedisKeyBuilder.BuildSearchKey(request);

// Helper method to format results
string FormatResults(List<Journey> journeys) =>
    string.Join(", ", journeys.Select(j => j.TripId));

// Helper method to delete Redis key
async Task DeleteRedisKey(string key)
{
    try
    {
        var options = ConfigurationOptions.Parse("127.0.0.1:6379");
        options.AbortOnConnectFail = false;
        await using var redis = await ConnectionMultiplexer.ConnectAsync(options);
        await redis.GetDatabase().KeyDeleteAsync(key);
    }
    catch { /* Ignore Redis errors */ }
}

// Helper method to run concurrent tests
async Task ConcurrentTestResult(string testName, string cacheKey, SearchRequest request, int concurrentRequests, bool deleteKeyFirst = false)
{
    if (deleteKeyFirst)
    {
        await DeleteRedisKey(cacheKey);
    }
    
    int callsBefore = source.Calls;
    var stopwatch = Stopwatch.StartNew();
    
    // Run concurrent requests
    var tasks = Enumerable.Range(0, concurrentRequests)
        .Select(_ => source.Fetch(request))
        .ToList();
    
    List<Journey>[] results = await Task.WhenAll(tasks);
    stopwatch.Stop();
    
    int callsAfter = source.Calls;
    int mongoCalls = callsAfter - callsBefore;
    long elapsedMs = stopwatch.ElapsedMilliseconds;
    
    // Check if all results are the same
    bool allResultsSame = results.All(r => 
        r.SequenceEqual(results[0], new JourneyEqualityComparer()));
    
    Console.WriteLine($"\n--- Test {testName} ---");
    Console.WriteLine($"Cache key: {cacheKey}");
    Console.WriteLine($"Concurrent requests: {concurrentRequests}");
    Console.WriteLine($"MongoDB-call count: {mongoCalls}");
    Console.WriteLine($"Elapsed time: {elapsedMs}ms");
    Console.WriteLine($"All results identical: {allResultsSame}");
    if (results.Length > 0 && results[0].Count > 0)
    {
        Console.WriteLine($"Returned trip IDs: {FormatResults(results[0])}");
    }
}

// Run tests
Console.WriteLine("=== Task 07: Concurrent Searches ===\n");

string morningKey = GetCacheKey(morningRequest);

// Set artificial delay of 200ms
source.DelayMilliseconds = 200;
Console.WriteLine("Set artificial delay to 200ms");

// First batch: 10 concurrent requests with empty cache
Console.WriteLine("\n--- Test 07A: First batch of 10 concurrent requests (empty cache) ---");
await ConcurrentTestResult("07A", morningKey, morningRequest, 10, deleteKeyFirst: true);

// Second batch: 10 concurrent requests with existing cache
Console.WriteLine("\n--- Test 07B: Second batch of 10 concurrent requests (cached) ---");
await ConcurrentTestResult("07B", morningKey, morningRequest, 10, deleteKeyFirst: false);

// Remove artificial delay
source.DelayMilliseconds = 0;
Console.WriteLine("\nRemoved artificial delay");

// Explanation
Console.WriteLine("\n=== Explanation ===");
Console.WriteLine("The first batch can query MongoDB more than once because:");
Console.WriteLine("1. All 10 requests start simultaneously and check the cache first");
Console.WriteLine("2. Since the cache is empty (key was deleted), all requests get a cache miss");
Console.WriteLine("3. All requests then proceed to query MongoDB before any of them have finished");
Console.WriteLine("4. With the 200ms artificial delay, multiple requests can enter the MongoDB query section");
Console.WriteLine("5. Each request that enters the MongoDB section increments the call counter and executes the query");
Console.WriteLine("6. This is a race condition: the cache check and MongoDB query are not atomic");
Console.WriteLine("\nThe second batch should show only 0-1 MongoDB calls because:");
Console.WriteLine("1. The cache now contains valid data from the first batch");
Console.WriteLine("2. All concurrent requests will find the cached data and return it");
Console.WriteLine("3. No requests need to query MongoDB");

Console.WriteLine("\n=== All Tests Complete ===");

// Journey equality comparer for checking if results are the same
public class JourneyEqualityComparer : IEqualityComparer<Journey>
{
    public bool Equals(Journey? x, Journey? y)
    {
        if (x == null || y == null) return false;
        return x.TripId == y.TripId &&
               x.CityId == y.CityId &&
               x.RouteId == y.RouteId &&
               x.FromStopId == y.FromStopId &&
               x.ToStopId == y.ToStopId &&
               x.DepartureUtc == y.DepartureUtc &&
               x.ArrivalUtc == y.ArrivalUtc &&
               x.AvailableSeats == y.AvailableSeats &&
               x.Price == y.Price &&
               x.Currency == y.Currency;
    }
    
    public int GetHashCode(Journey obj)
    {
        return obj.TripId.GetHashCode();
    }
}