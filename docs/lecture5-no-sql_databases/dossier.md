# Lecture 5 – MongoDB: Journey Search

PostgreSQL remains the source of truth. MongoDB holds a separate, derived model (`mobility.journey_search`) built only for searching direct departures.

## 1. Start MongoDB

### Setup

- Added `mongo:8.0` to `compose.yaml`: port bound to `127.0.0.1` only, named volume `mobility-mongo`, and `./database/mongodb` mounted read-only at `/scripts`.
- `setup.js` loaded 6 fixture documents into `mobility.journey_search`.

### Schema validation

The collection has a `$jsonSchema` validator with `validationAction: 'error'`, so invalid documents are rejected, not just logged.

**Test** (`validation.js`): copy `LAB05:A`, change exactly one field, try `insertOne`, catch the error.

| Test | Change | Result | Rule broken |
|---|---|---|---|
| 1 | `departures[0].departureUtc = "2026-10-02T06:20:00Z"` | code 121 | `bsonType: 'date'` (got `string`) |
| 2 | `departures[0].availableSeats = NumberInt(-1)` | code 121 | `minimum: 0` |

**Takeaways**

- The validator checks the BSON type, not the content. A string that looks like a date is still a string.
- `NumberInt` matters. A plain `-1` is stored as a `double` and would fail on type instead of on `minimum`, so the test would fail for the wrong reason.
- Each array element is validated on its own (`itemIndex` in `errInfo`).
- `findOne` returns `null` when nothing matches. It does not throw, so check the result before using it.

## 2. Journey search

**Approach** (`search.js`): an aggregation pipeline in five steps.

1. `$match` on `cityId`, `fromStopId`, `toStopId`, which removes whole documents early.
2. `$unwind: "$departures"`, giving one row per departure, as the lab requires.
3. `$match` on `status: "Scheduled"` and `departureUtc` with `$gte: start, $lt: end`. After the unwind, both conditions always apply to the same departure.
4. `$project` lifts the departure fields to the top level.
5. `$sort` by `departureUtc`.

```js
[
  { $match: { cityId, fromStopId, toStopId } },
  { $unwind: "$departures" },
  { $match: {
      "departures.status": "Scheduled",
      "departures.departureUtc": { $gte: start, $lt: end },
  } },
  { $project: {
      _id: 0, cityId: 1, routeId: 1, fromStopId: 1, toStopId: 1, price: 1, currency: 1,
      tripId: "$departures.tripId",
      departureUtc: "$departures.departureUtc",
      arrivalUtc: "$departures.arrivalUtc",
      availableSeats: "$departures.availableSeats",
  } },
  { $sort: { departureUtc: 1 } },
]
```

The interval is half-open (start inclusive, end exclusive), so `LAB05-T-EDGE` at 07:00 is excluded. Invalid input (missing or empty IDs, `end <= start`) throws an error.

**Results** (full output in `search-results.txt`)

| Search | Expected | Result |
|---|---|---|
| Nørreport to Airport, 06:00–07:00 | LAB05-T-OK | PASS |
| End at 06:20 | [] | PASS |
| Start at 06:20 | LAB05-T-OK | PASS |
| Airport to Nørreport | LAB05-T-E | PASS |
| Same search on 3 October | LAB05-T-F | PASS |
| Destination STOP-NO-MATCH | [] | PASS |
| `end <= start` | error | PASS |
| Empty stop ID | error | PASS |

**Takeaways**

- In `$project`, a missing `$` silently turns a field path into a string constant. We got `departureUtc: 'departures.departureUtc'` with no error.
- A missing `$sort` was not caught by the tests, because every case returns at most one result. Tests only cover what they are designed to see.
- The fixtures are built so that each departure tests one rule: `T-EARLY` (before start), `T-CANCEL` (status), `T-EDGE` (exclusive end), plus documents with another city, destination, direction or date.

## 3. A query that looks right, but isn't

Script: `search_bug.js`, which prints the buggy and the fixed query.

**Problem.** The query used two separate conditions, `"departures.status": "Scheduled"` and `"departures.departureUtc": { $gte, $lt }`. On an array, each condition only needs *some* element to match, not the same one. Document B matched even though none of its departures is valid:

