const m = db.getSiblingDB("mobility");

function search(cityId, fromStopId, toStopId, start, end) {
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

const start = ISODate("2026-10-02T06:00:00Z");
const end = ISODate("2026-10-02T07:00:00Z");

printjson(search("CPH", "STOP-NORREPORT", "STOP-AIRPORT", start, end));
printjson(search("RANDERS", "STOP-NORREPORT", "STOP-AIRPORT", start, end));

