# MongoDB: Journey Search

This week you will use MongoDB for journey search in MobilityTicketing.

PostgreSQL is still the main database and remains the source of truth for routes, trips, products and tickets. MongoDB will contain a separate model built specifically for search.

We will keep the problem fairly small. A passenger wants to search for direct departures between two stops within a given time period.

During the lab you will build the search, work with arrays in MongoDB, update duplicated data, and compare two different ways of structuring the documents.

## Starter code

Continue in your existing MobilityTicketing repository.

If you are starting fresh, you can clone the starter repository.

Add:

`database/mongodb/lecture05/`

The lab only covers direct journeys between an exact origin and destination.

All times in this lab are UTC.

All the .js files in this lab are run with mongosh. For example:

```bash
docker compose exec -T mongo \
  mongosh --quiet --file /scripts/lecture05/search.js
```

Replace search.js with the script you want to run.

You'll also be resetting the database between tasks. For the sake of convenience, you can use the following command:

```bash
docker compose exec -T mongo mongosh --quiet --file /scripts/lecture05/reset.js
```

## 1. Start MongoDB

Add MongoDB to your existing docker-compose.yml.

```yaml
mongo:
  image: mongo:8.0
  ports:
    - "127.0.0.1:27017:27017"
  volumes:
    - mobility-mongo:/data/db
    - ./database/mongodb:/scripts:ro
  healthcheck:
    test:
      [
        "CMD",
        "mongosh",
        "--quiet",
        "--eval",
        "quit(db.runCommand({ping:1}).ok ? 0 : 1)",
      ]
    interval: 2s
    timeout: 5s
    retries: 30
    start_period: 5s
```

Also add the volume:

```yaml
volumes:
  mobility-mongo:
```

Start MongoDB:

```bash
docker compose up -d --wait mongo
```

### Load the test data

The starter code contains setup.js.

Run:

```bash
docker compose exec -T mongo \
  mongosh --quiet --file /scripts/lecture05/setup.js
```

This creates six documents in:

`mobility.journey_search`

The important part of the model looks like this:

```js
{
  cityId,
  routeId,
  fromStopId,
  toStopId,
  serviceDate,
  price,
  currency,
  departures: [
    {
      tripId,
      departureUtc,
      arrivalUtc,
      availableSeats,
      status
    }
  ]
}
```

Each document represents a searchable journey and contains several departures.

You will also notice that LAB05-T-OK appears in more than one document. Leave it like that. We will use it later.

### Check the schema validator

The collection has a MongoDB schema validator.

Create validation.js and try two invalid inserts.

If you are unsure how to structure the script, start with a valid fixture document:

```js
const m = db.getSiblingDB("mobility");
const invalid = m.journey_search.findOne({ _id: "LAB05:A" });

invalid._id = "LAB05:INVALID";

// Change one field so the document becomes invalid.
// Then try m.journey_search.insertOne(invalid).
```

First, take a valid document and replace one departureUtc value with a string:

`2026-10-02T06:20:00Z`

Then try another document with:

```js
availableSeats: NumberInt(-1);
```

Both inserts should fail with validation error 121.

## 2. Implement journey search

Create:

`database/mongodb/lecture05/search.js`

Use five inputs: cityId, fromStopId, toStopId, start, and end.

A simple starting point for search.js is:

```js
const m = db.getSiblingDB("mobility");

function search(cityId, fromStopId, toStopId, start, end) {
  return m.journey_search
    .aggregate([
      {
        $match: { cityId, fromStopId, toStopId },
      },

      // TODO: work with the departures array here.
    ])
    .toArray();
}

const start = ISODate("2026-10-02T06:00:00Z");
const end = ISODate("2026-10-02T07:00:00Z");

printjson(search("CPH", "STOP-NORREPORT", "STOP-AIRPORT", start, end));
```

This only gives you the outer shape of the script. You still need to make sure that the returned departures satisfy the time and status requirements.

Start with this search:

```
City: CPH
From: STOP-NORREPORT
To: STOP-AIRPORT
Time: 06:00 to 07:00 UTC
Date: 2 October 2026
```

Return one result for each matching departure.

A result should contain:

```js
{
  (cityId,
    routeId,
    fromStopId,
    toStopId,
    tripId,
    departureUtc,
    arrivalUtc,
    availableSeats,
    price,
    currency);
}
```

A departure matches when:

- the city matches;
- the origin and destination match;
- the status is Scheduled;
- departureUtc is inside the requested time period.

The start of the interval is inclusive. The end is exclusive.

Sort the result by departure time.

If nothing matches, return:

```js
[];
```

For the first search, you should get:

```
LAB05-T-OK
06:20 to 06:40
36.00 DKK
10 available seats
```

### Hint

departures is an array.

Have a look at:

- $elemMatch
- $unwind
- $filter

You do not have to use all three.

### Test the search

Once the first search works, try these cases:

| Search |	Expected |
|--------|-------|
| Nørreport to Airport, 06:00 to 07:00 |	LAB05-T-OK |
| End at 06:20 |	[] |
| Start at 06:20 |	LAB05-T-OK |
| Airport to Nørreport |	LAB05-T-E |
| Same search on 3 October |	LAB05-T-F |
| Destination STOP-NO-MATCH |	[] |

Also reject invalid input such as:

`end <= start`

or an empty stop ID.

Save your results in the repository.

## 3. A query that looks right, but isn't

There is an easy mistake to make when querying arrays in MongoDB. Let's reproduce it first.

### Reproduce the problem

Create search_bug.js.

You can start the deliberately broken version like this:

