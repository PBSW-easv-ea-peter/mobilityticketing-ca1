const m = db.getSiblingDB("mobility");

function measure(id) {
  return m.journey_search.aggregate([
    {
      $match: { _id: id },
    },
    {
      $project: {
        _id: 0,
        bytes: { $bsonSize: "$$ROOT" },
        departures: { $size: "$departures" },
      },
    },
  ]).toArray()[0];
}

function printMeasurement(label, measurement, originalBytes) {
  const growthBytes = measurement.bytes - originalBytes;
  const growthPercent = (growthBytes / originalBytes) * 100;

  print(`${label}`);
  printjson({
    bytes: measurement.bytes,
    departures: measurement.departures,
    growthBytes,
    growthPercent: `${growthPercent.toFixed(2)}%`,
  });
}

// --------------------------------------------------
// Step 1: Original document
// --------------------------------------------------

const original = m.journey_search.findOne({
  _id: "LAB05:A",
});

const originalMeasurement = measure("LAB05:A");

printMeasurement(
  "=== STEP 1: Original ===",
  originalMeasurement,
  originalMeasurement.bytes,
);

// --------------------------------------------------
// Step 2: Create 100 departures
// --------------------------------------------------

const growth = {
  ...original,
  _id: "LAB05:GROWTH",
  departures: Array.from({ length: 100 }, (_, i) => ({
    ...original.departures[0],
    tripId: `LAB05-GROWTH-${i}`,
  })),
};

m.journey_search.insertOne(growth);

const measurement100 = measure("LAB05:GROWTH");

printMeasurement(
  "=== STEP 2: 100 departures ===",
  measurement100,
  originalMeasurement.bytes,
);

// --------------------------------------------------
// Step 3: Add 900 more departures
// --------------------------------------------------

const additionalDepartures = Array.from({ length: 900 }, (_, i) => ({
  ...original.departures[0],
  tripId: `LAB05-GROWTH-${i + 100}`,
}));

m.journey_search.updateOne(
  { _id: "LAB05:GROWTH" },
  {
    $push: {
      departures: {
        $each: additionalDepartures,
      },
    },
  },
);

const measurement1000 = measure("LAB05:GROWTH");

printMeasurement(
  "=== STEP 3: 1000 departures ===",
  measurement1000,
  originalMeasurement.bytes,
);
