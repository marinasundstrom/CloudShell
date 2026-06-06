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
    string? Description = null);

public enum LogSourceKind
{
    Resource,
    Artifact,
    Provider,
    Other
}
