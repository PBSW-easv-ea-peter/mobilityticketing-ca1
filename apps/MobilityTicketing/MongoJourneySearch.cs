using System.Globalization;
using System.Collections.Concurrent;
using MobilityTicketing.Models;
using MongoDB.Bson;
using MongoDB.Driver;

namespace MobilityTicketing;

public sealed class MongoJourneySearch
{
    private readonly IMongoCollection<BsonDocument> _collection;
    private int _calls;
    public int Calls => Volatile.Read(ref _calls);
    public int DelayMilliseconds { get; set; } // Only for section 7.
    
    // Redis Cache Service
    CacheJourneySearch _cache;
 
    // Cache stampede prevention by grouping requests
    private readonly ConcurrentDictionary<string, SemaphoreSlim>
        _requestLocks = new();
    
    public MongoJourneySearch()
    {
        MongoClientSettings settings = MongoClientSettings.FromConnectionString("mongodb://127.0.0.1:27017");
        settings.ServerSelectionTimeout = TimeSpan.FromSeconds(2);
        settings.ConnectTimeout = TimeSpan.FromSeconds(1);
        _collection = new MongoClient(settings).GetDatabase("mobility")
            .GetCollection<BsonDocument>("journey_search");
        
        _cache = new CacheJourneySearch();
    }
    
    
    public async Task<List<Journey>> Fetch(SearchRequest q)
    {
        // Check cache
        (List<Journey> journeys, bool isCached) =
            await _cache.TryGetSearchRequestAsync(q);

        if (isCached)
        {
            Console.WriteLine("hit");
            return journeys;
        }

        Console.WriteLine("miss");

        string key = RedisKeyBuilder.BuildSearchKey(q);
        
        // Avoid stampede by grouping requests
        SemaphoreSlim gate = _requestLocks.GetOrAdd(
            key, _ => new SemaphoreSlim(1, 1));

        await gate.WaitAsync();

        try
        {
            // Try cache again since another request might have populated it while this request was waiting.
            (journeys, isCached) =
                await _cache.TryGetSearchRequestAsync(q);

            if (isCached)
            {
                Console.WriteLine("hit after waiting");
                return journeys;
            }

            // Else go to MongoDB
            if (DelayMilliseconds > 0)
                await Task.Delay(DelayMilliseconds);

            // Add 1 to MongoDb count
            Interlocked.Increment(ref _calls);

            journeys = await QueryMongoAsync(q);
            
            // Cache the Journeys
            await _cache.SetSearchRequestAsync(q, journeys);

            return journeys;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<List<Journey>> QueryMongoAsync(SearchRequest q)
    {

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
        List<BsonDocument> documents = await _collection.Aggregate<BsonDocument>(pipeline).ToListAsync();
        List<Journey> journeys = 
        [
            .. documents.Select(d =>
            {
                var t = d["departures"].AsBsonDocument;
                return new Journey(d["cityId"].AsString, d["routeId"].AsString,
                    d["fromStopId"].AsString, d["toStopId"].AsString, t["tripId"].AsString,
                    t["departureUtc"].AsBsonDateTime.ToUniversalTime(),
                    t["arrivalUtc"].AsBsonDateTime.ToUniversalTime(),
                    t["availableSeats"].ToInt32(),
                    decimal.Parse(d["price"].ToString()!, CultureInfo.InvariantCulture),
                    d["currency"].AsString);
            })
        ];
        
        return journeys;
    }
}
