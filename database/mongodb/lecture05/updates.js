const m = db.getSiblingDB("mobility");

// Change the displayed price for CPH / LINE-M2 from 36.00 to 40.00 DKK.
printjson(
  m.journey_search.updateMany(
    {
      _id: /^LAB05:/,
      cityId: "CPH",
      routeId: "LINE-M2",
    },
    {
      $set: {
        price: NumberDecimal("40.00"),
      },
    },
  ),
);

printjson(
  m.journey_search
    .find({ _id: /^LAB05:/ }, { _id: 1, cityId: 1, routeId: 1, price: 1 })
    .toArray(),
);
