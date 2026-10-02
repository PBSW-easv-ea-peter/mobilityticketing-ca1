const m = db.getSiblingDB("mobility");

const start = ISODate("2026-10-02T06:00:00Z");
const end = ISODate("2026-10-02T07:00:00Z");

// Step 1: Cancel LAB05-T-OK in all documents
print("=== Step 1: Cancel LAB05-T-OK in all documents ===");
const updateResult = m.journey_search.updateMany(
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
);
printjson(updateResult);

// Step 2: Run the update again to observe matchedCount and modifiedCount
print("=== Step 2: Run the update again (should show matchedCount > 0, modifiedCount = 0) ===");
const secondUpdateResult = m.journey_search.updateMany(
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
);
printjson(secondUpdateResult);

// Step 3: Verify LAB05-T-OK no longer appears in the search
print("=== Step 3: Verify LAB05-T-OK no longer appears in the search ===");
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
printjson(m.journey_search.find(airport_query).toArray());

// Step 4: Reset the fixture (run reset.js manually before proceeding to the next steps)
print("=== Step 4: Reset the fixture (run reset.js manually) ===");

// Step 5: Make the data inconsistent - Cancel LAB05-T-OK only in Document A
print("=== Step 5: Cancel LAB05-T-OK only in Document A ===");
m.journey_search.updateOne(
  { _id: "LAB05:A", "departures.tripId": "LAB05-T-OK" },
  { $set: { "departures.$[trip].status": "Cancelled" } },
  { arrayFilters: [{ "trip.tripId": "LAB05-T-OK" }] }
);

// Step 6: Check the effect - searches now disagree
print("=== Step 6: Check the effect - searches now disagree ===");
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

print("Nørreport → Airport (LAB05-T-OK should be Cancelled):");
printjson(m.journey_search.find(airport_query).toArray());

print("Nørreport → Central (LAB05-T-OK should still be Scheduled):");
printjson(m.journey_search.find(central_query).toArray());

// Step 7: Repair the data - Update the remaining copy of LAB05-T-OK
print("=== Step 7: Repair the data - Update the remaining copy of LAB05-T-OK ===");
m.journey_search.updateOne(
  { _id: "LAB05:C", "departures.tripId": "LAB05-T-OK" },
  { $set: { "departures.$[trip].status": "Cancelled" } },
  { arrayFilters: [{ "trip.tripId": "LAB05-T-OK" }] }
);

// Step 8: Verify the searches now agree
print("=== Step 8: Verify the searches now agree ===");
print("Nørreport → Airport:");
printjson(m.journey_search.find(airport_query).toArray());

print("Nørreport → Central:");
printjson(m.journey_search.find(central_query).toArray());

