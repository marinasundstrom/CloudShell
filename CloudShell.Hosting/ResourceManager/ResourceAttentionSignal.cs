using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Hosting.ResourceManager;

public sealed record ResourceAttentionSignal(
    ResourceSignalSeverity Severity,
    string Tooltip);
