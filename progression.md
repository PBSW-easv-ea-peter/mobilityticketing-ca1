# Progression Documentation - Mobility Ticketing CA1

## Latest Changes (Lecture 06 - Week 41)

### 01: Added Redis to compose

#### New File: search_mongo.js

A MongoDB aggregation script for searching journeys with the following search:
- Includes test case for Nørreport to Airport route (06:00 to 07:00)

#### Purpose:

This commit introduces Redis caching infrastructure to the mobility ticketing system

### 02: Added Redis Key Generation to MobilityTicketing App

#### New File: ./apps/MobilityTicketing/RedisKeyBuilder.cs

Added a static `RedisKeyBuilder` class with methods for generating consistent Redis keys:
  - Generates keys in format: `search:LAB06:v2:{cityId}:{fromStopId}:{toStopId}:{start}:{end}`

#### Purpose:

This addition provides a standardized way to generate Redis keys for caching journey search results, ensuring consistent key formatting and proper parameter validation for the caching layer.
This is to avoid getting false cache hits, from the original key were the 'start' and 'end' was not included. This could lead to getting journeys before/after the search.

### 03: Updated Caching to Use Structured JSON with 20s Expiry

#### Modified Files:
- `./apps/MobilityTicketing/CacheJourneySearch.cs`
- `./apps/MobilityTicketing/MongoJourneySearch.cs`

#### Purpose:

Implements a standardized cache structure with versioning and timestamps for better cache management, with a short 20-second expiry to ensure data freshness while still providing caching benefits.
MongoJourneySearch uses CacheJourneySearch to cache request results.

---

### 04: Test the cache search

#### Modified files:
- `./apps/MobilityTicketing/Program.cs`
- `./apps/MobilityTicketing/CacheJourneySearch.cs`

#### Purpose:

Test resistence to Redis being unavailable, cache misses, hits, and invalid cache data. Aswell as counting calls to Mongo.

### 09: Decide what belongs in Redis

#### Expiry Time Analysis

| Data | Proposed Expiry | Recommended Expiry | Rationale |
|------|----------------|-------------------|-----------|
| Search results | 30 minutes | **5 minutes** | Journey availability changes frequently; 30 minutes may serve stale data |
| Route/day seat counts | 8 hours | **2-5 minutes** | Seat availability is highly dynamic; needs frequent refresh |
| Route information | 24 hours | **24 hours** | Route data is relatively static; changes infrequently (unless a stop is skipped, but then it's easier to write-through when the stop is updated.) |
| Stop information | 24 hours | **24 hours** | Stop data is static; rarely changes |

#### Redis Storage Examples

| Data Type | Example Key | Example Value | Source DB | Chosen Expiry |
|-----------|-------------|---------------|-----------|---------------|
| Search results | `search:LAB06:v2:CPH:STOP-NORREPORT:STOP-AIRPORT:2026-10-02T06:00:00Z:2026-10-02T07:00:00Z` | `{"schemaVersion":2,"cachedAtUtc":"2026-10-09T08:00:00Z","items":[...]}` | MongoDB | 5 minutes |
| Trip availability | `availability:CPH:ROUTE-M1:2026-10-02` | `{"availableSeats":42,"lastUpdated":"2026-10-09T08:00:00Z"}` | MongoDB | 2 minutes |
| Route information | `route:CPH:ROUTE-M1` | `{"routeId":"ROUTE-M1","name":"Metro M1","stops":[...]}` | PostgreSQL | 24 hours |
| Stop information | `stop:CPH:STOP-NORREPORT` | `{"stopId":"STOP-NORREPORT","name":"Nørreport","location":[...]}` | PostgreSQL | 24 hours |

#### Update and Fetch Strategy

- **Search results**: Updated by passenger search requests. Deleted on expiry. Fetched from MongoDB if cache miss.
- **Trip availability**: Updated when seats are booked/released. Deleted on expiry. Fetched from MongoDB if cache miss.
- **Route information**: Updated on route schedule changes. Deleted on expiry. Fetched from PostgreSQL if cache miss.
- **Stop information**: Updated on stop data changes. Deleted on expiry. Fetched from PostgreSQL if cache miss.

#### Practical Expiry for Journey Search

For production use outside the lab, I would try **5 minutes** for journey search caching. This balances:
- **Performance**: Reduces MongoDB load significantly
- **Freshness**: Most journey data doesn't change every second
- **User experience**: Passengers get reasonably current information

However, results could still be out of date due to:
- **Real-time changes**: Last-minute schedule adjustments or cancellations
- **Seat availability**: Dynamic changes as bookings are made/released
- **Delays**: Live departure/arrival time updates
- **New journeys**: Recently added trips not yet in cache

#### Cache Sharing Between Passengers

**Yes, two passengers searching a minute apart will share the same cache entry.**

This happens because:
1. Both passengers use the same search parameters (city, fromStop, toStop, time range)
2. The cache key is generated from these exact parameters: `search:LAB06:v2:{cityId}:{fromStopId}:{toStopId}:{start}:{end}`
3. If the time range is the same (e.g., both searching 06:00-07:00), they generate the identical cache key
4. The second passenger's request will hit the cache and return the same cached results

This is the intended behavior of caching - reducing redundant database queries for identical search parameters.
