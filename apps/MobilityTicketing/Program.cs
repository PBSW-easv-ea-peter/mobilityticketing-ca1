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
DateTimeOffset eveningStart = new(2026, 10, 2, 18, 0, 0, TimeSpan.Zero);
DateTimeOffset eveningEnd = new(2026, 10, 2, 19, 0, 0, TimeSpan.Zero);

SearchRequest morningRequest = new(city, fromStop, toStop, morningStart, morningEnd);
SearchRequest eveningRequest = new(city, fromStop, toStop, eveningStart, eveningEnd);

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

// Helper method to set Redis key with specific value and expiry
async Task SetRedisKey(string key, string value, TimeSpan? expiry = null)
{
    try
    {
        var options = ConfigurationOptions.Parse("127.0.0.1:6379");
        options.AbortOnConnectFail = false;
        await using var redis = await ConnectionMultiplexer.ConnectAsync(options);
        var db = redis.GetDatabase();
        if (expiry.HasValue)
        {
            await db.StringSetAsync(key, value, expiry.Value);
        }
        else
        {
            await db.StringSetAsync(key, value);
        }
    }
    catch { /* Ignore Redis errors */ }
}

// Helper method to run a test and record results
async Task TestResult(string testName, string cacheKey, SearchRequest request, bool deleteKeyFirst = false)
{
    if (deleteKeyFirst)
    {
        await DeleteRedisKey(cacheKey);
    }
    
    int callsBefore = source.Calls;
    var stopwatch = Stopwatch.StartNew();
    List<Journey> result = await source.Fetch(request);
    stopwatch.Stop();
    int callsAfter = source.Calls;
    
    string dataSource = callsAfter > callsBefore ? "MongoDB" : "Redis";
    string tripIds = FormatResults(result);
    int mongoCalls = callsAfter - callsBefore;
    long elapsedMs = stopwatch.ElapsedMilliseconds;
    
    Console.WriteLine($"\n--- Test {testName} ---");
    Console.WriteLine($"Cache key: {cacheKey}");
    Console.WriteLine($"Data source: {dataSource}");
    Console.WriteLine($"Returned trip IDs: {tripIds}");
    Console.WriteLine($"MongoDB-call count: {mongoCalls}");
    Console.WriteLine($"Elapsed time: {elapsedMs}ms");
}

// Run tests
Console.WriteLine("=== Starting Cache Tests ===\n");

// Test A: Repeat a search
string morningKey = GetCacheKey(morningRequest);

// First search (delete key first to ensure MongoDB call)
await TestResult("A1", morningKey, morningRequest, deleteKeyFirst: true);

// Second search (should be cached)
await TestResult("A2", morningKey, morningRequest, deleteKeyFirst: false);

// Test B: Cache an empty result
string eveningKey = GetCacheKey(eveningRequest);

// First evening search (should return empty from MongoDB)
await TestResult("B1", eveningKey, eveningRequest, deleteKeyFirst: true);

// Second evening search (should be cached empty result)
await TestResult("B2", eveningKey, eveningRequest, deleteKeyFirst: false);

// Test C: Expire a result
Console.WriteLine($"\n--- Test C ---");
Console.WriteLine($"Cache key: {morningKey}");

// Set expiry to 1 second
Console.WriteLine("Setting Redis key expiry to 1 second...");
try
{
    var options = ConfigurationOptions.Parse("127.0.0.1:6379");
    options.AbortOnConnectFail = false;
    await using var redis = await ConnectionMultiplexer.ConnectAsync(options);
    await redis.GetDatabase().KeyExpireAsync(morningKey, TimeSpan.FromSeconds(1));
}
catch { /* Ignore Redis errors */ }

// Wait until TTL returns -2 (expired)
Console.WriteLine("Waiting for key to expire...");
await Task.Delay(1000);

// Run search again (should call MongoDB since cache expired)
await TestResult("C1", morningKey, morningRequest, deleteKeyFirst: false);

// Test D: Store an invalid value
Console.WriteLine($"\n--- Test D ---");
Console.WriteLine($"Cache key: {morningKey}");

// D1: Replace with non-JSON value
Console.WriteLine("Setting cache to invalid value: 'not-json'");
await SetRedisKey(morningKey, "not-json", TimeSpan.FromSeconds(20));
await TestResult("D1", morningKey, morningRequest, deleteKeyFirst: false);

// D2: Replace with JSON containing wrong schemaVersion
Console.WriteLine("Setting cache to JSON with wrong schemaVersion");
string wrongSchemaJson = "{\"schemaVersion\": 999, \"cachedAtUtc\": \"2026-10-09T08:00:00Z\", \"items\": []}";
await SetRedisKey(morningKey, wrongSchemaJson, TimeSpan.FromSeconds(20));
await TestResult("D2", morningKey, morningRequest, deleteKeyFirst: false);

// D3: Replace with JSON missing items field
Console.WriteLine("Setting cache to JSON missing items field");
string missingItemsJson = "{\"schemaVersion\": 2, \"cachedAtUtc\": \"2026-10-09T08:00:00Z\"}";
await SetRedisKey(morningKey, missingItemsJson, TimeSpan.FromSeconds(20));
await TestResult("D3", morningKey, morningRequest, deleteKeyFirst: false);

Console.WriteLine("\n=== All Tests Complete ===");
