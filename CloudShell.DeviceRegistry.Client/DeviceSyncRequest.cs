using System.Text.Json;

namespace CloudShell.DeviceRegistry.Client;

public sealed record DeviceSyncRequest(
    IReadOnlyDictionary<string, JsonElement>? ReportedState = null,
    IReadOnlyDictionary<string, string>? Properties = null,
    string? Source = null,
    long? LastKnownDesiredVersion = null);
