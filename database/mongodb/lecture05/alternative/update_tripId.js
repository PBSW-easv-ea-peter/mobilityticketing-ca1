const m = db.getSiblingDB("mobility");

// Old update
m.journey_search.updateMany(
  {
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
  }
);

// New update
m.journey_search_by_trip.updateMany(
  {
    tripId: "LAB05-T-OK",
  },
  {
    $set: {
      status: "Cancelled",
    },
  }
);
