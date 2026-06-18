using CloudShell.Abstractions.ResourceManager;
using CloudShell.Hosting.ResourceManager;

namespace CloudShell.Abstractions.Tests;

public sealed class NameMappingAuthoringConflictTests
{
    [Fact]
    public void FindDuplicate_DetectsSameZoneHostAndExposure()
    {
        var existing = CreateNameMapping(
            "dns:local:name:api-local",
            "dns:local",
            "API mapping",
            "API.LOCAL",
            ResourceExposureScope.Public);

        var conflict = NameMappingAuthoringConflicts.FindDuplicate(
            [existing],
            "dns:local",
            "api.local",
            ResourceExposureScope.Public);

        Assert.NotNull(conflict);
        Assert.Equal(existing, conflict.Resource);
        Assert.Equal(
            "DNS zone already has a name mapping for host 'api.local' in exposure scope 'Public': API mapping.",
            conflict.Message);
    }

    [Fact]
    public void FindDuplicate_IgnoresCurrentMappingDuringUpdate()
    {
        var existing = CreateNameMapping(
            "dns:local:name:api-local",
            "dns:local",
            "API mapping",
            "api.local",
            ResourceExposureScope.Public);

        var conflict = NameMappingAuthoringConflicts.FindDuplicate(
            [existing],
            "dns:local",
            "api.local",
            ResourceExposureScope.Public,
            "dns:local:name:api-local");

        Assert.Null(conflict);
    }

    [Fact]
    public void FindDuplicate_IgnoresDifferentExposure()
    {
        var existing = CreateNameMapping(
            "dns:local:name:api-local",
            "dns:local",
            "API mapping",
            "api.local",
            ResourceExposureScope.Private);

        var conflict = NameMappingAuthoringConflicts.FindDuplicate(
            [existing],
            "dns:local",
            "api.local",
            ResourceExposureScope.Public);

        Assert.Null(conflict);
    }

    private static Resource CreateNameMapping(
        string id,
        string zoneResourceId,
        string name,
        string hostName,
        ResourceExposureScope exposure) =>
        new(
            id,
            name,
            "Name mapping",
            "CloudShell",
            "local",
            ResourceState.Running,
            [],
            hostName,
            DateTimeOffset.UtcNow,
            [],
            ParentResourceId: zoneResourceId,
            TypeId: "cloudshell.nameMapping",
            ResourceClass: ResourceClass.Network,
            Attributes: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [ResourceAttributeNames.NameMappingHostName] = hostName,
                [ResourceAttributeNames.NameMappingExposure] = exposure.ToString()
            },
            Capabilities: [new(ResourceCapabilityIds.NetworkingNameMapping)]);
}
