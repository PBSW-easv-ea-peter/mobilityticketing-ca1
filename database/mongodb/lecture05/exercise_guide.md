# Lecture 05: MongoDB Exercise Guide

## Task Guide

### Task 01: Verify Schema Validator
- **Script**: `validation.js`
- **Goal**: Test invalid inserts (`departureUtc` as string, `availableSeats` as negative).
- **Run**:
  ```bash
  docker compose exec -T mongo mongosh --quiet --file /scripts/lecture05/validation.js
  ```
- **Expected**: Both inserts fail with **validation error 121**.

---

### Task 02: Implement Journey Search
- **Script**: `search.js`
- **Goal**: Search for departures matching `cityId`, `fromStopId`, `toStopId`, `start`, and `end`.
- **Run**:
  ```bash
  docker compose exec -T mongo mongosh --quiet --file /scripts/lecture05/search.js
  ```
- **Expected**: All test cases pass (e.g., `LAB05-T-OK` for Nørreport → Airport, 06:00–07:00).

---

### Task 03: Debug Array Query
- **Script**: `search_bug.js`
- **Goal**: Reproduce and fix a bug where separate conditions on an array cause incorrect matches.
- **Run**:
  ```bash
  docker compose exec -T mongo mongosh --quiet --file /scripts/lecture05/search_bug.js
  ```
- **Expected**:
  - Broken query: Document B (`LAB05:B`) incorrectly matches.
  - Fixed query: Document B no longer matches.

---

### Task 04: Update Duplicated Data
- **Script**: `updates/status.js`
- **Goal**: Update `LAB05-T-OK` to `Cancelled` in all documents, then test inconsistencies.
- **Steps**:
  1. Run the script to cancel `LAB05-T-OK` everywhere:
     ```bash
     docker compose exec -T mongo mongosh --quiet --file /scripts/lecture05/updates/status.js
     ```
  2. **Reset the database** (for the inconsistency test):
     ```bash
     docker compose exec -T mongo mongosh --quiet --file /scripts/lecture05/reset.js
     ```
  3. Manually update **only Document A** (uncomment the `updateOne` in the script or run it separately).
  4. Re-run the script to see the inconsistency (Nørreport → Airport vs. Nørreport → Central).
  5. Repair by updating the remaining copy of `LAB05-T-OK`.

---

### Task 05: Document Growth
- **Script**: `growth.js`
- **Goal**: Measure document size with 100 and 1,000 departures.
- **Run**:
  ```bash
  docker compose exec -T mongo mongosh --quiet --file /scripts/lecture05/growth.js
  ```
- **Expected**:
  - 100 departures: ~X bytes.
  - 1,000 departures: ~10X bytes (linear growth).
- **Decision**: Use a **limited time window** (e.g., 24–48 hours) to cap document size.

---

### Task 06: One Departure Per Document
- **Script**: `alternative/alternative.js`
- **Goal**: Create `journey_search_by_trip` collection and implement the same search.
- **Run**:
  ```bash
  docker compose exec -T mongo mongosh --quiet --file /scripts/lecture05/alternative/alternative.js
  ```
- **Expected**:
  - 10 documents inserted into `journey_search_by_trip`.
  - All test cases from Task 02 pass.
  - Cancelling `LAB05-T-OK` updates **only 2 documents** (vs. multiple in the original model).
