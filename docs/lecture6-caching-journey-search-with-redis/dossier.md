# Lecture 6 – Redis: Caching Journey Search

MongoDB stays the search model from lecture 5. Redis is added as a disposable cache in front of it.

**Locations:** this dossier sits next to `lab.md` instead of `docs/evidence/lecture06/README.md`. The C# cache code is in `database/redis/lecture06/CacheLab/`. `search_once.js` is in `database/mongodb/lecture06/`, because only `./database/mongodb` is mounted into the mongo container.

## 1. Start Redis

### Setup

- Added `redis:7-alpine` to `compose.yaml`: port bound to `127.0.0.1` only, `./database/redis` mounted read-only at `/scripts`.
- Persistence is disabled (`--save ""`, `--appendonly no`), so a restart starts with an empty cache, which is fine because Redis isn't meant as a source of truth - values can always be rebuilt from MongoDB or PostgreSQL - losing the cache only costs extra misses, not a data loss.
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

`search_once.js` (`database/mongodb/lecture06/`) runs the search once with `START`/`END` from environment variables and prints one JSON array. The output is piped straight into Redis:

```powershell
docker compose exec -T -e START=2026-10-02T06:00:00Z -e END=2026-10-02T07:00:00Z mongo mongosh --quiet --file /scripts/lecture06/search_once.js | docker compose exec -T redis redis-cli -x SETEX search:LAB06:CPH:STOP-NORREPORT:STOP-AIRPORT 300
```

`-x` appends stdin as the last argument, which is why we use `SETEX key seconds value` here.

| Step | Source | Result (trip IDs) |
|---|---|---|
| Morning search, stored under the key | MongoDB → Redis | `["LAB05-T-OK"]` |
| Evening search (18:00–19:00) | MongoDB | `[]` |
| `GET search:LAB06:CPH:STOP-NORREPORT:STOP-AIRPORT` | Redis | `["LAB05-T-OK"]` (the morning result) |

**Why the evening search got the morning result:** the key contained only city, origin and destination, so the morning and evening searches built the same key. Redis matches on the key alone, so the evening search got the value already stored there: the morning result. The time window was only in the value, never in the key. Rule: everything that can change the result must be part of the key.

**Observations**

- `JSON.stringify` turns Decimal128 into `{"$numberDecimal":"36.00"}`, so the raw MongoDB output is not a clean cache format.
- The stored value was the full journey objects. The table shows only their trip IDs.
- `-x` also stores the trailing newline.

### Complete key

`CacheKeys.Search` (`database/redis/lecture06/CacheLab/CacheKeys.cs`) builds:

`search:LAB06:v2:<city>:<from>:<to>:<startUtc>:<endUtc>`

- **Every input that can change the result is in the key**: city, origin, destination, start and end.
- **IDs are case-sensitive**: they are not lower-cased, because `STOP-AIRPORT` and `stop-airport` are different IDs in the data.
- **IDs are escaped** with `Uri.EscapeDataString`, so a `:` inside an ID becomes `%3A` and cannot be mistaken for a separator.
- **Times are normalised** to UTC and formatted with `"O"` (ISO 8601 round-trip) and `InvariantCulture`. The same instant always gives the same text, regardless of offset or the machine's culture.
- **`v2` is a format version.** If the key format or the cached value changes, bumping the version means old entries are simply never read again and expire on their own, so no manual cleanup is needed.
- **Invalid input is rejected** before a key is built: blank IDs or `End <= Start` throw `ArgumentException`.

| Test | Expected | Keys | Result |
|---|---|---|---|
| Morning vs evening | Different | `...:2026-10-02T06:00:00.0000000Z:...` vs `...:2026-10-02T18:00:00.0000000Z:...` | PASS |
| Origin/destination swapped | Different | `...:STOP-NORREPORT:STOP-AIRPORT:...` vs `...:STOP-AIRPORT:STOP-NORREPORT:...` | PASS |
| `06:00Z` vs `08:00+02:00` | Same | both `...:2026-10-02T06:00:00.0000000Z:2026-10-02T07:00:00.0000000Z` | PASS |
| `A:B`/`C` vs `A`/`B:C` | Different | `...:CPH:A%3AB:C:...` vs `...:CPH:A:B%3AC:...` | PASS |
| Blank origin | Rejected | - | PASS |
| `End == Start` | Rejected | - | PASS |

