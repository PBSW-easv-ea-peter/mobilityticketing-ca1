using System.Collections.Concurrent;
using System.Text.Json;
using StackExchange.Redis;

public record CacheEntry(int SchemaVersion, DateTimeOffset CachedAtUtc, List<Journey> Items);
public sealed class CachedJourneySearch(IDatabase cache, MongoJourneySearch source, TimeSpan ttl)
{
    public int MongoCalls => source.Calls;
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        RespectNullableAnnotations = true
    };
    // One lock per cache key, so only one request per search goes to MongoDB on a miss (task 7)
    readonly ConcurrentDictionary<string, SemaphoreSlim> gates = new();

    public async Task<List<Journey>> Search(SearchRequest q)
    {
        string key = CacheKeys.Search(q);
        var (entry, redisOnline) = await ReadCache(key);
        if (entry is not null)
        {
            Console.WriteLine("hit");
            return entry.Items;
        }

        // Without Redis the waiting requests could not read the result anyway, so skip the lock
        if (!redisOnline) return await FetchAndStore(key, q);

        var gate = gates.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            // Another request may have stored the result while this one was waiting
            (entry, _) = await ReadCache(key, log: false);
            if (entry is not null)
            {
                Console.WriteLine("hit after waiting");
                return entry.Items;
            }
            return await FetchAndStore(key, q);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<(CacheEntry? Entry, bool RedisOnline)> ReadCache(string key, bool log = true)
    {
        RedisValue raw;
        try // GET key
        {
            raw = await cache.StringGetAsync(key);
        } catch(Exception e) when (e is RedisException or TimeoutException) //→ "cache unavailable", gå videre til MongoDB
        {
            if (log) Console.WriteLine("cache unavailable");
            return (null, false);
        }

        if (raw.IsNull)
        {
            if (log) Console.WriteLine("miss");
            return (null, true);
        }
        var entry = TryRead(raw!);
        if (entry is null && log) Console.WriteLine("invalid");
        return (entry, true);
    }

    private async Task<List<Journey>> FetchAndStore(string key, SearchRequest q)
    {
        List<Journey> items = await source.Fetch(q);

        try
        {
            var json = JsonSerializer.Serialize(new CacheEntry(2, DateTimeOffset.UtcNow, items), Json);
            await cache.StringSetAsync(key, json, ttl);
        }
        catch(Exception e) when (e is RedisException or TimeoutException)
        {
            Console.WriteLine("cache write failed");
        }
        return items;
    }

    private CacheEntry? TryRead(string raw)
    {
        try
        {
            var entry = JsonSerializer.Deserialize<CacheEntry>(raw, Json);
            if (entry is null) return null;
            if (entry.SchemaVersion != 2) return null;
            if (entry.CachedAtUtc == default) return null;
            if (entry.Items is null) return null;
            if (entry.Items.Any(j => j is null)) return null;
            return entry;
        }
        catch (JsonException)
        {
            return null;                   // "not-json" lander her
        }
    }
}
