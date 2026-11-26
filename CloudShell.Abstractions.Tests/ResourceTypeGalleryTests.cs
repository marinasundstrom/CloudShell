using System.Text.Json;
using CloudShell.Components;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.Hosting.ResourceManager;
using CloudShell.Hosting.Components.Pages.Resources;

namespace CloudShell.Abstractions.Tests;

public sealed class ResourceTypeGalleryTests
{
    [Fact]
    public async Task NewGroupIsDeferredUntilSubmissionAndRetryUsesTheCreatedGroup()
    {
        var calls = 0;
        var draft = new ResourceRegistrationGroupDraft { CreateNew = true, Name = "  UX project  " };
        Assert.Null(draft.Validate());
        Assert.Equal(0, calls);
        var id = await draft.ResolveAsync(null, command =>
        {
            calls++;
            Assert.Equal("UX project", command.Name);
            return Task.FromResult(new ResourceGroup("group-1", command.Name, command.Description, []));
        });
        Assert.Equal("group-1", id);
        Assert.False(draft.CreateNew);
        Assert.Equal(id, await draft.ResolveAsync(id, _ => throw new InvalidOperationException("Must not create twice")));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task DefaultGroupRequiresNoCreationAndNewGroupRequiresAName()
    {
        var draft = new ResourceRegistrationGroupDraft();
        Assert.Null(await draft.ResolveAsync("", _ => throw new InvalidOperationException("Default group is implicit")));
        draft.CreateNew = true;
        Assert.NotNull(draft.Validate());
        await Assert.ThrowsAsync<InvalidOperationException>(() => draft.ResolveAsync(null, _ => throw new Exception("Must not submit")));
    }

    [Fact]
    public void SearchCombinesCategoryAndAllSearchTermsAcrossProviderMetadata()
    {
        ResourceTypeContribution[] types =
        [
            new("vendor.queue", "Message Queue", "Durable messaging", "queue", 2, typeof(object), ResourceClass: ResourceClass.Service),
            new("vendor.disk", "Queue archive", "Durable storage", "storage", 1, typeof(object), ResourceClass: ResourceClass.Storage),
            new("other.queue", "Message Queue", "Ephemeral messaging", "queue", 3, typeof(object), ResourceClass: ResourceClass.Service)
        ];
        Assert.Equal("vendor.queue", Assert.Single(ResourceTypeGallery.Filter(types, ResourceClass.Service, "  DURABLE queue ")).Id);
        Assert.Equal(2, ResourceTypeGallery.Filter(types, null, "vendor").Count);
        Assert.Empty(ResourceTypeGallery.Filter(types, ResourceClass.Network, "queue"));
        Assert.Equal("vendor.disk", ResourceTypeGallery.Filter(types, null, "")[0].Id);
    }

    [Fact]
    public void CriticalRegistrationValuesHaveNoExampleDefaults()
    {
        var dns = new RegisterDnsZoneResource.RegistrationDraft();
        Assert.Empty(dns.ResourceId);
        Assert.Empty(dns.ZoneName);
        Assert.Empty(dns.HostName);
        Assert.Empty(dns.TargetResourceId);
        Assert.Empty(new RegisterNameMappingResource.RegistrationDraft().HostName);
        Assert.Empty(new RegisterLoadBalancerResource.RegistrationDraft().HttpHost);
    }

    [Fact]
    public void RegistrationDraftRoundTripPreservesSelectionsAndEndpointChoices()
    {
        var draft = new RegisterServiceResource.RegistrationDraft
        {
            ResourceId = "orders", SelectedGroupId = "team", SelectedTargetIds = ["app:api", "app:worker"],
            SelectedNetworkIds = ["network:private"], EndpointAssignment = ResourceEndpointAssignment.Manual,
            Port = 8080, TargetPort = 80, Protocol = "http"
        };
        var restored = JsonSerializer.Deserialize<RegisterServiceResource.RegistrationDraft>(JsonSerializer.Serialize(draft))!;
        Assert.Equal(draft.ResourceId, restored.ResourceId);
        Assert.Equal(draft.SelectedGroupId, restored.SelectedGroupId);
        Assert.True(draft.SelectedTargetIds.SetEquals(restored.SelectedTargetIds));
        Assert.True(draft.SelectedNetworkIds.SetEquals(restored.SelectedNetworkIds));
        Assert.Equal(ResourceEndpointAssignment.Manual, restored.EndpointAssignment);
        Assert.Equal(8080, restored.Port);
        Assert.Equal(80, restored.TargetPort);
        Assert.Equal("http", restored.Protocol);
    }
}
