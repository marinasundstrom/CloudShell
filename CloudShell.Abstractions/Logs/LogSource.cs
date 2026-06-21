namespace CloudShell.Abstractions.Logs;

public sealed record ResourceLogSource(
    string Id,
    string Name,
    ResourceLogSourceKind Kind,
    LogFormat Format = LogFormat.PlainText,
    LogStorage Storage = default,
    LogSourceCapabilities Capabilities = LogSourceCapabilities.Read,
    string? Location = null,
    string? ProducerResourceId = null,
    string? Description = null,
    ResourceLogSourceOrigin Origin = ResourceLogSourceOrigin.ProviderDefault,
    LogSourceConfiguration Configuration = default,
    ResourceLogSourcePurpose Purpose = ResourceLogSourcePurpose.Discovery,
    LogSourceAvailability Availability = LogSourceAvailability.Unknown);

public sealed record LogSource(
    string Id,
    string Name,
    string Provider,
    string SourceName,
    LogSourceKind SourceKind,
    ResourceLogSourceKind Kind = ResourceLogSourceKind.ProviderDefined,
    LogFormat Format = LogFormat.PlainText,
    LogStorage Storage = default,
    LogSourceCapabilities Capabilities = LogSourceCapabilities.Read,
    string? ResourceId = null,
    string? ArtifactId = null,
    string? Location = null,
    string? ProducerResourceId = null,
    string? Description = null,
    ResourceLogSourceOrigin Origin = ResourceLogSourceOrigin.ProviderDefault,
    LogSourceConfiguration Configuration = default,
    ResourceLogSourcePurpose Purpose = ResourceLogSourcePurpose.Discovery,
    LogSourceAvailability Availability = LogSourceAvailability.Unknown)
{
    public bool SupportsStreaming => Capabilities.HasFlag(LogSourceCapabilities.Stream);

    public static LogSource FromDescriptor(LogDescriptor descriptor) =>
        descriptor.ToLogSource();
}

public enum ResourceLogSourceOrigin
{
    ProviderDefault,
    UserConfigured,
    Programmatic,
    ProviderProjected,
    RuntimeDiscovered
}

public readonly record struct LogSourceConfiguration(
    bool IsConfigurable,
    string? SchemaId = null);

public enum ResourceLogSourcePurpose
{
    Discovery,
    Default,
    Custom
}

public enum LogSourceAvailability
{
    Unknown,
    ResourceRunning,
    ProducerRunning,
    Persisted,
    Always,
    ProviderDefined
}

public enum ResourceLogSourceKind
{
    ProcessOutput,
    ProcessStdout,
    ProcessStderr,
    File,
    FilePattern,
    Container,
    External,
    Activity,
    ProviderDefined,
    Other
}

public enum LogFormat
{
    PlainText,
    JsonConsole,
    SerilogCompactJson,
    OpenTelemetry,
    Structured,
    ResourceEvent,
    ProviderDefined
}

public readonly record struct LogStorage(LogStorageKind Kind)
{
    public static LogStorage InMemory { get; } = new(LogStorageKind.InMemory);
}

public enum LogStorageKind
{
    InMemory,
    File,
    Database,
    Remote,
    ProviderDefined
}

[Flags]
public enum LogSourceCapabilities
{
    None = 0,
    Read = 1,
    Stream = 2,
    Query = 4,
    Search = 8,
    StructuredFields = 16
}
