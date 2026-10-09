using System.Text.Json.Serialization;

namespace MobilityTicketing.Models;

public record Journey(
    [property: JsonRequired] string CityId,
    [property: JsonRequired] string RouteId,
    [property: JsonRequired] string FromStopId,
    [property: JsonRequired] string ToStopId,
    [property: JsonRequired] string TripId,
    [property: JsonRequired] DateTimeOffset DepartureUtc,
    [property: JsonRequired] DateTimeOffset ArrivalUtc,
    [property: JsonRequired] int AvailableSeats,
    [property: JsonRequired] decimal Price,
    [property: JsonRequired] string Currency);