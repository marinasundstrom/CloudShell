namespace CloudShell.Abstractions.Logs;

public sealed record LogDescriptor(
    string Id,
    string Name,
    string Provider,
    string SourceName,
    LogSourceKind SourceKind,
    string? ResourceId = null,
    string? ArtifactId = null,
    bool SupportsStreaming = false,
    string? Description = null,
    ResourceLogSourceKind Kind = ResourceLogSourceKind.ProviderDefined,
    LogFormat Format = LogFormat.PlainText,
    LogStorage Storage = default,
    LogSourceCapabilities Capabilities = LogSourceCapabilities.Read,
    string? Location = null,
    string? ProducerResourceId = null,
    ResourceLogSourceOrigin Origin = ResourceLogSourceOrigin.ProviderDefault,
    LogSourceConfiguration Configuration = default,
    ResourceLogSourcePurpose Purpose = ResourceLogSourcePurpose.Discovery,
    LogSourceAvailability Availability = LogSourceAvailability.Unknown)
{
    public LogSource ToLogSource() =>
        new(
            Id,
            Name,
            Provider,
            SourceName,
            SourceKind,
            Kind,
            Format,
            Storage,
            SupportsStreaming
                ? Capabilities | LogSourceCapabilities.Stream
                : Capabilities,
            ResourceId,
            ArtifactId,
            Location,
            ProducerResourceId,
            Description,
            Origin,
            Configuration,
            Purpose,
            Availability);
}

public enum LogSourceKind
{
    Resource,
    Artifact,
    Provider,
    Other
}
