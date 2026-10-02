const m = db.getSiblingDB("mobility");

const journeys = m.journey_search
  .find({ _id: /^LAB05:/ })
  .toArray();

const tripDocuments = [];

for (const journey of journeys) {
  for (const departure of journey.departures) {
    const tripDocument = {
      _id: [
        journey.cityId,
        journey.routeId,
        journey.fromStopId,
        journey.toStopId,
        departure.tripId,
      ].join(":"),
      cityId: journey.cityId,
      routeId: journey.routeId,
      fromStopId: journey.fromStopId,
      toStopId: journey.toStopId,
      tripId: departure.tripId,
      departureUtc: departure.departureUtc,
      arrivalUtc: departure.arrivalUtc,
      status: departure.status,
      availableSeats: departure.availableSeats,
      price: journey.price,
      currency: journey.currency,
      schemaVersion: journey.schemaVersion,
    };

    tripDocuments.push(tripDocument);
  }
}

m.journey_search_by_trip.deleteMany({
  _id: /^LAB05:/,
});

m.journey_search_by_trip.insertMany(tripDocuments);

printjson({
  inserted: tripDocuments.length,
});

printjson(
  m.journey_search_by_trip
    .find({ _id: /^CPH:/ })
    .sort({ _id: 1 })
    .toArray()
);
