namespace CloudShell.DeviceRegistry.Client;

public sealed record DeviceRevokeRequest(
    string? Reason = null);
