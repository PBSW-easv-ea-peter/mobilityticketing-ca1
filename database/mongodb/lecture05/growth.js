const m = db.getSiblingDB("mobility");

const original = m.journey_search.findOne({
  _id: "LAB05:A",
});

const growth = {
  ...original,
  _id: "LAB05:GROWTH",
};

growth.departures = Array.from({ length: 1000 }, (_, i) => ({
  ...original.departures[0],
  tripId: `LAB05-GROWTH-${i}`,
}));

m.journey_search.deleteOne({ _id: "LAB05:GROWTH" });

// Insert growth into journey_search.
function tryInsert(label, doc) {
    try{
        m.journey_search.insertOne(doc);
        print(`${label}: LYKKEDES - insert lykkedes`);
    } catch (e) {
        print(`${label}: fejlede med code ${e.code}`);
        printjson(e.errInfo?.details);
    }
}
tryInsert("GROWTH", growth);

printjson(
  m.journey_search.aggregate([
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
  ]).toArray(),
);
