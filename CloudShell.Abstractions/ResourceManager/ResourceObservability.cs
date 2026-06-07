namespace CloudShell.Abstractions.ResourceManager;

public sealed record ResourceObservability(
    bool Logs = false,
    bool Traces = false,
    bool Metrics = false,
    string? OtlpEndpoint = null,
    string? OtlpProtocol = null,
    string? OtlpHeaders = null,
    string? ServiceName = null,
    IReadOnlyDictionary<string, string>? ResourceAttributes = null)
{
    public static ResourceObservability None { get; } = new();

    public static ResourceObservability Default { get; } = new(
        Logs: true,
        Traces: true,
        Metrics: true);

    public bool HasAnySignal => Logs || Traces || Metrics;

    public IReadOnlyDictionary<string, string> Attributes => ResourceAttributes ?? EmptyAttributes;

    private static readonly IReadOnlyDictionary<string, string> EmptyAttributes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}
