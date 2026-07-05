namespace CloudShell.DeviceRegistryService;

public sealed class DeviceRegistryServiceOptions
{
    public const string SectionName = "CloudShell:DeviceRegistryService";

    public string DefinitionsPath { get; set; } = "Data/device-registries.json";

    public string? DevicesPath { get; set; }

    public string? ResourceId { get; set; }

    public string? MqttEndpoint { get; set; }
}