**Why the offset is removed but the instant is kept:** `06:00Z` and `08:00+02:00` are the same moment and must return the same journeys, so they must share a key. The instant itself changes the result, so it must stay in the key.

**Takeaway:** my first version had the helpers but did not use them. `{q.Start}` used the da-DK culture and kept the offset, so equal searches got different keys, and the unescaped IDs could collide.

## 3. Cache the journey search

`CachedJourneySearch` (`database/redis/lecture06/CacheLab/CachedJourneySearch.cs`) wraps `MongoJourneySearch` using **cache-aside**: the application checks Redis first and only calls MongoDB on a miss. Redis knows nothing about MongoDB.

1. Build the key with `CacheKeys.Search` (this also validates the request).
2. `GET` the key. If Redis fails, print `cache unavailable` and continue to MongoDB.
3. Valid value → `hit`, return the items without calling MongoDB.
   Invalid value → `invalid`, treated as a miss. No value → `miss`.
4. Call MongoDB. A MongoDB failure is **not** caught: the search fails visibly instead of returning `[]`.
5. Store the whole result as one entry with `StringSetAsync(key, json, ttl)` (value and 20 s expiry in one command). A failed write is logged, and the MongoDB result is still returned.

Redis failures are caught as `RedisException` **or** `TimeoutException`. `RedisConnectionException` inherits from `RedisException`, but `RedisTimeoutException` inherits from `System.TimeoutException`, so catching only `RedisException` would let a slow Redis crash the search. The behaviour when Redis or MongoDB is down is verified in part 6.

Cached value:

`{"schemaVersion":2,"cachedAtUtc":"<now, UTC>","items":[...]}`

A value is valid only if it is readable JSON, `schemaVersion` is 2, `cachedAtUtc` is set, and `items` is an array without `null` elements. `[JsonRequired]` on `Journey` makes every field mandatory in each item. An empty result is cached too.

`RespectNullableAnnotations = true` on the serializer options also rejects `null` in non-nullable fields (e.g. `"cityId": null`). `[JsonRequired]` alone only checks that a field is present, not that it is non-null.

**Takeaway:** my first draft stored each journey separately in a loop. That is wrong: the key represents the whole search, so the value must be the whole result. With a loop, an empty result would never be cached.

## 4. Test the cached search

Morning key: `search:LAB06:v2:CPH:STOP-NORREPORT:STOP-AIRPORT:2026-10-02T06:00:00.0000000Z:2026-10-02T07:00:00.0000000Z`
Evening key: same with `18:00`–`19:00`.

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

After Test C the key had a new TTL of 20 s. After each Test D case the invalid value was replaced by a valid entry. The tests run automatically from `Program.cs`. The lab's `redis-cli` steps are done through StackExchange.Redis: `DEL` → `KeyDeleteAsync`, `EXPIRE key 1` → `KeyExpireAsync`, `SET key value EX 20` → `StringSetAsync`, and waiting for TTL -2 → polling `KeyExistsAsync`.

**Observation:** only the first request is noticeably slow (115 ms). That is startup cost (connections, JIT), not the query itself. Locally, with a tiny dataset, a hit and a miss take almost the same time. The measurable gain here is fewer MongoDB calls, not milliseconds.

## 5. Cancel a cached departure

### Cancellation in MongoDB
The cancellation is last week's update from `updates.js.example`, saved as `database/mongodb/lecture06/cancel.js`. It lives under `mongodb/` because only that folder is mounted as `/scripts` in the mongo container.

The update works in three parts:
- The filter (`_id: /^LAB05:/`, `"departures.tripId": "LAB05-T-OK"`) selects which documents to update: `LAB05:A` and `LAB05:C`.
- `"departures.$[trip].status": "Cancelled"` sets the status on every array element that matches `trip`.
- `arrayFilters: [{ "trip.tripId": "LAB05-T-OK" }]` defines what `trip` means. It is not a separate step, only the condition `$[trip]` refers to.

The positional `$` would only update the first matching element in each document. `$[trip]` updates all of them.

