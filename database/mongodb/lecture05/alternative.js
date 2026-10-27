const m = db.getSiblingDB("mobility");

if (!m.getCollectionNames().includes("journey_search_by_trip")) {
  m.createCollection("journey_search_by_trip");
} else {
  m.journey_search_by_trip.deleteMany({
    $or: [
      { _id: /^LAB05:/ },
      { tripId: /^LAB05-/ },
    ],
  });
}

const journeys = m.journey_search.find({ _id: /^LAB05:/ }).toArray();

// One document per dated trip and stop pair.
const docs = [];
for (const journey of journeys) {
  for (const departure of journey.departures) {
    docs.push({
      _id: [journey.cityId, journey.routeId, journey.fromStopId,
            journey.toStopId, departure.tripId].join(":"),
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
    });
  }
}
m.journey_search_by_trip.insertMany(docs);
print(`Inserted ${docs.length} documents`);

function searchByTrip(cityId, fromStopId, toStopId, start, end) {
  if (!cityId || !fromStopId || !toStopId) {
    throw new Error("cityId, fromStopId and toStopId are required");
  }
  if (!(start instanceof Date) || !(end instanceof Date) || end <= start) {
    throw new Error("start and end must be dates with end > start");
  }

  return m.journey_search_by_trip
    .find(
      { cityId, fromStopId, toStopId, status: "Scheduled",
        departureUtc: { $gte: start, $lt: end } },
      { _id: 0, status: 0, schemaVersion: 0 },
    )
    .sort({ departureUtc: 1 })
    .toArray();
}

const d = (s) => ISODate(`2026-10-${s}Z`);
const N = "STOP-NORREPORT";
const A = "STOP-AIRPORT";

const cases = [
  ["Nørreport to Airport, 06:00-07:00", ["CPH", N, A, d("02T06:00:00"), d("02T07:00:00")], ["LAB05-T-OK"]],
  ["End at 06:20",                      ["CPH", N, A, d("02T06:00:00"), d("02T06:20:00")], []],
  ["Start at 06:20",                    ["CPH", N, A, d("02T06:20:00"), d("02T07:00:00")], ["LAB05-T-OK"]],
  ["Airport to Nørreport",              ["CPH", A, N, d("02T06:00:00"), d("02T07:00:00")], ["LAB05-T-E"]],
  ["Same search on 3 October",          ["CPH", N, A, d("03T06:00:00"), d("03T07:00:00")], ["LAB05-T-F"]],
  ["Destination STOP-NO-MATCH",         ["CPH", N, "STOP-NO-MATCH", d("02T06:00:00"), d("02T07:00:00")], []],
  ["Nørreport to Central",              ["CPH", N, "STOP-CENTRAL", d("02T06:00:00"), d("02T07:00:00")], ["LAB05-T-OK"]],
  ["Nørreport to Airport, 07:00-08:00", ["CPH", N, A, d("02T07:00:00"), d("02T08:00:00")], ["LAB05-T-EDGE"]],
];

const invalid = [
  ["end <= start", ["CPH", N, A, d("02T07:00:00"), d("02T06:00:00")]],
  ["empty stop ID", ["CPH", "", A, d("02T06:00:00"), d("02T07:00:00")]],
];

function runTests() {
  for (const [label, args, expected] of cases) {
    const actual = searchByTrip(...args).map((r) => r.tripId);
    const ok = JSON.stringify(actual) === JSON.stringify(expected);
    print(`${ok ? "PASS" : "FAIL"}  ${label}: ${JSON.stringify(actual)}`);
  }

  for (const [label, args] of invalid) {
    try {
      searchByTrip(...args);
      print(`FAIL  ${label}: no error`);
    } catch (e) {
      print(`PASS  ${label}: rejected (${e.message})`);
    }
  }
}

print("--- Before cancel ---");
runTests();

const res = m.journey_search_by_trip.updateMany(
  { tripId: "LAB05-T-OK" },
  { $set: { status: "Cancelled" } },
);
print(`Cancel: matched ${res.matchedCount}, modified ${res.modifiedCount}`);

print("--- After cancel ---");
runTests();
