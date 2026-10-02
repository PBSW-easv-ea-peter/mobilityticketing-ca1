const m = db.getSiblingDB("mobility");

const start = ISODate("2026-10-02T06:00:00Z");
const end = ISODate("2026-10-02T07:00:00Z");

const broken_query = {
  cityId: "CPH",
  fromStopId: "STOP-NORREPORT",
  toStopId: "STOP-AIRPORT",
  "departures.status": "Scheduled",

  // Separate condition on the same array — the bug. Changes the behavior of the '$match' from AND to OR.
  "departures.departureUtc": { $gte: start, $lt: end },
};

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

printjson(m.journey_search.find(broken_query).toArray());
print("========== broken query results above / fixed query results below ==========");
printjson(m.journey_search.find(fixed_query).toArray());
