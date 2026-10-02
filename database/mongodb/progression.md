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
