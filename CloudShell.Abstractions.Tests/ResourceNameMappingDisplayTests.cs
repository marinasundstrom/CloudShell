using CloudShell.Abstractions.ResourceManager;
using CloudShell.Components;

namespace CloudShell.Abstractions.Tests;

public sealed class ResourceNameMappingDisplayTests
{
    [Fact]
    public void GetMappedEndpointAddress_DoesNotFallbackWhenExplicitTargetEndpointIsMissing()
    {
        var mapping = CreateNameMapping("api.local", "missing");
        var target = CreateTargetResource();

        var address = ResourceNameMappingDisplay.GetMappedEndpointAddress(mapping, target);

        Assert.Null(address);
    }

    [Fact]
    public void GetMappedEndpointAddress_FallsBackToFirstEndpointWhenTargetEndpointIsImplicit()
    {
        var mapping = CreateNameMapping("api.local", targetEndpointName: null);
        var target = CreateTargetResource();

        var address = ResourceNameMappingDisplay.GetMappedEndpointAddress(mapping, target);

        Assert.Equal("http://api.local:5080/", address);
    }

    [Fact]
    public void GetLocalHostNamePublishingMetadata_FormatsProviderRuntimeState()
    {
        var mapping = CreateNameMapping(
            "api.local",
            "http",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [ResourceAttributeNames.NameMappingLocalHostNamesHostsFilePath] = "/tmp/cloudshell-hosts",
                [ResourceAttributeNames.NameMappingLocalHostNamesHostsFileTarget] = "Custom",
                [ResourceAttributeNames.NameMappingLocalHostNamesResolverRefreshStatus] = "Skipped"
            });

        var metadata = ResourceNameMappingDisplay.GetLocalHostNamePublishingMetadata(
            mapping,
            static value => value);

        Assert.Equal(
            [
                "Hosts file: custom (/tmp/cloudshell-hosts)",
                "Resolver: not refreshed"
            ],
            metadata);
    }

    private static Resource CreateNameMapping(
        string hostName,
        string? targetEndpointName,
        IReadOnlyDictionary<string, string>? additionalAttributes = null)
    {
        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [ResourceAttributeNames.NameMappingHostName] = hostName,
            [ResourceAttributeNames.NameMappingTargetResourceId] = "application:api"
        };
        if (!string.IsNullOrWhiteSpace(targetEndpointName))
        {
            attributes[ResourceAttributeNames.NameMappingTargetEndpointName] = targetEndpointName;
        }

        if (additionalAttributes is not null)
        {
            foreach (var (name, value) in additionalAttributes)
            {
                attributes[name] = value;
            }
        }

        return new Resource(
            "cloudshell.nameMapping:api-local",
            "api-local",
            "cloudshell.nameMapping",
            "resource-model",
            "local",
            null,
            [],
            "1",
            DateTimeOffset.UtcNow,
            [],
            TypeId: "cloudshell.nameMapping",
            ResourceClass: ResourceClass.Network,
            Attributes: attributes,
            Capabilities: [new(ResourceCapabilityIds.NetworkingNameMapping)]);
    }

    private static Resource CreateTargetResource() =>
        new(
            "application:api",
            "api",
            "application",
            "resource-model",
            "local",
            ResourceState.Running,
            [ResourceEndpoint.Contract("http", "http", targetPort: 8080)],
            "1",
            DateTimeOffset.UtcNow,
            [],
            EndpointNetworkMappings:
            [
                ResourceEndpointNetworkMapping.ForEndpoint(
                    "application:api",
                    "http",
                    "http://localhost:5080",
                    ResourceExposureScope.Public)
            ]);
}
