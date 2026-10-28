using Microsoft.EntityFrameworkCore;
using MobilityTicketing.Lecture07.Contracts;
using MobilityTicketing.Lecture07.Data;

namespace MobilityTicketing.Lecture07.Commands;

// Keyed by request ID so retries wait for each other. The request is checked before the trip,
// so a retry returns the original receipt even after a later cancellation.
public static class BookTripCommand
{
    public static Task<BookingResult> ExecuteAsync(string connection, TimeProvider clock, string tripId, string requestId,
        CancellationToken ct = default)
    {
        DatabaseSession.RequireText(tripId, "Trip ID");
        DatabaseSession.RequireText(requestId, "Request ID");
        return DatabaseSession.WriteAsync(connection, "request:" + requestId, async db =>
        {
            var existing = await db.Bookings.SingleOrDefaultAsync(b => b.RequestId == requestId, ct);
            if (existing is not null)
                return existing.TripId == tripId
                    ? new BookingResult(BookingStatus.AlreadyBooked, ToReceipt(existing))
                    : new BookingResult(BookingStatus.RequestConflict);

            var trip = await DatabaseSession.LockTripAsync(db, tripId, ct);
            if (trip is null) return new BookingResult(BookingStatus.NotFound);
            if (trip.Status != "Scheduled") return new BookingResult(BookingStatus.Cancelled);

            var booking = new Booking { RequestId = requestId, TripId = tripId, BookedAtUtc = UtcTime.NowToMicroseconds(clock) };
            db.Bookings.Add(booking);
            return new BookingResult(BookingStatus.Booked, ToReceipt(booking));
        }, ct);
    }

    static BookingReceipt ToReceipt(Booking b) => new(b.RequestId, b.TripId, b.BookedAtUtc);
}
