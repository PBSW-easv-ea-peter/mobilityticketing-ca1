const m = db.getSiblingDB("mobility");

function search(cityId, fromStopId, toStopId, start, end) {
  // Input validation
  if (!cityId || !fromStopId || !toStopId || end <= start) {
    return [];
  }

  return m.journey_search
    .aggregate([
      {
        // 1. Only look at documents for this city + stop pair.
        $match: { cityId, fromStopId, toStopId },
      },
      {
        // 2. One document per departure instead of per journey.
        $unwind: "$departures",
      },
      {
        // 3. Keep only Scheduled departures inside [start, end).
        $match: {
          "departures.status": "Scheduled",
          "departures.departureUtc": { $gte: start, $lt: end },
        },
      },
      {
        // 4. Earliest departure first.
        $sort: { "departures.departureUtc": 1 },
      },
      {
        // 5. Flatten to the required result shape.
        $project: {
          _id: 0,
          cityId: 1,
          routeId: 1,
          fromStopId: 1,
          toStopId: 1,
          tripId: "$departures.tripId",
          departureUtc: "$departures.departureUtc",
          arrivalUtc: "$departures.arrivalUtc",
          availableSeats: "$departures.availableSeats",
          price: 1,
          currency: 1,
        },
      },
    ])
    .toArray();
}

// Test cases
const start = ISODate("2026-10-02T06:00:00Z");
const end = ISODate("2026-10-02T07:00:00Z");

// 1. Nørreport to Airport, 06:00 to 07:00 → LAB05-T-OK
print("Test 1: Nørreport to Airport, 06:00 to 07:00");
printjson(search("CPH", "STOP-NORREPORT", "STOP-AIRPORT", start, end));

// 2. End at 06:20 → []
print("Test 2: End at 06:20");
printjson(search("CPH", "STOP-NORREPORT", "STOP-AIRPORT", start, ISODate("2026-10-02T06:20:00Z")));

// 3. Start at 06:20 → LAB05-T-OK
print("Test 3: Start at 06:20");
printjson(search("CPH", "STOP-NORREPORT", "STOP-AIRPORT", ISODate("2026-10-02T06:20:00Z"), end));

// 4. Airport to Nørreport → LAB05-T-E
print("Test 4: Airport to Nørreport");
printjson(search("CPH", "STOP-AIRPORT", "STOP-NORREPORT", start, end));

// 5. Same search on 3 October → LAB05-T-F
print("Test 5: Same search on 3 October");
printjson(search("CPH", "STOP-NORREPORT", "STOP-AIRPORT", ISODate("2026-10-03T06:00:00Z"), ISODate("2026-10-03T07:00:00Z")));

// 6. Destination STOP-NO-MATCH → []
print("Test 6: Destination STOP-NO-MATCH");
printjson(search("CPH", "STOP-NORREPORT", "STOP-NO-MATCH", start, end));

// 7. Invalid input: end <= start → []
print("Test 7: Invalid input (end <= start)");
printjson(search("CPH", "STOP-NORREPORT", "STOP-AIRPORT", end, start));

// 8. Invalid input: empty stop ID → []
print("Test 8: Invalid input (empty stop ID)");
printjson(search("CPH", "", "STOP-AIRPORT", start, end));

