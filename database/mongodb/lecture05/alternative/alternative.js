const m = db.getSiblingDB("mobility");

// Step 1: Build the alternative model (journey_search_by_trip)
const journeys = m.journey_search
  .find({ _id: /^LAB05:/ })
  .toArray();

const tripDocuments = [];

for (const journey of journeys) {
  for (const departure of journey.departures) {
    const tripDocument = {
      _id: [
        journey.cityId,
        journey.routeId,
        journey.fromStopId,
        journey.toStopId,
        departure.tripId,
      ].join(":"),
      cityId: journey.cityId,
      routeId: journey.routeId,
      fromStopId: journey.fromStopId,
      toStopId: journey.toStopId,
      tripId: departure.tripId,
      departureUtc: departure.departureUtc,
      arrivalUtc: departure.arrivalUtc,
      status: departure.status,
      availableSeats: departure.availableSeats,
      price: journey.price,
      currency: journey.currency,
      schemaVersion: journey.schemaVersion,
    };

    tripDocuments.push(tripDocument);
  }
}

m.journey_search_by_trip.deleteMany({
  _id: /^LAB05:/,
});

m.journey_search_by_trip.insertMany(tripDocuments);

printjson({
  inserted: tripDocuments.length,
});

// Step 2: Implement the search for journey_search_by_trip
function searchByTrip(cityId, fromStopId, toStopId, start, end) {
  // Input validation
  if (!cityId || !fromStopId || !toStopId || end <= start) {
    return [];
  }

  return m.journey_search_by_trip
    .find({
      cityId,
      fromStopId,
      toStopId,
      status: "Scheduled",
      departureUtc: { $gte: start, $lt: end },
    })
    .sort({ departureUtc: 1 })
    .project({
      _id: 0,
      cityId: 1,
      routeId: 1,
      fromStopId: 1,
      toStopId: 1,
      tripId: 1,
      departureUtc: 1,
      arrivalUtc: 1,
      availableSeats: 1,
      price: 1,
      currency: 1,
    })
    .toArray();
}

// Step 3: Run the same test cases from Task 02
const start = ISODate("2026-10-02T06:00:00Z");
const end = ISODate("2026-10-02T07:00:00Z");

print("=== Test 1: Nørreport to Airport, 06:00 to 07:00 ===");
printjson(searchByTrip("CPH", "STOP-NORREPORT", "STOP-AIRPORT", start, end));

print("=== Test 2: End at 06:20 ===");
printjson(searchByTrip("CPH", "STOP-NORREPORT", "STOP-AIRPORT", start, ISODate("2026-10-02T06:20:00Z")));

print("=== Test 3: Start at 06:20 ===");
printjson(searchByTrip("CPH", "STOP-NORREPORT", "STOP-AIRPORT", ISODate("2026-10-02T06:20:00Z"), end));

print("=== Test 4: Airport to Nørreport ===");
printjson(searchByTrip("CPH", "STOP-AIRPORT", "STOP-NORREPORT", start, end));

print("=== Test 5: Same search on 3 October ===");
printjson(searchByTrip("CPH", "STOP-NORREPORT", "STOP-AIRPORT", ISODate("2026-10-03T06:00:00Z"), ISODate("2026-10-03T07:00:00Z")));

print("=== Test 6: Destination STOP-NO-MATCH ===");
printjson(searchByTrip("CPH", "STOP-NORREPORT", "STOP-NO-MATCH", start, end));

print("=== Test 7: Invalid input (end <= start) ===");
printjson(searchByTrip("CPH", "STOP-NORREPORT", "STOP-AIRPORT", end, start));

print("=== Test 8: Invalid input (empty stop ID) ===");
printjson(searchByTrip("CPH", "", "STOP-AIRPORT", start, end));

// Step 4: Cancel LAB05-T-OK in the new model
print("=== Step 4: Cancel LAB05-T-OK in journey_search_by_trip ===");
const cancelResult = m.journey_search_by_trip.updateMany(
  { tripId: "LAB05-T-OK" },
  { $set: { status: "Cancelled" } }
);
printjson(cancelResult);

// Verify the cancellation
print("=== Verify LAB05-T-OK is now Cancelled ===");
printjson(
  m.journey_search_by_trip
    .find({ tripId: "LAB05-T-OK" })
    .project({ _id: 1, tripId: 1, status: 1 })
    .toArray()
);

// Notice: In the new model, only the documents with tripId LAB05-T-OK are updated.
// In the original model, multiple journey documents had to be updated.
