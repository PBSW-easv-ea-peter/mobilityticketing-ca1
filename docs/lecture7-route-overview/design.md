# Design decision

We choose Design A: the live board is computed on each request. The contract allows arbitrary windows of up to 24 hours with µs precision, so a summary table (Design B) can only precompute fixed buckets.
A window such as `[06:20, 07:00)` does not align with the buckets, so B has to read `trips` anyway.
At the same time, every cancellation and route rename must update the summary table in the same transaction, which adds more points of failure.
Design A uses one projected query over the index `(route_id, scheduled_departure_utc) INCLUDE (status)`, so only the relevant departures are read.

A cancellation only sets `trips.status = 'Cancelled'`; the row is kept so bookings and history stay valid, and the live board reflects it on the next read. A published plan stores a header (`plan_id`, window, `published_at_utc`) and one row per route with *copied* values (name, departure count, next departure), so later cancellations or renames do not change it.

**Transaction boundaries.** Booking runs in one `WriteAsync` keyed by `request_id`: an existing request returns the original receipt, otherwise `LockTripAsync` locks the trip row (`FOR UPDATE`), the status must be `Scheduled`, and the booking is inserted. The lock stops a concurrent cancellation from slipping in. Publication runs in one `WriteAsync` keyed by `plan_id`: an existing ID with the same window returns `AlreadyPublished`, a different window throws `SnapshotConflictException`; otherwise the live values are computed and header and rows are inserted together, so a failure leaves no partial plan.

**`Include` vs projection.** At 200 routes / 100,000 trips, `Include` loads ~100,200 tracked entities with all columns in one call; the lazy baseline needs 1 + N calls. Projecting to `RouteOverviewDto` lets the database count and returns one row per route (200) in one call.

## Designs

- [Design A – live-tavlen beregnes, når nogen spørger](design-a.md)
- [Design B – live-tavlen læses fra en summary-tabel](design-b.md)
