# Lecture 05: MongoDB Schema Validation

## 01: Verify Schema Validator and Test Invalid Inserts

### Schema Validator
The `journey_search` collection in the `mobility` database has a MongoDB schema validator defined in [`setup.js`](./lecture05/setup.js). The validator enforces the following rules:

- **Required Fields**: `_id`, `cityId`, `routeId`, `fromStopId`, `toStopId`, `serviceDate`, `schemaVersion`, `price`, `currency`, `departures`.
- **Field Types**:
  - `_id`, `cityId`, `routeId`, `fromStopId`, `toStopId`, `serviceDate`: `string`.
  - `schemaVersion`: `int` (minimum: 1).
  - `price`: `decimal`.
  - `currency`: Must be `DKK` (enum).
  - `departures`: Array of objects with:
    - `tripId`: `string`.
    - `departureUtc`: `date`.
    - `arrivalUtc`: `date`.
    - `availableSeats`: `int` (minimum: 0).
    - `status`: `Scheduled` or `Cancelled` (enum).

### Validation Tests
The [`validation.js`](./lecture05/validation.js) script tests the validator by attempting two invalid inserts:

1. **Test 1: Invalid `departureUtc` Type**
   - Takes a valid document (`LAB05:A`).
   - Replaces `departureUtc` with a string (`"2026-10-02T06:20:00Z"`).
   - Expected: **Validation Error 121** (schema validation failure).

2. **Test 2: Negative `availableSeats`**
   - Takes a valid document (`LAB05:A`).
   - Sets `availableSeats` to `NumberInt(-1)`.
   - Expected: **Validation Error 121** (schema validation failure).

### Expected Output
Both inserts should fail with:
- **Error Code**: `121` (schema validation error).
- **Error Message**: Indicates violation of the schema rules.

### Verification
- The schema validator is correctly defined in `setup.js`.
- The `validation.js` script tests the validator with the required invalid inserts.
- Both tests are expected to fail with validation error `121`.

### Status
✅ **Completed**: Schema validator verified and invalid inserts tested.

---

## 02: Implement Journey Search

### Objective
Create a `search.js` script that searches the `journey_search` collection for matching departures based on:
- `cityId`
- `fromStopId`
- `toStopId`
- `start` (inclusive)
- `end` (exclusive)

### Implementation
The [`search.js`](./lecture05/search.js) script uses MongoDB aggregation to:
1. Filter documents by `cityId`, `fromStopId`, and `toStopId`.
2. Unwind the `departures` array to process each departure individually.
3. Filter for `Scheduled` departures within the `[start, end)` time range.
4. Sort results by `departureUtc`.
5. Project the output to match the required format:
   ```json
   {
     "cityId": "CPH",
     "routeId": "LINE-M2",
     "fromStopId": "STOP-NORREPORT",
     "toStopId": "STOP-AIRPORT",
     "tripId": "LAB05-T-OK",
     "departureUtc": ISODate("2026-10-02T06:20:00Z"),
     "arrivalUtc": ISODate("2026-10-02T06:40:00Z"),
     "availableSeats": 10,
     "price": NumberDecimal("36.00"),
     "currency": "DKK"
   }
   ```

### Input Validation
The script rejects invalid input:
- If `end <= start`, return `[]`.
- If `cityId`, `fromStopId`, or `toStopId` is empty, return `[]`.

### Test Cases
The script includes the following test cases:

| Search | Expected |
|--------|----------|
| Nørreport to Airport, 06:00 to 07:00 | `LAB05-T-OK` |
| End at 06:20 | `[]` |
| Start at 06:20 | `LAB05-T-OK` |
| Airport to Nørreport | `LAB05-T-E` |
| Same search on 3 October | `LAB05-T-F` |
| Destination STOP-NO-MATCH | `[]` |
| Invalid input (`end <= start`) | `[]` |
| Invalid input (empty stop ID) | `[]` |

