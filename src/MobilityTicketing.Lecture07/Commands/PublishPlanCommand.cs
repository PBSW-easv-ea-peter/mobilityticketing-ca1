using Microsoft.EntityFrameworkCore;
using MobilityTicketing.Lecture07.Contracts;
using MobilityTicketing.Lecture07.Data;
using MobilityTicketing.Lecture07.Queries;

namespace MobilityTicketing.Lecture07.Commands;

// One transaction keyed by plan ID: header and copied rows are saved together or not at all.
public static class PublishPlanCommand
{
    public static Task<PublishResult> ExecuteAsync(string connection, TimeProvider clock, ApplicationHooks hooks,
        string planId, TimeWindow window, CancellationToken ct = default)
    {
        DatabaseSession.RequireText(planId, "Snapshot ID");
        window.Validate();
        return DatabaseSession.WriteAsync(connection, "plan:" + planId, async db =>
        {
            var existing = await db.PublishedPlans.SingleOrDefaultAsync(p => p.Id == planId, ct);
            if (existing is not null)
            {
                // Valid windows are whole microseconds, so the stored values compare exactly.
                if (existing.WindowFromUtc == window.FromUtc && existing.WindowToUtc == window.ToUtc)
                    return PublishResult.AlreadyPublished;
                throw new SnapshotConflictException(planId);
            }

            var live = await LiveRouteOverviewQuery.ExecuteAsync(db, window, ct);
            db.PublishedPlans.Add(new PublishedPlan
            {
                Id = planId,
                WindowFromUtc = window.FromUtc,
                WindowToUtc = window.ToUtc,
                PublishedAtUtc = UtcTime.NowToMicroseconds(clock),
                Rows = live.Select(r => new PublishedPlanRow
                {
                    PlanId = planId,
                    RouteId = r.RouteId,
                    RouteName = r.RouteName,
                    DepartureCount = r.ScheduledTripCount,
                    NextDepartureUtc = r.NextDepartureUtc
                }).ToList()
            });
            return PublishResult.Published;
        }, ct, hooks.BeforePublishCommit);
    }
}
