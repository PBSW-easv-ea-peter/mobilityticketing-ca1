const m = db.getSiblingDB("mobility");

function tryInsert(label, doc) {
  try {
    m.journey_search.insertOne(doc);
    print(`${label}: UVENTET - insert lykkedes`);
  } catch (e) {
    print(`${label}: fejlede med code ${e.code}`);
    printjson(e.errInfo?.details);
  }
}

function freshCopy(newId) {
  const doc = m.journey_search.findOne({ _id: "LAB05:A" });
  if (doc === null) throw new Error("LAB05:A findes ikke - kør reset.js");
  doc._id = newId;
  return doc;
}

// Test 1: departureUtc som string i stedet for date
const badDate = freshCopy("LAB05:INVALID-DATE");
badDate.departures[0].departureUtc = "2026-10-02T06:20:00Z";
tryInsert("Test 1 (string-dato)", badDate);

// Test 2: negativt antal pladser
const badSeats = freshCopy("LAB05:INVALID-SEATS");
badSeats.departures[0].availableSeats = NumberInt(-1);
tryInsert("Test 2 (negative seats)", badSeats);
