# Results

Measured 2026-10-28 with `docker compose --profile l07 run --rm app <command>`: `reset`, `baseline`, `overview`, then `scale`, `baseline`, `overview`. The full SQL logs are written to `logs/` (ignored by git), so the SQL is copied below.

| Data size | Read | Executed commands | Route entities | Trip entities | SQL log |
| --- | --- | ---: | ---: | ---: | --- |
| 4 routes / 9 trips | Baseline | 5 | 4 | 9 | [Baseline SQL](#baseline-sql) |
| 4 routes / 9 trips | Live board | 1 | 0 | 0 | [Live SQL](#live-board-sql) |
| 200 routes / 100,000 trips | Baseline | 201 | 200 | 100,000 | [Baseline SQL](#baseline-sql) |
| 200 routes / 100,000 trips | Live board | 1 | 0 | 0 | [Live SQL](#live-board-sql) |

The baseline grows with the data (1 + N reads, every trip loaded as an entity and filtered in C#). The live board stays at one read with no entities, because the database counts and returns one row per route.

### Baseline SQL

One query for the routes, then one lazy load per route (N+1):

```sql
SELECT r.id, r.short_name FROM lab07.routes AS r ORDER BY r.id

-- repeated once per route: R1, R2, R3, ...
SELECT t.id, t.scheduled_departure_utc, t.route_id, t.status
FROM lab07.trips AS t
WHERE t.route_id = @__p_0
```

### Live board SQL

```sql
SELECT r.id, r.short_name, (
    SELECT count(*)::int
    FROM lab07.trips AS t
    WHERE r.id = t.route_id AND t.status = 'Scheduled' AND t.scheduled_departure_utc >= @__from_0 AND t.scheduled_departure_utc < @__to_1), (
    SELECT min(t0.scheduled_departure_utc)
    FROM lab07.trips AS t0
    WHERE r.id = t0.route_id AND t0.status = 'Scheduled' AND t0.scheduled_departure_utc >= @__from_0 AND t0.scheduled_departure_utc < @__to_1)
FROM lab07.routes AS r
ORDER BY r.id
```

### Published plan SQL

One read, only plan tables (0 route and 0 trip entities):

```sql
SELECT t.id, t.published_at_utc, t.window_from_utc, t.window_to_utc, p0.plan_id, p0.route_id, p0.departure_count, p0.next_departure_utc, p0.route_name
FROM (
    SELECT p.id, p.published_at_utc, p.window_from_utc, p.window_to_utc
    FROM lab07.published_plans AS p
    WHERE p.id = @__planId_0
    LIMIT 2
) AS t
LEFT JOIN lab07.published_plan_rows AS p0 ON t.id = p0.plan_id
ORDER BY t.id, p0.route_id
```

## Cancellation, rename, booking and snapshot

Run after `reset`, window 23 October 2026 06:00–07:00 UTC.

| Step | Command | Output |
| --- | --- | --- |
| 1 | `publish morning` | `Published` |
| 2 | `cancel T102` | `Cancelled` |
| 3 | `rename R1 "City Ring"` | `Renamed` |
| 4 | `overview` | R1: **City Ring / 1 / 06:20** (R2 2 / 06:10, R3 and R4 0 / null) |
| 5 | `snapshot morning` | R1: **City Loop / 2 / 06:00** (unchanged) |
| 6 | `book T102 fresh-1` | `Cancelled`, no receipt (rejected) |
| 7 | `publish later-plan`, `snapshot later-plan` | R1: City Ring / 1 / 06:20 (new plan records the changed values) |
| 8 | `book T201 campus-1` (twice) | `Booked`, then `AlreadyBooked`, same receipt `2026-10-28T11:53:28.917678Z`; `lab07.bookings` has one row |
| 9 | `publish morning` (same window) | `AlreadyPublished` |
| 10 | `publish morning 2026-10-23T07:00:00Z 2026-10-23T08:00:00Z` | `Snapshot 'morning' belongs to another time window.` |

## Tests

The supplied acceptance tests in `tests/MobilityTicketing.Lecture07.Tests/` pass: 47 passed, 0 failed (`docker compose --profile l07 run --rm --build tests`). Our own tests in `StudentTests.cs` are still to be written.

The design sketch and explanation are in [`docs/lecture7-route-overview/design.md`](../../lecture7-route-overview/design.md), and the full write-up is in [`dossier.md`](../../lecture7-route-overview/dossier.md).
