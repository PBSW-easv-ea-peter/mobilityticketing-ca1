const m = db.getSiblingDB("mobility");

// Update Many
printjson(
  m.journey_search.updateMany(
    {
      _id: /^LAB05:/,
      "departures.tripId": "LAB05-T-OK",
    },
    {
      $set: {
        "departures.$[trip].status": "Cancelled",
      },
    },
    {
      arrayFilters: [
        {
          "trip.tripId": "LAB05-T-OK",
        },
      ],
    },
  ),
);

// Update one
//printjson(
//  m.journey_search.updateOne(
//    {
//      _id: /^LAB05:/,
//      "departures.tripId": "LAB05-T-OK",
//    },
//    {
//      $set: {
//        "departures.$[trip].status": "Cancelled",
//      },
//    },
//    {
//      arrayFilters: [
//        {
//          "trip.tripId": "LAB05-T-OK",
//        },
//      ],
//    },
//  ),
//);

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

