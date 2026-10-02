const m = db.getSiblingDB("mobility");

const original = m.journey_search.findOne({
  _id: "LAB05:A",
});

// Create a copy of document A with 100 departures
const growth100 = {
  ...original,
  _id: "LAB05:GROWTH",
};

growth100.departures = Array.from({ length: 100 }, (_, i) => ({
  ...original.departures[0],
  tripId: `LAB05-GROWTH-${i}`,
}));

// Insert the document with 100 departures
m.journey_search.insertOne(growth100);

// Measure the size of the document with 100 departures
const size100 = m.journey_search.aggregate([
  {
    $match: {
      _id: "LAB05:GROWTH",
    },
  },
  {
    $project: {
      _id: 0,
      bytes: {
        $bsonSize: "$$ROOT",
      },
      departures: {
        $size: "$departures",
      },
    },
  },
]).toArray();

print("=== Document with 100 departures ===");
printjson(size100);

// Replace the departures with 1,000 departures
const growth1000 = {
  ...original,
  _id: "LAB05:GROWTH",
};

growth1000.departures = Array.from({ length: 1000 }, (_, i) => ({
  ...original.departures[0],
  tripId: `LAB05-GROWTH-${i}`,
}));

// Replace the existing document with 1,000 departures
m.journey_search.replaceOne({ _id: "LAB05:GROWTH" }, growth1000);

// Measure the size of the document with 1,000 departures
const size1000 = m.journey_search.aggregate([
  {
    $match: {
      _id: "LAB05:GROWTH",
    },
  },
  {
    $project: {
      _id: 0,
      bytes: {
        $bsonSize: "$$ROOT",
      },
      departures: {
        $size: "$departures",
      },
    },
  },
]).toArray();

print("=== Document with 1,000 departures ===");
printjson(size1000);

// Compare the two sizes
print("=== Comparison ===");
print(`100 departures: ${size100[0].bytes} bytes`);
print(`1,000 departures: ${size1000[0].bytes} bytes`);
print(`Ratio: ${(size1000[0].bytes / size100[0].bytes).toFixed(2)}x`);

// Clean up
m.journey_search.deleteOne({ _id: "LAB05:GROWTH" });

// Decision: How to limit growth
print("\n=== Decision: How to limit growth ===");
print("Limited window: Keep only a limited time window of departures (e.g., 24-48 hours) to prevent unbounded growth.");
