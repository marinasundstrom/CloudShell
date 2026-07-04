namespace CloudShell.DeviceRegistry.Client;

public sealed record DeviceHeartbeatRequest(
    IReadOnlyDictionary<string, string>? Properties = null,
    string? Source = null);
