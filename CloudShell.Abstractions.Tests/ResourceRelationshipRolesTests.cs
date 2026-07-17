using CloudShell.Abstractions.ResourceManager;
using CloudShell.Hosting.ResourceManager;

namespace CloudShell.Abstractions.Tests;

public sealed class ResourceRelationshipRolesTests
{
    [Theory]
    [InlineData(ResourceClass.Project, "Application workload")]
    [InlineData(ResourceClass.Configuration, "Configuration service")]
    [InlineData(ResourceClass.SecretsVault, "Secrets vault")]
    [InlineData(ResourceClass.Network, "Network resource")]
    public void GetRole_UsesResourceClassForCommonApplicationTopologyResources(
        ResourceClass resourceClass,
        string expectedRole)
    {
        var resource = CreateResource(resourceClass);

        Assert.Equal(expectedRole, ResourceRelationshipRoles.GetRole(resource));
    }

    [Fact]
    public void GetRole_UsesCapabilityForLoadBalancerResources()
    {
        var resource = CreateResource(
            ResourceClass.Network,
            capabilities: [new ResourceCapability(ResourceCapabilityIds.NetworkingLoadBalancer)]);

        Assert.Equal("Exposure resource", ResourceRelationshipRoles.GetRole(resource));
    }

    [Fact]
    public void GetRole_UsesInfrastructureKindForIdentityProvisioningResources()
    {
        var resource = CreateResource(
            ResourceClass.Infrastructure,
            attributes: new Dictionary<string, string>
            {
                [ResourceAttributeNames.InfrastructureKind] = "identity-provisioning"
            });

        Assert.Equal("Identity provider boundary", ResourceRelationshipRoles.GetRole(resource));
    }

    private static Resource CreateResource(
        ResourceClass resourceClass,
        IReadOnlyDictionary<string, string>? attributes = null,
        IReadOnlyList<ResourceCapability>? capabilities = null) =>
        new(
            $"test:{resourceClass.ToString().ToLowerInvariant()}",
            resourceClass.ToString().ToLowerInvariant(),
            "Test",
            "test",
            "local",
            ResourceState.Running,
            [],
            "1.0",
            DateTimeOffset.UtcNow,
            [],
            ResourceClass: resourceClass,
            Attributes: attributes,
            Capabilities: capabilities);
}
