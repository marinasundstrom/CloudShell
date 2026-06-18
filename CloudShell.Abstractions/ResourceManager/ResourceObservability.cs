using CloudShell.Abstractions.Observability;

namespace CloudShell.Abstractions.ResourceManager;

public sealed record ResourceObservability(
    bool Logs = false,
    bool Traces = false,
    bool Metrics = false,
    string? OtlpEndpoint = null,
    string? OtlpProtocol = null,
    string? OtlpHeaders = null,
    string? ServiceName = null,
    IReadOnlyDictionary<string, string>? ResourceAttributes = null,
    IReadOnlyList<TelemetryScopeDescriptor>? Scopes = null,
    IReadOnlyList<TelemetrySourceDescriptor>? Sources = null)
{
    public static ResourceObservability None { get; } = new();

    public static ResourceObservability Default { get; } = new(
        Logs: true,
        Traces: true,
        Metrics: true);

    public bool HasAnySignal => Logs || Traces || Metrics;

    public bool ServesTelemetry => HasAnySignal || TelemetrySources.Count > 0;

    public IReadOnlyDictionary<string, string> Attributes => ResourceAttributes ?? EmptyAttributes;

    public IReadOnlyList<TelemetrySourceDescriptor> TelemetrySources => Sources ?? [];

    public IReadOnlyList<TelemetryScopeDescriptor> TelemetryScopes =>
        (Scopes ?? [])
        .Concat(TelemetrySources.SelectMany(source => source.SourceScopes))
        .GroupBy(scope => scope.ScopeResourceId, StringComparer.OrdinalIgnoreCase)
        .Select(group => group.Last())
        .ToArray();

    private static readonly IReadOnlyDictionary<string, string> EmptyAttributes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}