### Status
✅ **Completed**: Journey search implemented and tested.

---

## 03: A Query That Looks Right, But Isn't

### Objective
Reproduce and fix a common MongoDB array query bug where separate conditions on the same array can lead to incorrect matches.

### Problem Reproduction
The [`search_bug.js`](./lecture05/search_bug.js) script demonstrates the issue:

#### Broken Query
```javascript
const broken_query = {
  cityId: "CPH",
  fromStopId: "STOP-NORREPORT",
  toStopId: "STOP-AIRPORT",
  "departures.status": "Scheduled",
  "departures.departureUtc": { $gte: start, $lt: end },
};
```

**Why It Fails**:
- The query applies **separate conditions** to the `departures` array.
- MongoDB matches the document if **any** departure satisfies the `status` condition **AND any (possibly different)** departure satisfies the `time` condition.
- **Document B (`LAB05:B`)** incorrectly matches because:
  - `LAB05-T-B1` has `status: "Scheduled"` (satisfies the status condition).
  - `LAB05-T-B2` has `departureUtc: 2026-10-02T06:15:00Z` (satisfies the time condition).
- Thus, the document matches even though **no single departure** satisfies both conditions.

#### Fixed Query
```javascript
const fixed_query = {
  cityId: "CPH",
  fromStopId: "STOP-NORREPORT",
  toStopId: "STOP-AIRPORT",
  departures: {
    $elemMatch: {
      status: "Scheduled",
      departureUtc: { $gte: start, $lt: end },
    },
  },
};
```

**Why It Works**:
- `$elemMatch` ensures that **both conditions** (`status` and `departureUtc`) apply to the **same departure** in the array.
- Document B no longer matches because neither `LAB05-T-B1` nor `LAB05-T-B2` satisfies both conditions simultaneously.

### Key Takeaway
When querying arrays in MongoDB, **separate conditions on the same array** can lead to unexpected matches. Use `$elemMatch` to ensure all conditions apply to the **same array element**.

### Status
✅ **Completed**: Bug reproduced, fixed, and documented.

---

## 04: Updating Duplicated Data

### Objective
Demonstrate the trade-off of duplicated data: while it simplifies reads, it complicates updates because changes must be applied to all copies.

### Steps
1. **Cancel the Trip**:
   - Use `updateMany` with `arrayFilters` to update every occurrence of `LAB05-T-OK` to `Cancelled`.
   - Verify that `LAB05-T-OK` no longer appears in the journey search.

2. **Run the Update Again**:
   - Re-run the same `updateMany` query.
   - Observe:
     - `matchedCount`: Number of documents that matched the filter (still matches even if no changes are made).
     - `modifiedCount`: Number of documents actually modified (should be `0` if no changes are needed).
   - **Why?** The query still matches the documents, but no changes are applied because the `status` is already `Cancelled`.

3. **Make the Data Inconsistent**:
   - **Run `reset.js`** to restore the fixture.
   - Update only **Document A** to cancel `LAB05-T-OK`:
     ```javascript
     m.journey_search.updateOne(
       { _id: "LAB05:A", "departures.tripId": "LAB05-T-OK" },
       { $set: { "departures.$[trip].status": "Cancelled" } },
       { arrayFilters: [{ "trip.tripId": "LAB05-T-OK" }] }
     );
     ```

4. **Check the Effect**:
   - Run the following searches:
     - **Nørreport to Airport**: Shows `LAB05-T-OK` as `Cancelled` (from Document A).
     - **Nørreport to Central**: Shows `LAB05-T-OK` as `Scheduled` (from Document C).
   - The searches now **disagree** about the status of the same trip.

5. **Repair the Data**:
   - Update the remaining copy of `LAB05-T-OK` in **Document C** to `Cancelled`.
   - Re-run the searches to verify they now agree.

