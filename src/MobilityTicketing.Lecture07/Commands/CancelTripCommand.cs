using MobilityTicketing.Lecture07.Contracts;
using MobilityTicketing.Lecture07.Data;

namespace MobilityTicketing.Lecture07.Commands;

// Locks the trip row so a concurrent booking sees the new status. The row is kept.
public static class CancelTripCommand
{
    public static Task<CancelResult> ExecuteAsync(string connection, string tripId, CancellationToken ct = default)
    {
        DatabaseSession.RequireText(tripId, "Trip ID");
        return DatabaseSession.WriteAsync(connection, "trip:" + tripId, async db =>
        {
            var trip = await DatabaseSession.LockTripAsync(db, tripId, ct);
            if (trip is null) return CancelResult.NotFound;
            if (trip.Status == "Cancelled") return CancelResult.AlreadyCancelled;
            trip.Status = "Cancelled";
            return CancelResult.Cancelled;
        }, ct);
    }
}
