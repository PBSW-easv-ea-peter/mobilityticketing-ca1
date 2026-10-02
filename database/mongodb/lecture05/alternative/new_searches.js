const m = db.getSiblingDB("mobility");

function searchByTrip(cityId, fromStopId, toStopId, start, end) {
  return m.journey_search_by_trip
    .find(
      {
        cityId,
        fromStopId,
        toStopId,
        status: "Scheduled",
        departureUtc: {
          $gte: start,
          $lt: end,
        },
      },
      {
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
      }
    )
    .sort({ departureUtc: 1 })
    .toArray();
}

const start = ISODate("2026-10-02T06:00:00Z");
const end = ISODate("2026-10-02T07:00:00Z");

print("=== Nørreport → Airport ===");
printjson(
  searchByTrip(
    "CPH",
    "STOP-NORREPORT",
    "STOP-AIRPORT",
    start,
    end
  )
);

print("=== Nørreport → Central ===");
printjson(
  searchByTrip(
    "CPH",
    "STOP-NORREPORT",
    "STOP-CENTRAL",
    start,
    end
  )
);
