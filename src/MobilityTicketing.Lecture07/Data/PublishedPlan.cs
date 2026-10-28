namespace MobilityTicketing.Lecture07.Data;

public class PublishedPlan
{
    public string Id { get; set; } = "";
    public DateTime WindowFromUtc { get; set; }
    public DateTime WindowToUtc { get; set; }
    public DateTime PublishedAtUtc { get; set; }
    public virtual List<PublishedPlanRow> Rows { get; set; } = new();
}

public class PublishedPlanRow
{
    public string PlanId { get; set; } = "";
    public string RouteId { get; set; } = "";
    public string RouteName { get; set; } = "";
    public int DepartureCount { get; set; }
    public DateTime? NextDepartureUtc { get; set; }
}
