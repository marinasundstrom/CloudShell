using System.Text.Json;

namespace CloudShell.DeviceRegistry.Client;

public sealed record DeviceTwinState
{
    public long Version { get; init; }

    public DateTimeOffset? UpdatedAt { get; init; }

    public IReadOnlyDictionary<string, JsonElement> State { get; init; } =
        new Dictionary<string, JsonElement>();
}
