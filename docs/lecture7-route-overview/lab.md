# Implementation lab: The route overview

The departure board needs current trip data. Dispatch also needs a saved copy of each published plan. Booking must check whether a trip can still be sold, even when the passenger is looking at an old screen.

| Read or operation | Required behavior |
|---|---|
| Live board | Show current route names, scheduled-trip counts and next departures. |
| Published plan | Keep the values shown when the plan was published. |
| Booking | Check the current trip status and handle repeated requests. |

Use the [Lecture 7 starter](https://github.com/patrickstolc/mobilityticketing-lecture-7-starter). It supplies Docker, the source tables, a lazy-loading example, logging and transaction helpers. The fixture uses 23 October 2026; the default window is 06:00 to 07:00 UTC.

## 1. Choose the read models

Sketch the tables and the command/query paths. Compare two designs, choose one and explain the choice in about 150 words. Show where booking rules, retries and transaction boundaries belong.

For example, compare calculating the live board on request with maintaining a current-summary table. What would a cancellation update? How would each design handle another time window and preserve an earlier published plan?

For the live query, consider eager loading with `Include` and projecting directly to `RouteOverviewDto`. Compare the columns and objects each would load, as well as the number of database calls.

## 2. Implement the operations

Complete the six methods in `StudentApplication` using your own command and query classes:

- Read the live board for a UTC window, including routes with no departures.
- Cancel a trip while keeping its row.
- Rename a route. The next live read must show the change.
- Book a scheduled trip and return the original receipt on retry.
- Publish a named plan in one transaction. Preserve it on retry and reject a conflicting window.
- Read a published plan in one database call.

Design the snapshot storage in `03-read-model.sql`. The [contract](https://github.com/patrickstolc/mobilityticketing-lecture-7-starter/blob/main/docs/contract.md) gives the signatures and failure cases. The [README](https://github.com/patrickstolc/mobilityticketing-lecture-7-starter#readme) has setup and run commands.

Use positive UTC windows of at most 24 hours, with endpoints that are exact whole microseconds. The supplied `TimeWindow.Validate()` rejects finer precision with `ArgumentException` before any database query or write. For example, `06:00:00.000001Z` is valid; `06:00:00.0000007Z` is not. Do not round or truncate the requested window.

Once booking is implemented, try:

```bash
docker compose run --rm app book T201 campus-1
docker compose run --rm app book T201 campus-1
```

The calls should return the same receipt and leave one booking. For publication, repeating `morning` for 06:00–07:00 preserves the original plan; using that ID for 07:00–08:00 must report a conflict.

## 3. Check the results

Start with `reset`. Publish a plan, cancel `T102` and rename R1 to "City Ring". The live row should become City Ring / 1 / 06:20 UTC. The earlier plan should still show City Loop / 2 / 06:00 UTC.

Try a new booking for T102 using a fresh request ID. It must be rejected. Publish a new plan and check that it records the changed values.

Compare the lazy baseline with your live query at 4 routes / 9 trips and 200 routes / 100,000 trips. Record executed reads, loaded entities and SQL. Keep the live query to one or two reads.

Pass the acceptance checks and add at least four tests of your own, including a failure or concurrency case. For example, publish two different plans concurrently and check that each retains its own window and rows.
