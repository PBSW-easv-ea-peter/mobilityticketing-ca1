const m = db.getSiblingDB("mobility");
const invalid = m.journey_search.findOne({ _id: "LAB05:A" });

invalid._id = "LAB05:INVALID";

try {
  m.journey_search.insertOne({
    _id: "LAB05:A",
    cityId: "CPH",
    routeId: "LINE-M2",
    fromStopId: "STOP-NORREPORT",
    toStopId: "STOP-AIRPORT",
    serviceDate: "2026-10-02",
    schemaVersion: NumberInt(1),
    price: NumberDecimal("36.00"),
    currency: "EUR", // Changed from 'DKK' to 'EUR' so validation fails.
    departures: [
      {
        tripId: "LAB05-T-EARLY",
        departureUtc: new Date("2026-10-02T05:50:00Z"),
        arrivalUtc: new Date("2026-10-02T06:10:00Z"),
        availableSeats: NumberInt(10),
        status: "Scheduled"
      },
      {
        tripId: "LAB05-T-CANCEL",
        departureUtc: new Date("2026-10-02T06:10:00Z"),
        arrivalUtc: new Date("2026-10-02T06:30:00Z"),
        availableSeats: NumberInt(10),
        status: "Cancelled"
      },
      {
        tripId: "LAB05-T-OK",
        departureUtc: new Date("2026-10-02T06:20:00Z"),
        arrivalUtc: new Date("2026-10-02T06:40:00Z"),
        availableSeats: NumberInt(10),
        status: "Scheduled"
      },
      {
        tripId: "LAB05-T-EDGE",
        departureUtc: new Date("2026-10-02T07:00:00Z"),
        arrivalUtc: new Date("2026-10-02T07:20:00Z"),
        availableSeats: NumberInt(10),
        status: "Scheduled"
      }
    ]
  });
  print("LAB05:A inserted successfully");
} catch (e) {
  print("LAB05:A insert FAILED:");
  print("  codeName: " + e.codeName);
  print("  message: " + e.message);
}

try {
  m.journey_search.insertOne({
    _id: "LAB05:B",
    //  cityId: "CPH", Required field
    routeId: "LAB-OTHER-ROUTE",
    fromStopId: "STOP-NORREPORT",
    toStopId: "STOP-AIRPORT",
    serviceDate: "2026-10-02",
    schemaVersion: NumberInt(1),
    price: NumberDecimal("36.00"),
    currency: "DKK",
    departures: [
      {
        tripId: "LAB05-T-B1",
        departureUtc: new Date("2026-10-02T05:55:00Z"),
        arrivalUtc: new Date("2026-10-02T06:15:00Z"),
        availableSeats: NumberInt(10),
        status: "Scheduled"
      },
      {
        tripId: "LAB05-T-B2",
        departureUtc: new Date("2026-10-02T06:15:00Z"),
        arrivalUtc: new Date("2026-10-02T06:35:00Z"),
        availableSeats: NumberInt(10),
        status: "Cancelled"
      }
    ]
  });
  print("LAB05:B inserted successfully");
} catch (e) {
  print("LAB05:B insert FAILED:");
  print("  codeName: " + e.codeName);
  print("  message: " + e.message);
}

printjson({
  fixtureDocuments: m.journey_search.countDocuments({_id: /^LAB05:/}),
  alternativeDocuments: m.journey_search_by_trip.countDocuments({
    tripId: /^LAB05-/
  })
});
