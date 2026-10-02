const m = db.getSiblingDB("mobility");

function updateTrip(tripId, departureUtc, arrivalUtc) {
  return m.journey_search.updateMany(
    {
      "departures.tripId": tripId,
    },
    {
      $set: {
        "departures.$[trip].departureUtc": new Date(departureUtc),
        "departures.$[trip].arrivalUtc": new Date(arrivalUtc),
      },
    },
    {
      arrayFilters: [
        {
          "trip.tripId": tripId,
        },
      ],
    },
  );
}

printjson(
  updateTrip(
    "LAB05-T-OK",
    "2026-10-02T07:05:00Z",
    "2026-10-02T07:25:00Z",
  ),
);