| Departure | Time | Status | Satisfies |
|---|---|---|---|
| `LAB05-T-B1` | 05:55 | Scheduled | status condition |
| `LAB05-T-B2` | 06:15 | Cancelled | time condition |

**Fix.** Wrapping both conditions in `departures: { $elemMatch: { ... } }` forces them to apply to the same departure. Now only `LAB05:A` matches.

```js
// Buggy: each condition can match a different departure
"departures.status": "Scheduled",
"departures.departureUtc": { $gte: start, $lt: end },

// Fixed: both conditions must match the same departure
departures: {
  $elemMatch: {
    status: "Scheduled",
    departureUtc: { $gte: start, $lt: end },
  },
},
```

**Takeaway.** `$elemMatch` selects documents, but it does not trim the array. `LAB05:A` is still returned with all four departures. That is why `search.js` uses `$unwind` + `$match`, which filters at departure level.

## 4. Updating duplicated data

`LAB05-T-OK` is stored in both `LAB05:A` (to Airport) and `LAB05:C` (to Central). Each step was run from a fresh `reset.js` in `updates.js`, which now holds the last step (price change).

Updates on array elements use `departures.$[trip]` with `arrayFilters: [{ "trip.tripId": "LAB05-T-OK" }]`. The query filter selects documents, and `arrayFilters` selects elements, so the other departures are left untouched.

| Step | Result |
|---|---|
| Cancel every copy | matched 2, modified 2. Both T-OK searches return `[]`. |
| Run it again | matched 2, modified 0. The filter still matches on `tripId`, but the value is already `Cancelled`. The update is idempotent. |
| Cancel only in A | matched 1, modified 1. Airport returns `[]`, but Central still returns T-OK. The searches disagree. |
| Repair C | matched 1, modified 1. Both searches agree again. |
| Move to 07:05–07:25 | matched 2, modified 2. 06:00–07:00 returns `[]`, and 07:00–08:00 returns `[T-EDGE, T-OK]`, the first real test of `$sort`. |
| Price 36.00 → 40.00 for CPH / LINE-M2 | matched 4, modified 4. A, C, E and F changed, B and D kept 36.00. |

The updates below show filter and update, plus `arrayFilters` where it is used. "Cancel only in A" and "Repair C" are the cancel update with `_id: "LAB05:A"` or `"LAB05:C"` as the filter.

```js
// Cancel every copy
updateMany(
  { _id: /^LAB05:/, "departures.tripId": "LAB05-T-OK" },
  { $set: { "departures.$[trip].status": "Cancelled" } },
  { arrayFilters: [{ "trip.tripId": "LAB05-T-OK" }] },
)

// Move every copy to 07:05-07:25 (same filter and arrayFilters)
{ $set: {
    "departures.$[trip].departureUtc": ISODate("2026-10-02T07:05:00Z"),
    "departures.$[trip].arrivalUtc": ISODate("2026-10-02T07:25:00Z"),
} }

// Change the price (document-level field, no arrayFilters)
updateMany(
  { _id: /^LAB05:/, cityId: "CPH", routeId: "LINE-M2" },
  { $set: { price: NumberDecimal("40.00") } },
)
```

**Takeaways**

- Duplication makes reads simple, but every change must reach every copy. Nothing in MongoDB enforces that. Both documents stay valid on their own, so a missed copy gives silent inconsistency.
- Prefer updates that target *all* copies by `tripId` over updates that target known documents. They are idempotent, so they are safe to re-run.
- `matchedCount` vs `modifiedCount` shows how many copies were already up to date. An update that matches 0 is not an error, so check the counts.
- D has `routeId: LINE-M2` but another `cityId`. A route ID is only unique within a city, so the filter needs both fields.
- Price uses `NumberDecimal("40.00")` (string argument) to satisfy the `decimal` validator without float rounding. Changing the price here only affects the search model. Tickets in PostgreSQL are unchanged.
- The tests in `search.js` describe the clean fixture state, so intended data changes show up as FAIL.

## 5. How large can the document get?

_TODO_

## 6. One departure per document

_TODO_