```js
const m = db.getSiblingDB("mobility");

const query = {
  cityId: "CPH",
  fromStopId: "STOP-NORREPORT",
  toStopId: "STOP-AIRPORT",
  "departures.status": "Scheduled",

  // TODO: add departures.departureUtc
  // as a separate condition.
};

printjson(m.journey_search.find(query).toArray());
```

Use the same journey search as before, but match departures.status and departures.departureUtc with two separate conditions.

Run the search against the fixture.

Document B should now appear in the result, even though none of its departures should actually match the search.

### Find out why

Look at the departures stored in document B.

Find out which departure satisfies the status condition and which departure satisfies the time condition.

### Fix the query

Change the query so that both conditions must be true for the same departure.

Run the search again and check that document B no longer matches.

### Record what happened

Add a short explanation in your repository, documenting why the first query matched document B and what you changed to fix it.

## 4. Updating duplicated data

LAB05-T-OK is stored in more than one journey document.

That makes the trip easy to find from different searches, but it also means that a change to the trip may need to be applied in several places.

### Cancel the trip

Create updates.js.

Update every occurrence of LAB05-T-OK so its status becomes Cancelled.

You can use this as a starting point:

```js
const m = db.getSiblingDB("mobility");

printjson(
  m.journey_search.updateMany(
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
  ),
);
```

Run your normal journey search afterwards. LAB05-T-OK should no longer appear.

### Run the update again

Run the same update a second time.

Look at matchedCount and modifiedCount. Make sure you understand why the update can still match documents even when there is nothing left to change.

### Make the data inconsistent

Reset the fixture.

This time, cancel LAB05-T-OK only in document A.

### Check the effect

Run these two searches:

```
Nørreport to Airport

Nørreport to Central
```

The searches now disagree about the status of the same trip.

Inspect the documents and make sure you can see why.

### Repair the data

Update the remaining copy of LAB05-T-OK so the trip has the same status everywhere.

Run the searches again to verify that they agree.

This is the trade-off we care about here: duplicated data can make reads convenient, but changes have to reach every relevant copy.

### Move the departure

Reset the fixture.

Move every copy of LAB05-T-OK to:

```
departure: 07:05
arrival:   07:25
```

Check the result using your journey search.

A search from 06:00 to 07:00 should no longer find the trip. A search from 07:00 to 08:00 should.

### Change the displayed price

Reset the fixture again.

Change the price from 36.00 DKK to 40.00 DKK for documents belonging to:

```
cityId = CPH
routeId = LINE-M2
```

The affected fixture documents should be:

```
A
C
E
F

B and D should keep their original price.
```

Inspect document D and work out why it should not be changed even though it has the same route ID.

The price in this collection belongs to the MongoDB search model. Changing it does not alter tickets already stored in PostgreSQL.

## 5. How large can the document get?

Our fixture contains only a few departures in each document. A real journey-search model could contain far more.

Let's see what that does to the document itself.

### Try 100 departures

Create growth.js.

JavaScript's Array.from is useful here. For example:

```js
const m = db.getSiblingDB("mobility");

const original = m.journey_search.findOne({
  _id: "LAB05:A",
});

const growth = {
  ...original,
  _id: "LAB05:GROWTH",
};

growth.departures = Array.from({ length: 100 }, (_, i) => ({
  ...original.departures[0],
  tripId: `LAB05-GROWTH-${i}`,
}));

// TODO: insert growth into journey_search.
```

Make a copy of document A with this ID:

`LAB05:GROWTH`

Replace its departures with 100 departures.

Measure the size of the document with:

```js
db.getSiblingDB("mobility").journey_search.aggregate([
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
]);
```

Record the size.

### Try 1,000 departures

Replace the 100 departures with 1,000 departures and measure the document again.

Compare the two sizes.

### Decide how you would limit the growth

Based on what you measured, decide how you would prevent this type of document from growing indefinitely.

Keep the answer short. For example, you might split documents by date or keep only a limited window of departures.

## 6. Try one departure per document

So far, one document has represented a journey with several departures.

Now try the opposite approach: store each departure as its own document.

### Build the alternative model

Create alternative.js and create a new collection called:

`journey_search_by_trip`

A useful way to start is to loop over the existing documents and then their departures:

```js
const m = db.getSiblingDB("mobility");

const journeys = m.journey_search.find({ _id: /^LAB05:/ }).toArray();

for (const journey of journeys) {
  for (const departure of journey.departures) {
    // TODO: build one document
    // from journey + departure.
  }
}
```

Then store one dated trip and stop pair per document.

A document can contain:

```js
{
  (cityId,
    routeId,
    fromStopId,
    toStopId,
    tripId,
    departureUtc,
    arrivalUtc,
    status,
    availableSeats,
    price,
    currency,
    schemaVersion);
}
```

Choose a deterministic _id for each document. It should uniquely identify that particular trip and stop pair.

For example:

`city + route + origin + destination + trip`

With the supplied fixture, the collection should contain 10 documents.

LAB05-T-OK will still appear twice because the same trip is searchable using two different destinations.

### Implement the search

Implement the same journey search against journey_search_by_trip.

Use the same input and return the same result format as your original search.

### Run the same tests

Run the test cases from section 2 against both collections.

The searches should return the same results.

If they do not, inspect the stored documents and queries before continuing.

### Cancel the trip

Cancel LAB05-T-OK in the new model.

Notice which documents have to change compared with the first model.

### Compare the two models

You have now implemented the same journey search in two different ways:

journey_search stores several departures inside each document.

journey_search_by_trip stores one dated trip and stop pair per document.

Compare the two. Focus on how easy the search was to write, where data was duplicated, what happened when a trip changed, and how the document structure affected growth.

Finish by choosing which model you would continue with next week and explain your choice.

## Sample solution

After working through the lab, compare your results with the sample solution: Building and reshaping journey search. It includes the scripts, expected results, and validation results.