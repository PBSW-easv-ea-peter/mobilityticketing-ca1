# Lecture 7 – EF Core: The Route Overview

PostgreSQL (`lab07` in `mobility_l07`) is the only store. The live board is calculated from `routes` and `trips` on each request. Published plans are saved as copied values in their own tables.

**Locations:** design decision in [`design.md`](design.md) (diagrams in [`design-a.md`](design-a.md) and [`design-b.md`](design-b.md)), code in `src/MobilityTicketing.Lecture07/` (`Commands/`, `Queries/`, `Data/`), snapshot tables in `database/postgres/lecture07/03-read-model.sql`, measurements in `docs/evidence/lecture07/README.md`.

**Running it:** the lecture 7 services are behind the compose profile `l07`, so a plain `docker compose up -d` skips them.

```powershell
docker compose --profile l07 up -d postgres-l07
docker compose exec postgres-l07 psql -U mobility -d mobility_l07
docker compose --profile l07 run --rm --build tests
```

`postgres-l07` has no host port (`localhost:5432` is the other `postgres` service), so psql runs inside the container.

## 1. Choose the read models

We chose **Design A: calculate the live board on request**. The full decision (about 150 words), cancellation, published plans, transaction boundaries and `Include` vs projection are in [`design.md`](design.md).

### Working through the seed data

| Window for R1 | Trips in window | Count | Why |
|---|---|---|---|
| `[06:00, 07:00)` | T102, T103, T104 | 2 | T101 (05:59) is before the start, T104 is cancelled, T105 (07:00) is excluded by the open end |
| `[06:20, 07:00)` | T103, T104 | 1 | T104 is cancelled |
| `[06:00:00.000001, 07:00)` | T103, T104 | 1 | T102 is 1 µs before the start |

**Half-open intervals** (`[` inclusive, `)` exclusive) make adjacent windows count a trip exactly once: T105 at 07:00 belongs to `[07:00, 08:00)`, not to both.

**Why µs precision matters:** `scheduled_departure_utc` is a stored, planned time, so the result depends only on the window values, not on delays or network latency. PostgreSQL stores µs and .NET ticks are 100 ns. Any rounding between the layers can move a trip on the boundary in or out of the window.

### Why not a summary table (Design B)

- **Request space vs data space:** there are few departures, but the contract lets the client choose any window up to 24 hours with µs endpoints. A summary table can only hold fixed buckets (e.g. one row per route per hour).
- `[06:20, 07:00)` does not align with an hourly bucket. The bucket says 2, but not where in the hour the trips are, so B has to read `trips` anyway.
- Every cancellation and rename would also have to update the summary in the same transaction, which adds points of failure.
- The "precomputed" structure that answers any window is the index `trips_route_window (route_id, scheduled_departure_utc) INCLUDE (status)`: a sorted list of departures per route.

**Takeaway:** we first thought precision was a rounding issue. The real problem is the number of possible windows, which a table cannot enumerate.

### Cancellation and published plans

- A cancellation only sets `trips.status = 'Cancelled'`. Deleting would fail on the `bookings.trip_id` foreign key and lose history.
- A published plan copies the values (name, count, next departure). If it stored trip IDs and recalculated on read, a plan published at 05:00 would show 1 instead of 2 after T102 is cancelled at 05:30.
- `route_id` in the plan rows only identifies the route. It is not used to look anything up, so there is no foreign key to `routes`.

## 2. Implement the operations

Each method in `StudentApplication` calls its own command or query class.

| Method | Class | Lock key | Results |
|---|---|---|---|
| `ReadOverviewAsync` | `Queries/LiveRouteOverviewQuery` | – (read-only) | One row per route, ordered by ID |
| `CancelTripAsync` | `Commands/CancelTripCommand` | `trip:<id>` + `LockTripAsync` | `Cancelled`, `AlreadyCancelled`, `NotFound` |
| `RenameRouteAsync` | `Commands/RenameRouteCommand` | `route:<id>` | `Renamed`, `NotFound` (name trimmed, blank rejected) |
| `BookTripAsync` | `Commands/BookTripCommand` | `request:<id>` + `LockTripAsync` | `Booked`, `AlreadyBooked`, `Cancelled`, `NotFound`, `RequestConflict` |
| `PublishSnapshotAsync` | `Commands/PublishPlanCommand` | `plan:<id>` | `Published`, `AlreadyPublished`, `SnapshotConflictException` |
| `ReadSnapshotAsync` | `Queries/PublishedPlanQuery` | – (read-only) | `PublishedSnapshot`, `SnapshotNotFoundException` |

