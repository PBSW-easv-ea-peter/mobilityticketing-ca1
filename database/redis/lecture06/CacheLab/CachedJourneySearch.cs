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
    public async Task<List<Journey>> Search(SearchRequest q)
    {
        string key = CacheKeys.Search(q);
        RedisValue raw = RedisValue.Null;
        bool _redisOnline = false;
        try // GET key
        {
            raw = await cache.StringGetAsync(key);
            _redisOnline = true;
        } catch(Exception e) when (e is RedisException or TimeoutException) //→ "cache unavailable", gå videre til MongoDB
        {
            _redisOnline = false;
            Console.WriteLine("cache unavailable");
        }

        if (!raw.IsNull)
        {
            var entry = TryRead(raw!);
            if (entry is not null)
            {
                Console.WriteLine("hit");
                return entry.Items;
            }
            Console.WriteLine("invalid");
        }
        else if (_redisOnline)
        {
            Console.WriteLine("miss");
        }
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