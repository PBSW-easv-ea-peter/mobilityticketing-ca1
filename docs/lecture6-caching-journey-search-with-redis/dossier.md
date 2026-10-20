# Lecture 6 – Redis: Caching Journey Search

MongoDB stays the search model from lecture 5. Redis is added as a disposable cache in front of it.

**Locations:** this dossier (instead of `docs/evidence/lecture06/README.md`), C# code in `database/redis/lecture06/CacheLab/`, mongosh scripts in `database/mongodb/lecture06/` (only `./database/mongodb` is mounted into the mongo container).

## 1. Start Redis

### Setup

- Added `redis:7-alpine` to `compose.yaml`: port bound to `127.0.0.1` only, `./database/redis` mounted read-only at `/scripts`.
- Persistence is disabled (`--save ""`, `--appendonly no`). Redis is not a source of truth: values can always be rebuilt from MongoDB, so losing the cache only costs extra misses, not data.
- `docker compose exec -T redis redis-cli PING` replied `PONG`.

### Expiry

| Step | Command | Result |
|---|---|---|
| Before expiry | `GET` / `TTL stop:LAB06:STOP-NORREPORT` | `"Norreport"` / `20` |
| After expiry | `GET` / `TTL` | `(nil)` / `-2` |

`-2` means the key does not exist. `-1` would mean the key exists without an expiry.

### Uncached search still works

After `reset.js` (6 fixture documents), `lecture05/search.js` passes all 10 tests. CPH, Nørreport → Airport, 2 Oct 06:00–07:00 returns only `LAB05-T-OK`.

