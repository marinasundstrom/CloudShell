namespace CloudShell.Hosting.Observability;

public sealed class MetricVisualizationOptions
{
    public const string SectionName = "Observability:Metrics";

    public List<ResourceMetricVisualizationOptions> Resources { get; set; } = [];
}

public sealed class ResourceMetricVisualizationOptions
{
    public string? ResourceId { get; set; }

    public string? ResourceName { get; set; }

    public List<MetricPanelOptions> Panels { get; set; } = [];
}

public sealed class MetricPanelOptions
{
    public string? Title { get; set; }

    public string? Description { get; set; }

    public string? MetricName { get; set; }

    public MetricPanelKind Kind { get; set; } = MetricPanelKind.Line;

    public MetricPanelAggregation Aggregation { get; set; } = MetricPanelAggregation.Latest;

    public string? Unit { get; set; }

    public int MaxPoints { get; set; } = 60;

    public Dictionary<string, string> AttributeFilters { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

public enum MetricPanelKind
{
    Line,
    Indicator
}

public enum MetricPanelAggregation
{
    Latest,
    Average,
    Sum,
    Min,
    Max,
    Count
}
