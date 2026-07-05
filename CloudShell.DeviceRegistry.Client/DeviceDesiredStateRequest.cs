using System.Text.Json;

namespace CloudShell.DeviceRegistry.Client;

public sealed record DeviceDesiredStateRequest(
    IReadOnlyDictionary<string, JsonElement>? State = null);
