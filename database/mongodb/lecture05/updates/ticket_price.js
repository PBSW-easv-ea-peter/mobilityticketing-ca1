const m = db.getSiblingDB("mobility");

const result = m.journey_search.updateMany(
  {
    cityId: "CPH",
    routeId: "LINE-M2",
  },
  {
    $set: {
      price: NumberDecimal("40.00"),
    },
  },
);

printjson(result);

print("=== Updated documents ===");
printjson(
  m.journey_search.find(
    { _id: /^LAB05:/ },
    { _id: 1, cityId: 1, routeId: 1, price: 1 }
  ).sort({ _id: 1 }).toArray()
);
