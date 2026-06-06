namespace CloudShell.Providers.Docker;

public sealed record DockerConnectionStatus(
    Uri Endpoint,
    bool IsConnected,
    string? Error,
    DateTimeOffset LastChecked);
