using Microsoft.EntityFrameworkCore;
using MobilityTicketing.Lecture07.Contracts;
using MobilityTicketing.Lecture07.Data;

namespace MobilityTicketing.Lecture07.Commands;

// Only the source row changes; the live board picks it up on the next read.
public static class RenameRouteCommand
{
    public static Task<RenameResult> ExecuteAsync(string connection, string routeId, string name, CancellationToken ct = default)
    {
        DatabaseSession.RequireText(routeId, "Route ID");
        DatabaseSession.RequireText(name, "Route name");
        var trimmed = name.Trim();
        return DatabaseSession.WriteAsync(connection, "route:" + routeId, async db =>
        {
            var route = await db.Routes.SingleOrDefaultAsync(r => r.Id == routeId, ct);
            if (route is null) return RenameResult.NotFound;
            route.Name = trimmed;
            return RenameResult.Renamed;
        }, ct);
    }
}
