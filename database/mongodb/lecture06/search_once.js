const m = db.getSiblingDB("mobility");

const cityId = process.env.CITY ?? "CPH";
const fromStopId = process.env.FROM ?? "STOP-NORREPORT";
const toStopId = process.env.TO ?? "STOP-AIRPORT";
const start = new Date(process.env.START);
const end = new Date(process.env.END);

if(isNaN(start) || isNaN(end) || end <= start) {
    throw new Error("START and END must be valid UTC times with END > START");
}

const result = m.journey_search.aggregate([
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
]).toArray();

print(JSON.stringify(result));