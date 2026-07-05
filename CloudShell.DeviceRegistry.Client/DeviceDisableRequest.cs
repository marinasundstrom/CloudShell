namespace CloudShell.DeviceRegistry.Client;

public sealed record DeviceDisableRequest(
    string? Reason = null);
