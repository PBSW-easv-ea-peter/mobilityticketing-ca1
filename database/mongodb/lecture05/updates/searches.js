const m = db.getSiblingDB("mobility");

const start = ISODate("2026-10-02T06:00:00Z");
const end = ISODate("2026-10-02T07:00:00Z");

const airport_query = {
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

const central_query = {
  cityId: "CPH",
  fromStopId: "STOP-NORREPORT",
  toStopId: "STOP-CENTRAL",
  departures: {
    $elemMatch: {
      status: "Scheduled",
      departureUtc: { $gte: start, $lt: end },
    },
  },
};

print("=== Nørreport → Airport ===");
printjson(m.journey_search.find(airport_query).toArray());

print("==========");

print("=== Nørreport → Central ===");
printjson(m.journey_search.find(central_query).toArray());

// After updating the departures
const startEarly = ISODate("2026-10-02T06:00:00Z");
const endEarly = ISODate("2026-10-02T07:00:00Z");

const startLate = ISODate("2026-10-02T07:00:00Z");
const endLate = ISODate("2026-10-02T08:00:00Z");

const search = (from, to, start, end) =>
  m.journey_search.find({
    cityId: "CPH",
    fromStopId: "STOP-NORREPORT",
    toStopId: to,
    departures: {
      $elemMatch: {
        tripId: "LAB05-T-OK",
        status: "Scheduled",
        departureUtc: { $gte: start, $lt: end },
      },
    },
  }).toArray();

print("=== 06:00 → 07:00 ===");
printjson(
  search("STOP-NORREPORT", "STOP-AIRPORT", startEarly, endEarly)
);

print("=== 07:00 → 08:00 ===");
printjson(
  search("STOP-NORREPORT", "STOP-AIRPORT", startLate, endLate)
);

