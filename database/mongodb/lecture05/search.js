const m = db.getSiblingDB("mobility");

function search(cityId, fromStopId, toStopId, start, end) {
  if (!cityId || !fromStopId || !toStopId) {
    throw new Error("cityId, fromStopId and toStopId are required");
  }
  if (!(start instanceof Date) || !(end instanceof Date) || end <= start) {
    throw new Error("start and end must be dates with end > start");
  }

  return m.journey_search
    .aggregate([
      { $match: { cityId, fromStopId, toStopId }, },
      { $unwind: "$departures" },
      {
        $match: {
          "departures.status": "Scheduled",
          "departures.departureUtc": { $gte: start,$lt: end },
        },
      },
      {
        $project: {
          _id: 0,
          cityId: 1,
          routeId: 1,
          fromStopId: 1,
          toStopId: 1,
          tripId: "$departures.tripId",
          departureUtc: "$departures.departureUtc",
          arrivalUtc: "$departures.arrivalUtc",
          availableSeats: "$departures.availableSeats",
          price: 1,
          currency: 1,
        },
      },
      { $sort: { departureUtc: 1} },
    ])
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

for (const [label, args, expected] of cases) {
  const actual = search(...args).map((r) => r.tripId);
  const ok = JSON.stringify(actual) === JSON.stringify(expected);
  print(`${ok ? "PASS" : "FAIL"}  ${label}: ${JSON.stringify(actual)}`);
}

const invalid = [
  ["end <= start", ["CPH", N, A, d("02T07:00:00"), d("02T06:00:00")]],
  ["empty stop ID", ["CPH", "", A, d("02T06:00:00"), d("02T07:00:00")]],
];

for (const [label, args] of invalid) {
  try {
    search(...args);
    print(`FAIL  ${label}: no error`);
  } catch (e) {
    print(`PASS  ${label}: rejected (${e.message})`);
  }
}
