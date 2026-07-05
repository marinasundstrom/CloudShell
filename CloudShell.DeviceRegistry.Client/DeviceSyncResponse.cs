namespace CloudShell.DeviceRegistry.Client;

public sealed record DeviceSyncResponse(
    DeviceMetadataResponse Device,
    DeviceTwinState Desired,
    DeviceTwinState Reported,
    bool DesiredStateChanged,
    DateTimeOffset? LastSyncedAt);