`LAB05:C` has a different `toStopId`, so it is not part of the morning search. It is still cancelled, because a cancellation applies to the trip, not to a search.

| Step | Result |
|---|---|
| `reset.js` | 6 fixture documents |
| Uncached search before | LAB05-T-OK |
| `cancel.js` 1st run | matched 2, modified 2 |
| `cancel.js` 2nd run | matched 2, modified 0 (idempotent) |
| Uncached search after | `[]` |

MongoDB is now correct, but Redis knows nothing about the change. The rest of this task shows what that means for a cached search.

### Cached search after the cancellation
Expiry temporarily set to 5 minutes with `dotnet run -- morning`, which runs one cached search and skips the tests. Each run is a new process, so `mongoCalls` starts at 0.

| Step | Search | Source | Trips | MongoDB calls |
|---|---|---|---|---|
| 3 | Cached, before cancellation | MongoDB (`miss`) | LAB05-T-OK | +1 |
| 5 | Uncached, after cancellation | MongoDB | `[]` | – |
| 6 | Cached, before expiry or deletion | Redis (`hit`) | **LAB05-T-OK** | +0 |
| 8 | Cached, after `DEL` | MongoDB (`miss`) | `[]` | +1 |

At step 6 the key had 296 s left (`TTL`). `DEL` returned 1 at step 7. Afterwards the expiry is back at 20 s (no-args run), and MongoDB is reset with `reset.js`.

Removing a cached value that should no longer be used is called **cache invalidation**.

### What the passenger saw
Before the deletion the passenger was shown LAB05-T-OK as a departure they could take, even though it was cancelled. Without the `DEL` that would have lasted up to the full expiry (here almost 5 minutes, normally 20 s). After the deletion the next search missed, read MongoDB and showed no departures, which is correct. The expiry is the upper limit for how long a stale result can live. Invalidation makes it shorter.

### If MongoDB had not yet received the cancellation
If the key is deleted *before* MongoDB has the cancellation, the next search misses, reads the old data from MongoDB and stores LAB05-T-OK in Redis again with a fresh expiry. The `DEL` did nothing, and the stale result lives for a full expiry. Invalidation only works when it happens *after* the source of truth has been updated.

### Race: an in-progress search writes an old result back (quiz 6)
1. Search S misses and reads MongoDB: LAB05-T-OK.
2. The cancellation is written to MongoDB, and the key is deleted.
3. S, which is still running, stores its old result with `SET ... EX`.

The key now holds the cancelled trip again, even though both the update and the `DEL` were done in the right order. Cache-aside has no link between "what I read" and "what I write". The race is not solved here. The expiry still limits how long the error lasts.

## 6. Stop Redis

Run with `dotnet run -- morning` (one cached search). Times are measured inside the program around the search.

| Test | Services down | Output | Time |
|---|---|---|---|
| A (default backlog) | Redis | `cache unavailable`, `cache write failed`, LAB05-T-OK from MongoDB | 11997 ms |
| A (`BacklogPolicy.FailFast`) | Redis | same | ~120 ms |
| B (no error handling) | Redis + MongoDB | unhandle selection 2 s), program crashed | ~12 s |
| B (with `search error`) | Redis + MongoDB | `search error after 2060 ms: TimeoutException ...` | 2060 ms |

### Redis unavailable
The search still works. Both Redis calls (GET and SET) fail and are caught, and the result comes from MongoDB. With the default settings it took 12 s, even though `AsyncTimeout` is 500 ms. While disconnected, StackExchange.Redis queues commands in a backlog and waits for the connection to come back. `BacklogPolicail at once, which brought the time down to ~120 ms. A timeout setting alone does not guarantee a fast fallback. For a cache, failing fast is the right trade-off, because MongoDB is the source of truth anyway.

### Redis and MongoDB unavailable
`CachedJourneySearch` only catches Redis errors. The MongoDB call is deliberately not wrapped, so its `TimeoutException` reaches `Program.cs`, which reports `search error`. Catchingturning `[]` would be a silent failure: the passenger would be told there are no departures, when in fact the search failed. Note that both drivers throw `TimeoutException`, so the Redis catch blocks must stay around the Redis calls only.