All writes go through `DatabaseSession.WriteAsync`: one transaction, and an advisory lock on the key so calls with the same key wait for each other. A failure anywhere rolls back the whole write.

### Live board

```csharp
db.Routes
    .OrderBy(r => r.Id)
    .Select(r => new RouteOverviewDto(
        r.Id, r.Name,
        r.Trips.Count(t => t.Status == "Scheduled" && t.DepartureUtc >= from && t.DepartureUtc < to),
        r.Trips.Where(/* same filter */).Min(t => (DateTime?)t.DepartureUtc)))
```

- The projection becomes one SQL query. No `Route` or `Trip` entities are loaded.
- Starting from `Routes` keeps routes without departures (R3, R4) as `0 / null`.
- `(DateTime?)` makes `Min` over an empty set return `null` instead of failing.
- `window.Validate()` runs before the context is used, so an invalid window never reaches the database.

### Booking

1. Look up `request_id` **before** the trip. Same trip → `AlreadyBooked` with the original receipt, also after a later cancellation. Other trip → `RequestConflict`.
2. `LockTripAsync` (`SELECT ... FOR UPDATE`), then require `Scheduled`.
3. Insert the booking with the time from `Clock`, truncated to whole µs.

**Why the row lock:** without it, a booking could read `Scheduled`, a concurrent cancellation could commit, and the booking would then insert a ticket for a cancelled trip. Cancellation locks the same row, so one waits for the other.

**Why truncate:** PostgreSQL keeps µs. Without truncation the first receipt would carry e.g. `.1234567`, while a retry reads `.123456` back, and the receipts would differ. `Commands/UtcTime.cs` is shared by booking and publication.

### Publication and snapshot storage

`03-read-model.sql` adds a header and detail rows:

| Table | Columns | Key |
|---|---|---|
| `published_plans` | `id`, `window_from_utc`, `window_to_utc`, `published_at_utc` | `id` |
| `published_plan_rows` | `plan_id`, `route_id`, `route_name`, `departure_count`, `next_departure_utc` (nullable) | `(plan_id, route_id)` |

1. An existing ID with the same window returns `AlreadyPublished`. Another window throws `SnapshotConflictException`. The windows compare exactly with `==`, because valid windows are whole µs, and that is what PostgreSQL returns.
2. The values come from `LiveRouteOverviewQuery`, in the same transaction, so the plan shows exactly what the live board would show at that moment.
3. Header and rows are inserted together. `Hooks.BeforePublishCommit` runs just before commit. If it throws, nothing is saved, so there is never a partial plan.

Reading a plan uses `AsNoTracking().Include(p => p.Rows.OrderBy(r => r.RouteId))`: one SQL query with a join, and only plan entities.

The tables are created by `reset` (`FixtureReset` runs `01`, `03`, `02`) and by the tests. The container's init does not run `03-read-model.sql`.

**Takeaway:** the first test run failed with `Property 'PublishedPlan.Rows' is not virtual`. The baseline measurement turns on lazy-loading proxies, and EF then requires every navigation property in the model to be `virtual`, including ours. This is the same reason `Route.Trips` is `virtual` in the starter.

## 3. Check the results

The supplied acceptance tests pass: **47 passed, 0 failed** (`docker compose --profile l07 run --rm --build tests`).

**Still open:**
- [ ] CLI scenario: `reset`, publish, cancel T102, rename R1 to "City Ring". Live should show City Ring / 1 / 06:20, and the earlier plan City Loop / 2 / 06:00. A new booking for T102 must be rejected, and a new plan must record the changed values.
- [ ] `book T201 campus-1` twice gives the same receipt and one booking. `morning` for 07:00–08:00 reports a conflict.
- [ ] Baseline vs live at 4 / 9 and 200 / 100,000: executed reads, entities and SQL in `docs/evidence/lecture07/README.md`.
- [ ] At least four tests of our own in `StudentTests.cs`, including a failure or concurrency case.