**Takeaway:** `mongosh` and `/scripts/...` only exist inside the container, so the commands must go through `docker compose exec`. In PowerShell, `\` is not a line continuation (use one line or a backtick).

## 2. Choose a cache key

### Incomplete key

`search_once.js` runs the search once with `START`/`END` from environment variables and prints a JSON array, which is piped into Redis (`-x` appends stdin as the last argument, hence `SETEX key seconds value`):

```powershell
docker compose exec -T -e START=2026-10-02T06:00:00Z -e END=2026-10-02T07:00:00Z mongo mongosh --quiet --file /scripts/lecture06/search_once.js | docker compose exec -T redis redis-cli -x SETEX search:LAB06:CPH:STOP-NORREPORT:STOP-AIRPORT 300
```

| Step | Source | Result (trip IDs) |
|---|---|---|
| Morning search, stored under the key | MongoDB → Redis | `["LAB05-T-OK"]` |
| Evening search (18:00–19:00) | MongoDB | `[]` |
| `GET search:LAB06:CPH:STOP-NORREPORT:STOP-AIRPORT` | Redis | `["LAB05-T-OK"]` (the morning result) |

**Why the evening search got the morning result:** the key contained only city, origin and destination, so both searches built the same key, and Redis matches on the key alone. The time window was only in the value, never in the key. Rule: everything that can change the result must be part of the key.

**Observations:** the raw output is not a clean cache format (`JSON.stringify` turns Decimal128 into `{"$numberDecimal":"36.00"}`, and `-x` also stores the trailing newline).

### Complete key

`CacheKeys.Search` (`CacheKeys.cs`) builds:

`search:LAB06:v2:<city>:<from>:<to>:<startUtc>:<endUtc>`

- **Every input that can change the result is in the key**: city, origin, destination, start and end.
- **IDs are case-sensitive**: not lower-cased, because `STOP-AIRPORT` and `stop-airport` are different IDs.
- **IDs are escaped** with `Uri.EscapeDataString`, so a `:` inside an ID becomes `%3A` and cannot be mistaken for a separator.
- **Times are normalised** to UTC and formatted with `"O"` and `InvariantCulture`, so the same instant always gives the same text, regardless of offset or culture.
- **`v2` is a format version.** Bumping it means old entries are never read again and expire on their own.
- **Invalid input is rejected**: blank IDs or `End <= Start` throw `ArgumentException`.

| Test | Expected | Result |
|---|---|---|
| Morning vs evening | Different | PASS |
| Origin/destination swapped | Different | PASS |
| `06:00Z` vs `08:00+02:00` | Same (`...T06:00:00.0000000Z:...T07:00:00.0000000Z`) | PASS |
| `A:B`/`C` vs `A`/`B:C` | Different (`A%3AB:C` vs `A:B%3AC`) | PASS |
| Blank origin | Rejected | PASS |
| `End == Start` | Rejected | PASS |

**Why the offset is removed but the instant is kept:** `06:00Z` and `08:00+02:00` are the same moment and must share a key. The instant itself changes the result, so it must stay in the key.

**Takeaway:** my first version had the helpers but did not use them. `{q.Start}` used the da-DK culture and kept the offset, so equal searches got different keys, and the unescaped IDs could collide.

## 3. Cache the journey search

`CachedJourneySearch` (`CachedJourneySearch.cs`) wraps `MongoJourneySearch` using **cache-aside**: the application checks Redis first and only calls MongoDB on a miss. Redis knows nothing about MongoDB.

1. Build the key with `CacheKeys.Search` (this also validates the request).
2. `GET` the key. If Redis fails, print `cache unavailable` and continue to MongoDB.
3. Valid value → `hit`, return the items without calling MongoDB. Invalid value → `invalid`, treated as a miss. No value → `miss`.
4. Call MongoDB. A MongoDB failure is **not** caught: the search fails visibly instead of returning `[]`.
5. Store the whole result with `StringSetAsync(key, json, ttl)` (value and 20 s expiry in one command). A failed write is logged, and the MongoDB result is still returned.

Redis failures are caught as `RedisException` **or** `TimeoutException`: `RedisTimeoutException` inherits from `System.TimeoutException`, not `RedisException`, so catching only `RedisException` would let a slow Redis crash the search.

Cached value: `{"schemaVersion":2,"cachedAtUtc":"<now, UTC>","items":[...]}`

A value is valid only if it is readable JSON, `schemaVersion` is 2, `cachedAtUtc` is set, and `items` is an array without `null` elements. `[JsonRequired]` on `Journey` makes every field mandatory, and `RespectNullableAnnotations = true` also rejects `null` in non-nullable fields (`[JsonRequired]` alone only checks presence). An empty result is cached too.

**Takeaway:** my first draft stored each journey separately in a loop. The key represents the whole search, so the value must be the whole result. With a loop, an empty result would never be cached.

## 4. Test the cached search

| Test | Step | Source | Trips | MongoDB calls | Time |
|---|---|---|---|---|---|
| A: repeat | 1st (key deleted) | MongoDB (`miss`) | LAB05-T-OK | +1 | 115 ms |
| | 2nd | Redis (`hit`) | LAB05-T-OK | +0 | 10 ms |
| B: empty result | 1st (key deleted) | MongoDB (`miss`) | `[]` | +1 | 4 ms |
| | 2nd | Redis (`hit`) | `[]` | +0 | 1 ms |
| C: expiry | after `EXPIRE 1` and TTL -2 | MongoDB (`miss`) | LAB05-T-OK | +1 | 4 ms |
| D: invalid value | `not-json` | MongoDB (`invalid`) | LAB05-T-OK | +1 | 6 ms |
| | wrong `schemaVersion` | MongoDB (`invalid`) | LAB05-T-OK | +1 | 4 ms |
| | missing `items` | MongoDB (`invalid`) | LAB05-T-OK | +1 | 4 ms |

After Test C the key had a new TTL of 20 s, and each invalid value in Test D was replaced by a valid entry. The tests run from `Program.cs`, using StackExchange.Redis instead of `redis-cli` (`KeyDeleteAsync`, `KeyExpireAsync`, `StringSetAsync`, polling `KeyExistsAsync`).

**Observation:** only the first request is noticeably slow (115 ms), which is startup cost (connections, JIT). Locally, with a tiny dataset, a hit and a miss take almost the same time. The measurable gain is fewer MongoDB calls, not milliseconds.

## 5. Cancel a cached departure

`cancel.js` is last week's update from `updates.js.example`. The filter (`_id: /^LAB05:/`, `"departures.tripId": "LAB05-T-OK"`) selects `LAB05:A` and `LAB05:C`. `"departures.$[trip].status": "Cancelled"` with `arrayFilters: [{ "trip.tripId": "LAB05-T-OK" }]` updates every matching array element, while the positional `$` would only update the first. `LAB05:C` is not in the morning search but is still cancelled, because a cancellation applies to the trip, not to a search.

Run with `dotnet run -- morning` (one cached search, expiry temporarily 5 minutes, tests skipped):

| Step | Action | Source | Result |
|---|---|---|---|
| 1–2 | `reset.js`, `DEL` morning key | – | 6 fixture documents |
| 3 | Cached search | MongoDB (`miss`, +1) | LAB05-T-OK |
| 4 | `cancel.js` | – | matched 2, modified 2 (a 2nd run: modified 0, idempotent) |
| 5 | Uncached search | MongoDB | `[]` |
| 6 | Cached search, TTL 296 s left | Redis (`hit`, +0) | **LAB05-T-OK** |
| 7 | `DEL` morning key | – | 1 |
| 8 | Cached search | MongoDB (`miss`, +1) | `[]` |

Afterwards the expiry is back at 20 s and MongoDB is reset. Removing a cached value that should no longer be used is called **cache invalidation**.

**What the passenger saw:** before the deletion, LAB05-T-OK was shown as a departure they could take, although it was cancelled. Without `DEL` that lasts up to the full expiry. After the deletion the next search missed, read MongoDB and correctly showed no departures. The expiry is the upper limit for how long a stale result lives. Invalidation makes it shorter.

**If MongoDB had not yet received the cancellation:** the next search after `DEL` would read the old data and store LAB05-T-OK again with a fresh expiry. The `DEL` would do nothing. Invalidation only works *after* the source of truth has been updated.

**Race (quiz 6):**
1. Search S misses and reads MongoDB: LAB05-T-OK.
2. The cancellation is written to MongoDB, and the key is deleted.
3. S, which is still running, stores its old result with `SET ... EX`.

The key holds the cancelled trip again, even though the update and the `DEL` were done in the right order. Cache-aside has no link between "what I read" and "what I write". The race is not solved here, but the expiry limits how long the error lasts.

## 6. Stop Redis

Run with `dotnet run -- morning`. Times are measured inside the program around the search.

| Test | Services down | Output | Time |
|---|---|---|---|
| A (default backlog) | Redis | `cache unavailable`, `cache write failed`, LAB05-T-OK from MongoDB | 11997 ms |
| A (`BacklogPolicy.FailFast`) | Redis | same | ~120 ms |
| B (no error handling) | Redis + MongoDB | unhandled `TimeoutException` (server selection 2 s), program crashed | ~12 s |
| B (with `search error`) | Redis + MongoDB | `search error after 2060 ms: TimeoutException ...` | 2060 ms |

**Redis unavailable:** the search still works. Both Redis calls (GET and SET) fail and are caught, and the result comes from MongoDB. With default settings it took 12 s, despite `AsyncTimeout = 500`, because StackExchange.Redis queues commands in a backlog while disconnected and waits for the connection to return. `BacklogPolicy.FailFast` makes commands fail at once (~120 ms). A timeout setting alone does not guarantee a fast fallback. For a cache, failing fast is the right trade-off, because MongoDB is the source of truth anyway.

**Redis and MongoDB unavailable:** `CachedJourneySearch` only catches Redis errors. The MongoDB call is deliberately not wrapped, so its `TimeoutException` reaches `Program.cs`, which reports `search error`. Catching it inside the search and returning `[]` would be a silent failure: the passenger would be told there are no departures, when in fact the search failed. Both drivers throw `TimeoutException`, so the Redis catch blocks must stay around the Redis calls only.

## 7. Send several searches at once

Run with `dotnet run -- concurrent`: deletes the morning key, adds a 200 ms delay before the MongoDB query (only in this mode) and sends ten identical searches with `Task.WhenAll`, twice.

| Batch | Output | MongoDB calls | Time |
|---|---|---|---|
| 1 (empty cache) | 10 × `miss` | +10 | 352 ms |
| 2 (within TTL) | 10 × `hit` | +0 | 12 ms |

The requests are not merged; they are ten independent searches running at the same time (352 ms, not 10 × 200 ms). All ten GETs happen before the first SET, about 200 ms later, so every request sees an empty cache and queries MongoDB (a *cache stampede*). Each then writes the same key, and the last write wins. That is harmless here because the values are identical, but on a popular key with a slow query it multiplies the database load exactly when the cache is empty. Not implemented: a per-key in-flight lock (e.g. `ConcurrentDictionary<string, Lazy<Task<...>>>`) would let one request query MongoDB while the others await its result.

## 8. Try a seat counter

| Step | Command | Result |
|---|---|---|
| Two trips, same route/date | `MGET ...TRIP-ONE ...TRIP-TWO` | `0`, `10` |
| Missing vs zero | `DEL` + `GET ...TRIP-TWO` / `GET ...TRIP-ONE` | `(nil)` / `"0"` |
| One seat left, script twice | `SET ...TRIP-ONE 1`, `--eval` ×2 | `0`, then `-1` (value stays 0) |
| Key deleted, script | `--eval` | `availability unknown` |

**Key per trip:** a route runs many trips per day, each with its own seats. One route/date key holds one number, so it cannot say "0 on one trip, 10 on another"; a sum would sell seats on a full trip. The key must contain `tripId`.

**Missing vs zero:** `0` is a known fact: the trip is sold out. A missing key means Redis does not know (expired, deleted or never loaded), so the answer must come from PostgreSQL. Treating missing as 0 would show available trips as sold out. The script keeps them apart: `-1` for sold out, an error for unknown.

**Counter vs purchase:** the Lua script runs atomically, so no other request can change the value between GET and DECR, and the count never goes below 0. But it only changed a number in Redis, which here has no persistence. No seat was reserved in PostgreSQL, no ticket created and no payment taken. A purchase needs one PostgreSQL transaction. The Redis counter is only a fast hint that can drift and must be rebuilt from PostgreSQL.

## 9. Decide what belongs in Redis

SET/GET/TTL/DEL worked for `route:LAB06:CPH:LINE-M2` and `stop:LAB06:CPH:STOP-NORREPORT` (TTL 86399 right after SET). Note: Windows PowerShell 5.1 strips `"` from arguments to native programs, so the first attempt stored `{routeId:LINE-M2,...}`, which is not JSON. Every quote must be written as `\"`. A cache reading such a value would treat it as invalid.

