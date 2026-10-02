const m = db.getSiblingDB("mobility");

// Test 1: Replace departureUtc with a string
const invalidDeparture = m.journey_search.findOne({ _id: "LAB05:A" });
invalidDeparture._id = "LAB05:INVALID_DEPARTURE";
invalidDeparture.departures[0].departureUtc = "2026-10-02T06:20:00Z";

try {
  m.journey_search.insertOne(invalidDeparture);
  print("Test 1: Insert with string departureUtc succeeded (UNEXPECTED)");
} catch (e) {
  print("Test 1: Insert with string departureUtc FAILED (EXPECTED):");
  print("  Error Code: " + e.code);
  print("  Error Message: " + e.message);
}

// Test 2: Set availableSeats to a negative value
const invalidSeats = m.journey_search.findOne({ _id: "LAB05:A" });
invalidSeats._id = "LAB05:INVALID_SEATS";
invalidSeats.departures[0].availableSeats = NumberInt(-1);

try {
  m.journey_search.insertOne(invalidSeats);
  print("Test 2: Insert with negative availableSeats succeeded (UNEXPECTED)");
} catch (e) {
  print("Test 2: Insert with negative availableSeats FAILED (EXPECTED):");
  print("  Error Code: " + e.code);
  print("  Error Message: " + e.message);
}
