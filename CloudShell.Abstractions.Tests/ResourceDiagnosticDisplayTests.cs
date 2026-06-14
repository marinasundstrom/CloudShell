using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.ResourceManager;
using CloudShell.Hosting.ResourceManager;

namespace CloudShell.Abstractions.Tests;

public sealed class ResourceDiagnosticDisplayTests
{
    [Fact]
    public void GetDiagnostics_WarnsWhenNameMappingPublisherResourceIsMissing()
    {
        var mapping = CreateNameMapping("networking:missing");

        var diagnostics = ResourceDiagnosticDisplay.GetDiagnostics(
            mapping,
            new Dictionary<string, Resource>(StringComparer.OrdinalIgnoreCase));

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("Warning", diagnostic.Severity);
        Assert.Equal("DNS publisher unavailable", diagnostic.Title);
        Assert.Equal(
            "Provider resource 'networking:missing' could not be found. CloudShell cannot verify that this name mapping can be published.",
            diagnostic.Message);
    }

    [Fact]
    public void GetDiagnostics_WarnsWhenNameMappingPublisherDoesNotAdvertiseCapability()
    {
        var mapping = CreateNameMapping("networking:resolver");
        var provider = new Resource(
            "networking:resolver",
            "Resolver",
            "Network provider",
            "CloudShell",
            "logical",
            ResourceState.Running,
            [],
            "resolver",
            DateTimeOffset.UtcNow,
            [],
            Capabilities: [new(ResourceCapabilityIds.NetworkingNameResolver)]);

        var diagnostics = ResourceDiagnosticDisplay.GetDiagnostics(
            mapping,
            new Dictionary<string, Resource>(StringComparer.OrdinalIgnoreCase)
            {
                [provider.Id] = provider
            });

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("Warning", diagnostic.Severity);
        Assert.Equal("DNS publisher capability missing", diagnostic.Title);
        Assert.Equal(
            "Provider resource 'Resolver' does not advertise the DNS name publisher capability.",
            diagnostic.Message);
    }

    [Fact]
    public void GetDiagnostics_DoesNotWarnWhenNameMappingPublisherAdvertisesCapability()
    {
        var mapping = CreateNameMapping("networking:publisher");
        var provider = new Resource(
            "networking:publisher",
            "Publisher",
            "Network provider",
            "CloudShell",
            "logical",
            ResourceState.Running,
            [],
            "publisher",
            DateTimeOffset.UtcNow,
            [],
            Capabilities: [new(ResourceCapabilityIds.NetworkingNamePublisher)]);

        var diagnostics = ResourceDiagnosticDisplay.GetDiagnostics(
            mapping,
            new Dictionary<string, Resource>(StringComparer.OrdinalIgnoreCase)
            {
                [provider.Id] = provider
            });

        Assert.Empty(diagnostics);
    }

    private static Resource CreateNameMapping(string providerResourceId) =>
        new(
            "dns:local:name:api-local",
            "api.local",
            "Name Mapping",
            "CloudShell",
            "logical",
            ResourceState.Running,
            [],
            "api.local",
            DateTimeOffset.UtcNow,
            [providerResourceId],
            TypeId: PlatformResourceProvider.NameMappingResourceType,
            ResourceClass: ResourceClass.Network,
            Attributes: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [ResourceAttributeNames.NameMappingHostName] = "api.local",
                [ResourceAttributeNames.NameMappingTargetResourceId] = "application:api",
                [ResourceAttributeNames.NameMappingExposure] = ResourceExposureScope.Public.ToString(),
                [ResourceAttributeNames.NameMappingStatus] = "Ready",
                [ResourceAttributeNames.NameMappingMaterializationStatus] = "ProviderSelected",
                [ResourceAttributeNames.NameMappingProviderResourceId] = providerResourceId
            },
            Capabilities: [new(ResourceCapabilityIds.NetworkingNameMapping)]);
}
