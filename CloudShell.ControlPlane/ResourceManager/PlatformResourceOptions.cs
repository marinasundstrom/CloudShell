using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.ControlPlane.ResourceManager;

public sealed class PlatformResourceOptions
{
    public string DefinitionsPath { get; set; } = "Data/platform-resources.json";

    public int AutoLocalPortStart { get; set; } = 20000;

    public int AutoLocalPortEnd { get; set; } = 29999;

    public List<DeclaredNetworkResource> DeclaredNetworks { get; } = [];

    public List<DeclaredServiceResource> DeclaredServices { get; } = [];
}

public sealed class DeclaredNetworkResource(NetworkResourceDefinition definition)
{
    public NetworkResourceDefinition Definition { get; set; } = definition;

    public bool Persist { get; set; }

    public bool OverwritePersistedState { get; set; }
}

public sealed class DeclaredServiceResource(ServiceResourceDefinition definition)
{
    public ServiceResourceDefinition Definition { get; set; } = definition;

    public bool Persist { get; set; }

    public bool OverwritePersistedState { get; set; }
}
