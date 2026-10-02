const m = db.getSiblingDB("mobility");

const start = ISODate("2026-10-02T06:00:00Z");
const end = ISODate("2026-10-02T07:00:00Z");

// Broken query: Separate conditions on the departures array.
// This matches if ANY departure satisfies the status condition
// AND ANY (possibly different) departure satisfies the time condition.
// Document B (LAB05:B) will incorrectly match because:
// - LAB05-T-B1 has status: "Scheduled" (satisfies status condition).
// - LAB05-T-B2 has departureUtc: 2026-10-02T06:15:00Z (satisfies time condition).
const broken_query = {
  cityId: "CPH",
  fromStopId: "STOP-NORREPORT",
  toStopId: "STOP-AIRPORT",
  "departures.status": "Scheduled",
  "departures.departureUtc": { $gte: start, $lt: end },
};

// Fixed query: Use $elemMatch to ensure both conditions apply to the SAME departure.
// This ensures that only departures matching BOTH status and time conditions are included.
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

// Test the broken query (Document B should appear incorrectly).
print("=== Broken Query Results (Document B should appear incorrectly) ===");
printjson(m.journey_search.find(broken_query).toArray());

// Test the fixed query (Document B should NOT appear).
print("=== Fixed Query Results (Document B should NOT appear) ===");
printjson(m.journey_search.find(fixed_query).toArray());
