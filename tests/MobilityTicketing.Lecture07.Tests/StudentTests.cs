using Xunit;
using MobilityTicketing.Lecture07.Contracts;
using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;

namespace MobilityTicketing.Lecture07.Tests;

[Trait("Category", "Student")]
public class StudentTests : Fixture
{
    [Fact]
    public async Task Concurrent_publication_of_different_plans_keeps_each_window_and_rows()
    {
        var later = new TimeWindow(Utc(7, 0), Utc(8, 0));

        var results = await Task.WhenAll(
            App.PublishSnapshotAsync("morning", TimeWindow.Morning),
            App.PublishSnapshotAsync("later", later));

        Assert.All(results, r => Assert.Equal(PublishResult.Published, r));

        var morning = await App.ReadSnapshotAsync("morning");
        Assert.Equal(TimeWindow.Morning, morning.Window);
        AssertRows(Baseline, morning.Routes);

        var laterPlan = await App.ReadSnapshotAsync("later");
        Assert.Equal(later, laterPlan.Window);
        Assert.Equal(4, laterPlan.Routes.Count);
        Assert.Equal(1, laterPlan.Routes[0].ScheduledTripCount);
        Assert.Equal(Utc(7, 0), laterPlan.Routes[0].NextDepartureUtc);
        Assert.All(laterPlan.Routes.Skip(1), r => Assert.Equal(0, r.ScheduledTripCount));
    }

    [Fact]
    public async Task A_new_plan_after_cancel_and_rename_records_the_changed_values()
    {
        // Given
        await App.PublishSnapshotAsync("before", TimeWindow.Morning);

        // When: 
        await App.CancelTripAsync("T102");
        await App.RenameRouteAsync("R1", "City Ring");
        Clock.Now = Clock.Now.AddMinutes(10);
        await App.PublishSnapshotAsync("after", TimeWindow.Morning);

        // Then (after):  
        var after = await App.ReadSnapshotAsync("after");
        RouteOverviewDto[] expected = [
            new("R1", "City Ring", 1, Utc(06, 20)),
            Baseline[1], Baseline[2], Baseline[3]
        ];
        AssertRows(expected, after.Routes);
        Assert.Equal(Utc(06, 05), after.PublishedAtUtc);
        // Then (before): stadig præcis Baseline
        var before = await App.ReadSnapshotAsync("before");
        AssertRows(Baseline, before.Routes);

    }

    [Fact]
    public async Task Concurrent_bookings_with_different_request_ids_on_the_same_trip_both_succeed()
    {
        var results = await Task.WhenAll(
            App.BookTripAsync("T102", "passenger-a"),
            App.BookTripAsync("T102", "passenger-b"));

        Assert.All(results, r => Assert.Equal(BookingStatus.Booked, r.Status));
        Assert.Equal(
            new[] { "passenger-a", "passenger-b" },
            results.Select(r => r.Receipt!.RequestId).OrderBy(id => id));
        Assert.All(results, r => Assert.Equal("T102", r.Receipt!.TripId));

        await using var db = Db();
        Assert.Equal(2, await db.Bookings.CountAsync(b => b.TripId == "T102"));
    }

    [Fact]
    public async Task Window_of_exactly_24_hours_is_accepted_but_one_microsecond_more_is_rejected()
    {
        var from = Utc(6, 0);
        var fullDay = new TimeWindow(from, from.AddDays(1));
        var tooLong = new TimeWindow(from, from.AddDays(1).AddMicroseconds(1));

        Assert.Equal(PublishResult.Published, await App.PublishSnapshotAsync("full-day", fullDay));

        var before = await State();
        await Assert.ThrowsAsync<ArgumentException>(() => App.PublishSnapshotAsync("too-long", tooLong));
        Assert.Equal(before, await State());
    }
}


