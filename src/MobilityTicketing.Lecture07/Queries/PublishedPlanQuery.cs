using Microsoft.EntityFrameworkCore;
using MobilityTicketing.Lecture07.Contracts;
using MobilityTicketing.Lecture07.Data;

namespace MobilityTicketing.Lecture07.Queries;

// One read with a join; only the copied plan values are loaded, never Route or Trip entities.
public static class PublishedPlanQuery
{
    public static async Task<PublishedSnapshot> ExecuteAsync(LectureDbContext db, string planId, CancellationToken ct = default)
    {
        DatabaseSession.RequireText(planId, "Snapshot ID");
        var plan = await db.PublishedPlans
            .AsNoTracking()
            .Include(p => p.Rows.OrderBy(r => r.RouteId))
            .SingleOrDefaultAsync(p => p.Id == planId, ct)
            ?? throw new SnapshotNotFoundException(planId);
        return new PublishedSnapshot(
            plan.Id,
            new TimeWindow(plan.WindowFromUtc, plan.WindowToUtc),
            plan.PublishedAtUtc,
            plan.Rows.Select(r => new RouteOverviewDto(r.RouteId, r.RouteName, r.DepartureCount, r.NextDepartureUtc)).ToList());
    }
}
