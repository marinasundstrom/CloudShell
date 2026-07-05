namespace CloudShell.DeviceRegistry.Client;

public sealed record DeviceTwinResponse(
    DeviceTwinState Desired,
    DeviceTwinState Reported,
    DateTimeOffset? LastSyncedAt);
