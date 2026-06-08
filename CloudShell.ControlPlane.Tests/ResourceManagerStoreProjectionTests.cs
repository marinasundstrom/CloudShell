using CloudShell.Abstractions.Extensions;
using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.ResourceManager;
using Microsoft.Extensions.DependencyInjection;

namespace CloudShell.ControlPlane.Tests;

public sealed class ResourceManagerStoreProjectionTests
{
    [Fact]
    public void GetResources_IncludesProviderChildrenUnderRegisteredRoot()
    {
        var root = CreateResource("root", "Root");
        var child = CreateResource("child", "Child", parentResourceId: "root");
        var store = CreateStore(
            [root, child],
            registrations: [CreateRegistration("root")]);

        var resources = store.GetResources();

        Assert.Equal(["child", "root"], resources.Select(resource => resource.Id).Order());
        Assert.Equal(["child"], store.GetChildren("root").Select(resource => resource.Id));
    }

    [Fact]
    public void GetResources_AppliesDeclarationParentBeforeVisibilityFiltering()
    {
        var root = CreateResource("root", "Root");
        var declaredChild = CreateResource("declared-child", "Declared Child");
        var declarations = new ResourceDeclarationStore();
        declarations.Declare(
            new TestCloudShellBuilder(),
            "test",
            "declared-child",
            parentResourceId: "root");
        var store = CreateStore(
            [root, declaredChild],
            registrations: [CreateRegistration("root")],
            declarations: declarations);

        var resources = store.GetResources();

        var child = Assert.Single(resources, resource => resource.Id == "declared-child");
        Assert.Equal("root", child.ParentResourceId);
        Assert.Equal(["declared-child"], store.GetChildren("root").Select(resource => resource.Id));
    }

    [Fact]
    public void GetResources_AppliesDeclarationClassAndAttributes()
    {
        var resource = CreateResource(
            "declared",
            "Declared",
            resourceClass: ResourceClass.Generic,
            attributes: new Dictionary<string, string>
            {
                ["provider.attribute"] = "provider-value",
                ["shared.attribute"] = "provider-value"
            });
        var declarations = new ResourceDeclarationStore();
        declarations.Declare(
            new TestCloudShellBuilder(),
            "test",
            "declared",
            resourceClass: ResourceClass.Project,
            attributes: new Dictionary<string, string>
            {
                ["declaration.attribute"] = "declaration-value",
                ["shared.attribute"] = "declaration-value"
            });
        var store = CreateStore(
            [resource],
            registrations: [CreateRegistration("declared")],
            declarations: declarations);

        var projected = Assert.Single(store.GetResources());

        Assert.Equal(ResourceClass.Project, projected.ResourceClass);
        Assert.Equal("provider-value", projected.ResourceAttributes["provider.attribute"]);
        Assert.Equal("declaration-value", projected.ResourceAttributes["declaration.attribute"]);
        Assert.Equal("declaration-value", projected.ResourceAttributes["shared.attribute"]);
    }

    [Fact]
    public void GetGroupForResource_InheritsGroupFromRegisteredParent()
    {
        var group = new ResourceGroup("group-one", "Group One", "Test group", []);
        var root = CreateResource("root", "Root");
        var child = CreateResource("child", "Child", parentResourceId: "root");
        var store = CreateStore(
            [root, child],
            groups: [group],
            registrations: [CreateRegistration("root", group.Id)]);

        var inheritedGroup = store.GetGroupForResource("child");

        Assert.NotNull(inheritedGroup);
        Assert.Equal(group.Id, inheritedGroup.Id);
    }

    [Fact]
    public void GetResources_DoesNotLoopWhenProviderParentGraphCycles()
    {
        var first = CreateResource("first", "First", parentResourceId: "second");
        var second = CreateResource("second", "Second", parentResourceId: "first");
        var store = CreateStore(
            [first, second],
            registrations: [CreateRegistration("first")]);

        var resources = store.GetResources();

        Assert.Equal(["first", "second"], resources.Select(resource => resource.Id).Order());
    }

    private static ResourceManagerStore CreateStore(
        IReadOnlyList<Resource> resources,
        IReadOnlyList<ResourceGroup>? groups = null,
        IReadOnlyList<ResourceRegistration>? registrations = null,
        ResourceDeclarationStore? declarations = null)
    {
        var groupStore = new TestResourceGroupStore(groups ?? []);
        var registrationStore = new TestResourceRegistrationStore(registrations ?? []);
        return new ResourceManagerStore(
            [new TestResourceProvider(resources)],
            groupStore,
            registrationStore,
            declarations ?? new ResourceDeclarationStore(),
            new CloudShellExtensionRegistry(),
            new InMemoryCloudShellExtensionActivationStore());
    }

    private static Resource CreateResource(
        string id,
        string name,
        string? parentResourceId = null,
        ResourceClass resourceClass = ResourceClass.Generic,
        IReadOnlyDictionary<string, string>? attributes = null) =>
        new(
            id,
            name,
            "Test",
            "Test",
            "local",
            ResourceState.Running,
            [],
            "1.0",
            DateTimeOffset.UtcNow,
            [],
            ParentResourceId: parentResourceId,
            TypeId: "test.resource",
            ResourceClass: resourceClass,
            Attributes: attributes);

    private static ResourceRegistration CreateRegistration(
        string resourceId,
        string? resourceGroupId = null) =>
        new(
            resourceId,
            "test",
            resourceGroupId,
            DateTimeOffset.UtcNow,
            []);

    private sealed class TestResourceProvider(IReadOnlyList<Resource> resources) : IResourceProvider
    {
        public string Id => "test";

        public string DisplayName => "Test";

        public IReadOnlyList<Resource> GetResources() => resources;
    }

    private sealed class TestResourceGroupStore(IReadOnlyList<ResourceGroup> groups) : IResourceGroupStore
    {
        public IReadOnlyList<ResourceGroup> GetResourceGroups() => groups;

        public ResourceGroup? GetGroupForResource(string resourceId) =>
            groups.FirstOrDefault(group =>
                group.ResourceIds.Contains(resourceId, StringComparer.OrdinalIgnoreCase));

        public Task<ResourceGroup> CreateAsync(
            string name,
            string description,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class TestResourceRegistrationStore(IReadOnlyList<ResourceRegistration> registrations) :
        IResourceRegistrationStore
    {
        public IReadOnlyList<ResourceRegistration> GetRegistrations() => registrations;

        public ResourceRegistration? GetRegistration(string resourceId) =>
            registrations.FirstOrDefault(registration =>
                string.Equals(registration.ResourceId, resourceId, StringComparison.OrdinalIgnoreCase));

        public Task RegisterAsync(
            string providerId,
            string resourceId,
            string? resourceGroupId = null,
            IReadOnlyList<string>? dependsOn = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task RemoveAsync(
            string resourceId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task AssignToGroupAsync(
            string resourceId,
            string? resourceGroupId,
            IReadOnlyList<string>? dependsOn = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task SetDependenciesAsync(
            string resourceId,
            IReadOnlyList<string> dependsOn,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class TestCloudShellBuilder : ICloudShellBuilder
    {
        public IServiceCollection Services { get; } = new ServiceCollection();
    }
}
