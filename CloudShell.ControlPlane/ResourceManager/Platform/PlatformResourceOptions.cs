using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.ResourceManager.Networking;

namespace CloudShell.ControlPlane.ResourceManager.Platform;

public sealed class PlatformResourceOptions
{
    public string DefinitionsPath { get; set; } = "Data/platform-resources.json";

    public int AutoLocalPortStart { get; set; } = 20000;

    public int AutoLocalPortEnd { get; set; } = 29999;

    public string LocalHostNameProviderName { get; set; } =
        LocalHostNamePublishingProvider.DefaultProviderName;

    public string? LocalHostNameHostsFilePath { get; set; }

    public string LocalHostNameDefaultAddress { get; set; } = "127.0.0.1";

    public LocalHostNameResolverRefreshMode LocalHostNameResolverRefreshMode { get; set; } =
        LocalHostNameResolverRefreshMode.BestEffort;

    public List<DeclaredNetworkResource> DeclaredNetworks { get; } = [];

    public List<DeclaredServiceResource> DeclaredServices { get; } = [];

    public List<DeclaredVolumeResource> DeclaredVolumes { get; } = [];

    public List<DeclaredStorageResource> DeclaredStorages { get; } = [];

    public List<DeclaredLoadBalancerResource> DeclaredLoadBalancers { get; } = [];

    public List<DeclaredDnsZoneResource> DeclaredDnsZones { get; } = [];
}

public enum LocalHostNameResolverRefreshMode
{
    Disabled,
    BestEffort
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

public sealed class DeclaredVolumeResource(VolumeResourceDefinition definition)
{
    public VolumeResourceDefinition Definition { get; set; } = definition;

    public bool Persist { get; set; }

    public bool OverwritePersistedState { get; set; }
}

public sealed class DeclaredStorageResource(StorageResourceDefinition definition)
{
    public StorageResourceDefinition Definition { get; set; } = definition;

    public bool Persist { get; set; }

    public bool OverwritePersistedState { get; set; }
}

public sealed class DeclaredLoadBalancerResource(LoadBalancerResourceDefinition definition)
{
    public LoadBalancerResourceDefinition Definition { get; set; } = definition;

    public bool Persist { get; set; }

    public bool OverwritePersistedState { get; set; }
}

public sealed class DeclaredDnsZoneResource(DnsZoneResourceDefinition definition)
{
    public DnsZoneResourceDefinition Definition { get; set; } = definition;

    public bool Persist { get; set; }

    public bool OverwritePersistedState { get; set; }
}
