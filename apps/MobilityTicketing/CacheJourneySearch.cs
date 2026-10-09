using System.Text.Json;
using System.Text.Json.Nodes;
using MobilityTicketing.Models;
using StackExchange.Redis;

namespace MobilityTicketing;

public class CacheJourneySearch
{
    private readonly ConfigurationOptions _options;
    
    public CacheJourneySearch()
    {
        _options = ConfigurationOptions.Parse("127.0.0.1:6379");
        _options.AbortOnConnectFail = false;
        _options.ConnectTimeout = 500;
        _options.AsyncTimeout = 500;
        _options.ConnectRetry = 1;
    }

#region Get
    public async Task<(List<Journey> Journeys, bool IsCached)> TryGetSearchRequestAsync(SearchRequest request)
    {
        string key = RedisKeyBuilder.BuildSearchKey(request);
        
        RedisValue cached = await TryGetAsync(key);
        
        if (cached.HasValue)
        {
            try
            {
                JsonNode? node = JsonNode.Parse(cached.ToString());
                if (node?["items"] is JsonArray itemsArray)
                {
                    List<Journey> journeys = itemsArray.Deserialize<List<Journey>>() ?? [];
                    return (journeys, true);
                }
            }
            catch (JsonException)
            {
                // Invalid JSON - treat as cache miss
                return ([], false);
            }
        }
  
        return ([], false);
    }
    
    private async Task<RedisValue> TryGetAsync(string key)
    {
        await using ConnectionMultiplexer redis = await ConnectionMultiplexer.ConnectAsync(_options);

        try
        {
            return await redis.GetDatabase().StringGetAsync(key);
        }
        catch (RedisTimeoutException)
        {
            Console.WriteLine($"Redis timeout error");
            return RedisValue.Null;
        }
        catch (Exception ex) when (ex is RedisException)
        {
            Console.WriteLine($"Redis error: {ex.Message}");
            return RedisValue.Null;
        }
    }
#endregion    
    
#region Set
    public async Task SetSearchRequestAsync(
        SearchRequest request,
        List<Journey> journeys)
    {
        // Create key
        string key = RedisKeyBuilder.BuildSearchKey(request);
        
        // Open connection
        await using ConnectionMultiplexer redis = await ConnectionMultiplexer.ConnectAsync(_options);
        IDatabase db = redis.GetDatabase();

        try
        {
            // Create cache structure with schema version and timestamp
            var cacheObject = new JsonObject
            {
                ["schemaVersion"] = 2,
                ["cachedAtUtc"] = DateTimeOffset.UtcNow.ToString("O"),
                ["items"] = JsonSerializer.SerializeToNode(journeys)
            };

            string json = cacheObject.ToJsonString();
            await db.StringSetAsync(key, json, TimeSpan.FromSeconds(20));
        }
        catch (RedisTimeoutException)
        {
            Console.WriteLine($"Redis timeout error");
        }
        catch (Exception ex) when (ex is RedisException)
        {
            Console.WriteLine($"Redis error: {ex.Message}");
        }
    }
#endregion
}