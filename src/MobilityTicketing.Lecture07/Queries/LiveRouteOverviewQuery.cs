using Microsoft.EntityFrameworkCore;
using MobilityTicketing.Lecture07.Contracts;
using MobilityTicketing.Lecture07.Data;

namespace MobilityTicketing.Lecture07.Queries;

// Projects straight to the DTO: one SQL read, no Route or Trip entities loaded.
public static class LiveRouteOverviewQuery
{
    public static Task<List<RouteOverviewDto>> ExecuteAsync(LectureDbContext db, TimeWindow window, CancellationToken ct = default)
    {
        window.Validate();
        var from = window.FromUtc;
        var to = window.ToUtc;
        return db.Routes
            .OrderBy(r => r.Id)
            .Select(r => new RouteOverviewDto(
                r.Id,
                r.Name,
                r.Trips.Count(t => t.Status == "Scheduled" && t.DepartureUtc >= from && t.DepartureUtc < to),
                r.Trips
                    .Where(t => t.Status == "Scheduled" && t.DepartureUtc >= from && t.DepartureUtc < to)
                    .Min(t => (DateTime?)t.DepartureUtc)))
            .ToListAsync(ct);
    }
}
