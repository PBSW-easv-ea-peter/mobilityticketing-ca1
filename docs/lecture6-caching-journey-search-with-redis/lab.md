# Redis: Caching Journey Search

Add Redis caching to your MobilityTicketing journey search. Test your cache keys, expiry and failure handling, then try an atomic seat counter.

Keep the five search inputs and result fields from last week. All lab times are UTC.

Continue in your existing repository. If you need a starting point, the [Lecture 6 starter](https://github.com/patrickstolc/mobilityticketing-lecture-6-starter) includes the MongoDB query and Redis setup.

Save your scripts in database/redis/lecture06/ and your observations in docs/evidence/lecture06/README.md.

## 1. Start Redis

Add this service under services in your existing Compose file:

```yaml
redis:
  image: redis:7-alpine
  ports:
    - "127.0.0.1:6379:6379"
  command: ["redis-server", "--save", "", "--appendonly", "no"]
  volumes:
    - ./database/redis:/scripts:ro
```

Keep MongoDB and PostgreSQL unchanged. Persistence is disabled because Redis is a disposable cache for this exercise.

Start the services and check Redis:

```bash
docker compose up -d mongo redis
docker compose exec -T redis redis-cli PING
```

Redis replies PONG.

1. Open the Redis command line:

```bash
docker compose exec redis redis-cli
```

2. Store a stop name with a 20-second expiry:

```
SET stop:LAB06:STOP-NORREPORT "Norreport" EX 20
GET stop:LAB06:STOP-NORREPORT
TTL stop:LAB06:STOP-NORREPORT
```

3. Wait for the value to expire. Run GET and TTL again.
4. Use exit to leave redis-cli.

Before expiry, GET returns the stop name and TTL shows the remaining lifetime. After expiry, they return nil and -2.

Use LAB06 in your experiment's keys. Delete individual keys with DEL; do not use FLUSHALL.

### Check last week's search

Reset the MongoDB test data:

```bash
docker compose exec -T mongo \
  mongosh --quiet --file /scripts/lecture05/reset.js
```

Run your uncached search with these inputs:

| Input |	Value |
|-------|---------|
| City |	CPH |
| From |	STOP-NORREPORT |
| To |	STOP-AIRPORT |
| Date |	2 October 2026 |
| Time |	06:00–07:00 UTC |

Only LAB05-T-OK is returned.

Fix any search problems before adding the cache.

## 2. Choose a cache key

1. Store the morning search's JSON result under this deliberately incomplete key:

`search:LAB06:CPH:STOP-NORREPORT:STOP-AIRPORT`

Use a five-minute expiry for this experiment.

2. Run the uncached search for **18:00–19:00 UTC** on the same date.
3. Retrieve the value stored under the incomplete key.
4. Record the key and both results.

The uncached evening search returns [], but Redis still contains the morning result.

### Build a complete key

Write a function that includes:

- City
- Origin
- Destination
- Start time
- End time
- A format version, such as v2

For C#, these helpers provide consistent UTC times and escape separators inside IDs:

```c#
static string Part(string value) => Uri.EscapeDataString(value);

static string Instant(DateTimeOffset value) =>
    value.UtcDateTime.ToString(
        "O",
        System.Globalization.CultureInfo.InvariantCulture);
```

Build the key from search, LAB06, v2 and all five inputs. Use Part(...) on each input and Instant(...) on the two times.

Keep IDs case-sensitive. Reject empty IDs and an end time that is not after the start.

### Check your key function

| Test |	Expected behaviour |
|------|------|
| Morning and evening searches |	Different keys |
| Origin and destination reversed |	Different keys |
| The same instant written as 06:00Z or 08:00+02:00 |	Same key |
| Stop pairs A:B / C and A / B:C |	Different keys |

Why did the incomplete key allow the evening search to receive the morning result?
## 3. Cache the journey search

Implement **cache-aside**: check Redis before calling MongoDB.

Keep your working MongoDB query and add the cache around it. Use your existing application language.

A script-based solution may run your real mongosh query and use redis-cli. The query must print one JSON array rather than a fixed test result.

For a new C# console program:

```powershell
dotnet new console -o database/redis/lecture06/CacheLab
dotnet add database/redis/lecture06/CacheLab package MongoDB.Driver
dotnet add database/redis/lecture06/CacheLab package StackExchange.Redis
```

Use .NET 8 or later. Keep the package versions recorded in the project file.

### C# starter: connect to the existing MongoDB data

The C# helper supplies the original journey_search query. Adapt it or reuse your own query if you kept the alternative collection. Save this as MongoJourneySearch.cs in database/redis/lecture06/CacheLab/.

```c#
using System.Globalization;
using System.Text.Json.Serialization;
using MongoDB.Bson;
using MongoDB.Driver;

public record SearchRequest(string CityId, string FromStopId, string ToStopId,
    DateTimeOffset Start, DateTimeOffset End);

public record Journey(
    [property: JsonRequired] string CityId,
    [property: JsonRequired] string RouteId,
    [property: JsonRequired] string FromStopId,
    [property: JsonRequired] string ToStopId,
    [property: JsonRequired] string TripId,
    [property: JsonRequired] DateTimeOffset DepartureUtc,
    [property: JsonRequired] DateTimeOffset ArrivalUtc,
    [property: JsonRequired] int AvailableSeats,
    [property: JsonRequired] decimal Price,
    [property: JsonRequired] string Currency);

public sealed class MongoJourneySearch
{
    private readonly IMongoCollection<BsonDocument> collection;
    private int calls;
    public int Calls => Volatile.Read(ref calls);
    public int DelayMilliseconds { get; set; } // Only for section 7.

    public MongoJourneySearch()
    {
        var settings = MongoClientSettings.FromConnectionString("mongodb://127.0.0.1:27017");
        settings.ServerSelectionTimeout = TimeSpan.FromSeconds(2);
        settings.ConnectTimeout = TimeSpan.FromSeconds(1);
        collection = new MongoClient(settings).GetDatabase("mobility")
            .GetCollection<BsonDocument>("journey_search");
    }

    public async Task<List<Journey>> Fetch(SearchRequest q)
    {
        if (DelayMilliseconds > 0) await Task.Delay(DelayMilliseconds);
        Interlocked.Increment(ref calls);
        var range = new BsonDocument("$gte", new BsonDateTime(q.Start.UtcDateTime))
            .Add("$lt", new BsonDateTime(q.End.UtcDateTime));
        var pipeline = new[] {
            new BsonDocument("$match", new BsonDocument {
                {"_id", new BsonRegularExpression("^LAB05:")},
                {"cityId", q.CityId}, {"fromStopId", q.FromStopId}, {"toStopId", q.ToStopId}
            }),
            new BsonDocument("$unwind", "$departures"),
            new BsonDocument("$match", new BsonDocument {
                {"departures.status", "Scheduled"}, {"departures.departureUtc", range}
            }),
            new BsonDocument("$sort", new BsonDocument("departures.departureUtc", 1))
        };
        var documents = await collection.Aggregate<BsonDocument>(pipeline).ToListAsync();
        return documents.Select(d => {
            var t = d["departures"].AsBsonDocument;
            return new Journey(d["cityId"].AsString, d["routeId"].AsString,
                d["fromStopId"].AsString, d["toStopId"].AsString, t["tripId"].AsString,
                t["departureUtc"].AsBsonDateTime.ToUniversalTime(),
                t["arrivalUtc"].AsBsonDateTime.ToUniversalTime(),
                t["availableSeats"].ToInt32(),
                decimal.Parse(d["price"].ToString()!, CultureInfo.InvariantCulture),
                d["currency"].AsString);
        }).ToList();
    }
}
```

Replace the generated Program.cs with this starting point:

```c#
using System.Text.Json;

var source = new MongoJourneySearch();
var request = new SearchRequest("CPH", "STOP-NORREPORT", "STOP-AIRPORT",
    DateTimeOffset.Parse("2026-10-02T06:00:00Z"),
    DateTimeOffset.Parse("2026-10-02T07:00:00Z"));

var result = await source.Fetch(request);
Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
Console.WriteLine($"MongoDB calls: {source.Calls}");
// Next: replace the direct Fetch call with your cached search.
```

Run it from your repository root:

```ps
dotnet run --project database/redis/lecture06/CacheLab
```

You should get LAB05-T-OK and one MongoDB call. Add your Redis wrapper around source.Fetch(request).

1. Create CachedJourneySearch around your MongoDB search.
2. Validate the request before accessing Redis.
3. Build the complete cache key and look it up.
4. Return a valid cached result without calling MongoDB. Print hit.
5. If the key is missing, print miss and call MongoDB.
6. Count the actual MongoDB calls.
7. Save the result as JSON with a configurable 20-second expiry.
8. Return the same journey fields as your uncached search.

An empty result is valid and should also be cached.

### Cache format

Use this structure:

```json
{
  schemaVersion: 2,
  cachedAtUtc: "2026-10-09T08:00:00Z", // Use the current UTC time.
  items: [] // Use the actual search results.
}
```

Set the value and expiry together. For example, in C#:

```c#
await cache.StringSetAsync(key, json, TimeSpan.FromSeconds(20));
```

Do not renew the expiry on every read.

### Invalid cached values

Check that the cached value:

- Contains readable JSON
- Has the expected schema version and timestamp
- Contains an array of valid journey results

Treat invalid values as a cache miss and print invalid.
### Connection failures

Your program must behave as follows:

| Situation |	Required behaviour |
|-----------|----------------------|
| Redis read fails |	Print cache unavailable and try MongoDB |
| Redis write fails after a successful MongoDB search |	Return the search result |
|MongoDB fails and no usable cached result exists |	Report a search error |

Do not return [] to disguise a failed search.

For C#, reuse one ConnectionMultiplexer. These settings support short timeouts and startup without Redis:

```c#
var options = ConfigurationOptions.Parse("127.0.0.1:6379");
options.AbortOnConnectFail = false;
options.ConnectTimeout = 500;
options.AsyncTimeout = 500;
options.ConnectRetry = 1;

using var redis = await ConnectionMultiplexer.ConnectAsync(options);
var cache = redis.GetDatabase();
```

Measure the actual waiting time during the outage test. A configured timeout does not guarantee that the whole search takes that long.

## 4. Test the cached search

For each test, record:

- Cache key
- Data source
- Returned trip IDs
- MongoDB-call count
- Elapsed time

### Test A: Repeat a search

1. Delete the exact morning key.
2. Run the morning search twice within 20 seconds.

The first request calls MongoDB. The second returns the same result from Redis without another MongoDB call.

### Test B: Cache an empty result

1. Run the evening search twice.

The second request is a cache hit returning [].

### Test C: Expire a result

1. Set the morning key's remaining lifetime to one second:

```bash
docker compose exec -T redis redis-cli EXPIRE 'YOUR-KEY' 1
```

2. Wait until TTL returns -2.
3. Run the search again.

Replace YOUR-KEY with the key printed by your program.

The search calls MongoDB and saves a new result with an expiry.

## Test D: Store an invalid value

1. Replace the cached value:

```bash
docker compose exec -T redis redis-cli SET 'YOUR-KEY' 'not-json' EX 20
```

2. Run the search.
3. Repeat with readable JSON containing the wrong schemaVersion.
4. Repeat with readable JSON missing items.

The program identifies the invalid value, fetches from MongoDB and replaces it.

## 5. Cancel a cached departure

1. Reset the MongoDB test data and delete the morning key.
2. Temporarily set the cache expiry to five minutes.
3. Run the morning search to cache its result.
4. Use last week's update to cancel **every copy** of LAB05-T-OK in MongoDB.
5. Run the uncached search.
6. Run the cached search before the key expires.
7. Delete the exact morning key.
8. Run the cached search again.

| Search |	Result |
|--------|---------|
| Uncached search after cancellation |	[] |
| Cached search before expiry or deletion |	Can still return LAB05-T-OK |
| Cached search after deletion |	Fetches from MongoDB and returns [] |

Removing a cached value that should no longer be used is called cache invalidation.

Explain:

- What the passenger saw before and after deletion
- What would happen if MongoDB had not yet received the cancellation
- Why an in-progress search could write an old result back after deletion

Use quiz question 6 to explain the last point. You do not need to implement a solution to that race.

Restore the 20-second expiry and reset the MongoDB test data before continuing.
## 6. Stop Redis
### Test A: Redis is unavailable

1. Stop only Redis:

```bash
docker compose stop redis
```

2. Run your search.
3. Record the elapsed time.

The program reports the cache failure and returns the MongoDB result.

## Test B: Redis and MongoDB are unavailable

1. Keep Redis stopped.
2. Stop MongoDB:

```bash
docker compose stop mongo
```

3. Run the search again.

The search fails visibly instead of returning an empty list.

### Restore the services

```bash
docker compose start mongo redis
```

Once both services are ready, run the search again and check that it works.

## 7. Send several searches at once

1. Delete the exact morning key.
2. Add a temporary delay of about 200 milliseconds immediately before the MongoDB query.
3. Send ten concurrent requests for the same journey.
4. Count the actual MongoDB calls.
5. Run the same batch again before the cache expires.
6. Compare the call counts.
7. Remove the artificial delay.

For example, if your C# function is called Search:

```c#
await Task.WhenAll(
    Enumerable.Range(0, 10).Select(_ => Search(request)));
```

Why can the first batch query MongoDB more than once? Explain what the requests might be doing at the same time.

Optional: try preventing duplicate MongoDB lookups for concurrent requests.

## 8. Try a seat counter

For this exercise, track seats in Redis only.

### Store two counts

Imagine two trips on the same route and date. One has no seats left and the other has ten.

```bash
docker compose exec -T redis redis-cli SET availability:LAB06:TRIP-ONE 0 EX 60
docker compose exec -T redis redis-cli SET availability:LAB06:TRIP-TWO 10 EX 60
```

1. Read both keys.
2. Explain why one key containing only the route and date could not describe both trips' remaining seats.
3. Delete one key and read it again.
4. Explain the difference between a missing value and a stored zero.

### Decrease only a positive count

Save this script as database/redis/lecture06/decrement_if_positive.lua:

```lua
local value = redis.call('GET', KEYS[1])
if not value then return {err = 'availability unknown'} end
local current = tonumber(value)
if not current or current < 0 or current % 1 ~= 0 then
  return {err = 'invalid availability'}
end
if current == 0 then return -1 end
return redis.call('DECR', KEYS[1])
```

The script checks and decreases the counter atomically. Another request cannot change it between those operations.

Start with one remaining seat and run the script twice:

```bash
docker compose exec -T redis redis-cli SET availability:LAB06:TRIP-ONE 1 EX 60
docker compose exec -T redis redis-cli --eval /scripts/lecture06/decrement_if_positive.lua availability:LAB06:TRIP-ONE
docker compose exec -T redis redis-cli --eval /scripts/lecture06/decrement_if_positive.lua availability:LAB06:TRIP-ONE
```

The script returns 0, then -1. The stored value remains 0.

Delete the key and run the script again.

The script reports availability unknown.

Did the script reserve a seat in PostgreSQL, create a ticket or take a payment?

Explain the difference between changing a Redis counter safely and completing a ticket purchase.

## 9. Decide what belongs in Redis

Would you use these expiry times?

| Data |	Proposed expiry |
|------|------|
|Search results |	30 minutes |
| Route/day seat counts |	8 hours |
| Route information |	24 hours |
| Stop information |	24 hours |

1. Store, read and delete one route value using SET, GET and DEL.
2. Do the same for one stop value. These do not need a cache function.
3. Make a table covering:
    - Search results
    - Trip availability
    - Route information
    - Stop information
4. For each row, include an example key and value, source database and your chosen expiry time.
5. Explain who updates each value, when it should be deleted and where it should be fetched if Redis has no usable copy.

Choose an expiry you would try for journey search outside the lab. Explain your choice and what could still make a result out of date.

Will two passengers searching a minute apart share a cache entry? Why?