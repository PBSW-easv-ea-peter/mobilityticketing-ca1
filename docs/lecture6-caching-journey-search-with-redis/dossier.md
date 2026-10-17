# Lecture 6 – Redis: Caching Journey Search

MongoDB stays the search model from lecture 5. Redis is added as a disposable cache in front of it.

## 1. Start Redis

### Setup

- Added `redis:7-alpine` to `compose.yaml`: port bound to `127.0.0.1` only, `./database/redis` mounted read-only at `/scripts`.
- Persistence is disabled (`--save ""`, `--appendonly no`), so a restart starts with an empty cache, which is fine because Redis isn't meant as a source of truth - values can always be rebuilt from MongoDB or PostgreSQL - losing the cache only costs extra misses, not a data loss.

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

| Step | Source | Result |
|---|---|---|
| Morning search, stored under the key | MongoDB → Redis | `["LAB05-T-OK"]` |
| Evening search (18:00–19:00) | MongoDB | `[]` |
| `GET search:LAB06:CPH:STOP-NORREPORT:STOP-AIRPORT` | Redis | `["LAB05-T-OK"]` (the morning result) |

**Why the evening search got the morning result:** the key contained only city, origin and destination, so the morning and evening searches built the same key. Redis matches on the key alone, so the evening search got the value already stored there: the morning result. The time window was only in the value, never in the key. Rule: everything that can change the result must be part of the key.

**Observations**

- `JSON.stringify` turns Decimal128 into `{"$numberDecimal":"36.00"}`, so the raw MongoDB output is not a clean cache format.
- `-x` also stores the trailing newline.

### Complete key

`CacheKeys.Search` (`database/redis/lecture06/CacheLab/CacheKeys.cs`) builds:

`search:LAB06:v2:<city>:<from>:<to>:<startUtc>:<endUtc>`

- **Every input that can change the result is in the key**: city, origin, destination, start and end.
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
