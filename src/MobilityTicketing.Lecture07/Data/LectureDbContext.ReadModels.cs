using Microsoft.EntityFrameworkCore;

namespace MobilityTicketing.Lecture07.Data;

public partial class LectureDbContext
{
    public DbSet<PublishedPlan> PublishedPlans => Set<PublishedPlan>();

    partial void ConfigureReadModels(ModelBuilder b)
    {
        b.Entity<PublishedPlan>(e =>
        {
            e.ToTable("published_plans");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.WindowFromUtc).HasColumnName("window_from_utc");
            e.Property(x => x.WindowToUtc).HasColumnName("window_to_utc");
            e.Property(x => x.PublishedAtUtc).HasColumnName("published_at_utc");
            e.HasMany(x => x.Rows).WithOne().HasForeignKey(x => x.PlanId);
        });
        b.Entity<PublishedPlanRow>(e =>
        {
            e.ToTable("published_plan_rows");
            e.HasKey(x => new { x.PlanId, x.RouteId });
            e.Property(x => x.PlanId).HasColumnName("plan_id");
            e.Property(x => x.RouteId).HasColumnName("route_id");
            e.Property(x => x.RouteName).HasColumnName("route_name");
            e.Property(x => x.DepartureCount).HasColumnName("departure_count");
            e.Property(x => x.NextDepartureUtc).HasColumnName("next_departure_utc");
        });
    }
}
