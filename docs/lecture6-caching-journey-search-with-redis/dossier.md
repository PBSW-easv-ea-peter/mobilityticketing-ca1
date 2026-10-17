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
