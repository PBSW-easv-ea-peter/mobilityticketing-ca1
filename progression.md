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