### Key Takeaway
- **Duplicated data** makes reads convenient (e.g., `LAB05-T-OK` appears in multiple documents for different routes).
- **Updates must reach every copy** to maintain consistency. If not, searches can return conflicting results.
- **When to run `reset.js`**: Before making the data inconsistent (Step 3).

### Files
- [`updates/status.js`](./lecture05/updates/status.js): Contains all steps for this task.

### Status
✅ **Completed**: Duplicated data trade-off demonstrated and documented.

---

## 05: How Large Can the Document Get?

### Objective
Investigate the impact of document growth when storing many departures in a single document and decide how to limit it.

### Steps
1. **Create a Document with 100 Departures**:
   - Copy `LAB05:A` and replace its `departures` array with 100 departures.
   - Measure the document size using `$bsonSize`.

2. **Create a Document with 1,000 Departures**:
   - Replace the `departures` array with 1,000 departures.
   - Measure the document size again.

3. **Compare the Sizes**:
   - Observe the linear growth in document size as the number of departures increases.
   - Example output:
     - 100 departures: ~X bytes
     - 1,000 departures: ~10X bytes

### Decision: How to Limit Growth
**Limited window**: Keep only a limited time window of departures (e.g., 24-48 hours) in each document to prevent unbounded growth. This ensures documents remain manageable in size while still providing useful data for recent searches.

### Files
- [`growth.js`](./lecture05/growth.js): Script to test document growth with 100 and 1,000 departures.

### Status
✅ **Completed**: Document growth measured and mitigation strategy documented.

---

## 06: Try One Departure Per Document

### Objective
Implement an alternative data model where each departure is stored as its own document in a new collection (`journey_search_by_trip`). Compare this approach with the original model.

### Steps
1. **Build the Alternative Model**:
   - Create the `journey_search_by_trip` collection.
   - Loop over the existing `journey_search` documents and their departures.
   - For each departure, create a new document with the following fields:
     ```json
     {
       "_id": "cityId:routeId:fromStopId:toStopId:tripId",
       "cityId": "CPH",
       "routeId": "LINE-M2",
       "fromStopId": "STOP-NORREPORT",
       "toStopId": "STOP-AIRPORT",
       "tripId": "LAB05-T-OK",
       "departureUtc": ISODate("2026-10-02T06:20:00Z"),
       "arrivalUtc": ISODate("2026-10-02T06:40:00Z"),
       "status": "Scheduled",
       "availableSeats": 10,
       "price": NumberDecimal("36.00"),
       "currency": "DKK",
       "schemaVersion": 1
     }
     ```
   - The `_id` is deterministic and uniquely identifies the trip and stop pair.
   - With the fixture, the collection contains **10 documents** (including two for `LAB05-T-OK` because it appears in two different journeys).

2. **Implement the Search**:
   - Use the same input (`cityId`, `fromStopId`, `toStopId`, `start`, `end`) and return the same result format as the original search.
   - The query filters for `status: "Scheduled"` and `departureUtc` within the `[start, end)` range.

3. **Run the Same Tests**:
   - All test cases from **Task 02** are run against `journey_search_by_trip`.
   - The results match the original `journey_search` collection.

4. **Cancel the Trip**:
   - Update all documents with `tripId: "LAB05-T-OK"` to set `status: "Cancelled"`.
   - **Observation**: In the new model, only the documents with `tripId: "LAB05-T-OK"` need to be updated (2 documents).
   - In the original model, multiple journey documents had to be updated (e.g., `LAB05:A` and `LAB05:C`).

### Key Takeaway
- **One departure per document** simplifies updates (only the relevant trip documents need to be modified).
- **Trade-off**: Queries may require additional filtering (e.g., by `cityId`, `fromStopId`, `toStopId`) to achieve the same results as the original model.

### Files
- [`alternative/alternative.js`](./lecture05/alternative/alternative.js): Script to build the alternative model, implement the search, and test it.

### Status
✅ **Completed**: Alternative model implemented, tested, and documented.