| Data | Example key → value | Source | Expiry | Updated / deleted by | If Redis has no usable copy |
|---|---|---|---|---|---|
| Search results | `search:LAB06:v2:CPH:STOP-NORREPORT:STOP-AIRPORT:<start>:<end>` → `{"schemaVersion":2,"cachedAtUtc":…,"items":[…]}` | MongoDB `journey_search` | 2 min | Written by the search on a miss; deleted when a departure is cancelled (section 5) | MongoDB |
| Trip availability | `availability:LAB06:<tripId>` → `10` | PostgreSQL (trips/tickets) | Until departure | Decremented by the purchase flow after the PostgreSQL commit; deleted on cancellation | PostgreSQL. Never assume 0 |
| Route information | `route:LAB06:CPH:LINE-M2` → `{"routeId":"LINE-M2","name":"M2",…}` | PostgreSQL `routes` | 24 h | Deleted by the admin/timetable import when the route changes | PostgreSQL |
| Stop information | `stop:LAB06:CPH:STOP-NORREPORT` → `{"stopId":"STOP-NORREPORT","name":"Nørreport"}` | PostgreSQL `stops` | 24 h | Deleted by the admin/timetable import when the stop changes | PostgreSQL |

**Expiry:** 24 h is fine for routes and stops; they rarely change and are deleted explicitly when they do. 30 minutes is too long for search results: cancellations are deleted explicitly, but `availableSeats` is part of the result and changes with every purchase without deleting the key. I would try about 2 minutes, so a burst of identical searches is still served from Redis. The result can still be out of date within those 2 minutes (seats sold, delays), so the purchase must always check PostgreSQL. 8 h for a route/day seat count is wrong twice: the key must be per trip (section 8), and a count that old is useless.

**Two passengers a minute apart** share an entry only if they send exactly the same city, stops, start and end, since the key holds no passenger data. If the app searches "from now", the start differs by a minute, the keys differ and nothing is shared. Rounding the start down (e.g. to 5 minutes) and filtering departed trips after reading the cache would fix that.

