const m = db.getSiblingDB("mobility");

const start = ISODate("2026-10-02T06:00:00Z");
const end = ISODate("2026-10-02T07:00:00Z");

// Buggy: each condition can be satisfied by a different departure.
const buggyQuery = {
  cityId: "CPH",
  fromStopId: "STOP-NORREPORT",
  toStopId: "STOP-AIRPORT",
  "departures.status": "Scheduled",
  "departures.departureUtc": { $gte: start, $lt: end },
};

// Fixed: $elemMatch requires both conditions on the same departure.
const fixedQuery = {
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

print("Buggy query:");
printjson(m.journey_search.find(buggyQuery, { _id: 1, departures: 1 }).toArray());

print("Fixed query:");
printjson(m.journey_search.find(fixedQuery, { _id: 1, departures: 1 }).toArray());
