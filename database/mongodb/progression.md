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
