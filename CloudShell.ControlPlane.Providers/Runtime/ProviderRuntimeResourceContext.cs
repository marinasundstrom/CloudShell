namespace CloudShell.ControlPlane.Providers;

public sealed record ProviderRuntimeResourceContext(
    string ResourceId,
    string Name,
    string DisplayName,
    string? Endpoint);
