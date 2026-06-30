using CloudShell.ControlPlane.Providers;

namespace CloudShell.ResourceModel.Tests;

public sealed class ResourceChangeApplyDispatcherTests
{
    [Fact]
    public async Task ApplyChangesAsync_UsesTypeOwnedChangeApplyProvider()
    {
        var resource = CreateResource("./api");
        resource.SetAttribute(ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath, "./worker");
        var changes = resource.ApplyChanges();
        var dispatcher = new ResourceChangeApplyDispatcher(
            [new ExecutableApplicationResourceTypeProvider()]);

        var result = await dispatcher.ApplyChangesAsync(
            changes,
            new ResourceChangeApplyContext("local", "developer", Commit: true));

        Assert.True(result.IsAccepted);
        Assert.Same(changes, result.ChangeSet);
        Assert.NotNull(result.AcceptedState);
        Assert.Equal("./worker", result.AcceptedState.ResourceAttributes[
            ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath]);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public async Task ApplyChangesAsync_ReturnsProviderDiagnosticsWhenChangeIsRejected()
    {
        var resource = CreateResource("./api");
        resource.SetAttribute(ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath, "");
        var changes = resource.ApplyChanges();
        var dispatcher = new ResourceChangeApplyDispatcher(
            [new ExecutableApplicationResourceTypeProvider()]);

        var result = await dispatcher.ApplyChangesAsync(
            changes,
            new ResourceChangeApplyContext());

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.False(result.IsAccepted);
        Assert.Null(result.AcceptedState);
        Assert.Equal("application.executable.pathRequired", diagnostic.Code);
        Assert.Equal(ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath, diagnostic.Target);
    }

    [Fact]
    public async Task ApplyChangesAsync_ReportsMissingChangeApplyProvider()
    {
        var resource = CreateResource("./api");
        resource.SetAttribute(ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath, "./worker");
        var changes = resource.ApplyChanges();
        var dispatcher = new ResourceChangeApplyDispatcher([]);

        var result = await dispatcher.ApplyChangesAsync(
            changes,
            new ResourceChangeApplyContext());

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.False(result.IsAccepted);
        Assert.Equal(ResourceDefinitionDiagnosticCodes.ResourceChangeApplyProviderMissing, diagnostic.Code);
        Assert.Equal(resource.EffectiveResourceId, diagnostic.Target);
    }

    [Fact]
    public async Task ApplyChangesAsync_RejectsReadOnlyAttributeChangesBeforeProviderDispatch()
    {
        var resource = CreateReadOnlyResource();
        resource.SetAttribute("system.generated", "changed");
        var changes = resource.ApplyChanges();
        var dispatcher = new ResourceChangeApplyDispatcher([]);

        var result = await dispatcher.ApplyChangesAsync(
            changes,
            new ResourceChangeApplyContext());

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.False(result.IsAccepted);
        Assert.Null(result.AcceptedState);
        Assert.Equal(ResourceDefinitionDiagnosticCodes.ReadOnlyAttributeChange, diagnostic.Code);
        Assert.Equal("system.generated", diagnostic.Target);
    }

    [Fact]
    public async Task ApplyChangesAsync_RejectsReadOnlyAttributesOnNewResources()
    {
        var resource = CreateReadOnlyResource("caller-value");
        var changes = ResourceChangeSet.FromNewResource(resource);
        var dispatcher = new ResourceChangeApplyDispatcher([]);

        var result = await dispatcher.ApplyChangesAsync(
            changes,
            new ResourceChangeApplyContext());

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.False(result.IsAccepted);
        Assert.Null(result.AcceptedState);
        Assert.Equal(ResourceDefinitionDiagnosticCodes.ReadOnlyAttributeChange, diagnostic.Code);
        Assert.Equal("system.generated", diagnostic.Target);
    }

    [Fact]
    public async Task ApplyChangesAsync_InheritsReadOnlyAttributeMetadataFromClassDefinitions()
    {
        var resource = CreateReadOnlyResource(
            "generated",
            classReadOnly: true,
            includeTypeAttributeDefinition: true,
            typeReadOnly: null);
        resource.SetAttribute("system.generated", "changed");
        var changes = resource.ApplyChanges();
        var dispatcher = new ResourceChangeApplyDispatcher([]);

        var result = await dispatcher.ApplyChangesAsync(
            changes,
            new ResourceChangeApplyContext());

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.False(result.IsAccepted);
        Assert.Equal(ResourceDefinitionDiagnosticCodes.ReadOnlyAttributeChange, diagnostic.Code);
        Assert.Equal("system.generated", diagnostic.Target);
    }

    [Fact]
    public async Task ApplyChangesAsync_AllowsTypeDefinitionToExplicitlyClearClassReadOnlyMetadata()
    {
        var resource = CreateReadOnlyResource(
            "generated",
            classReadOnly: true,
            includeTypeAttributeDefinition: true,
            typeReadOnly: false);
        resource.SetAttribute("system.generated", "changed");
        var changes = resource.ApplyChanges();
        var dispatcher = new ResourceChangeApplyDispatcher([]);

        var result = await dispatcher.ApplyChangesAsync(
            changes,
            new ResourceChangeApplyContext());

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.False(result.IsAccepted);
        Assert.Equal(ResourceDefinitionDiagnosticCodes.ResourceChangeApplyProviderMissing, diagnostic.Code);
        Assert.Equal(resource.EffectiveResourceId, diagnostic.Target);
    }

    [Fact]
    public async Task ApplyChangesAsync_AllowsProviderManagedReadOnlyAttributeUpdatesFromProviderResult()
    {
        var resource = CreateReadOnlyResource(
            "generated",
            typeReadOnly: true,
            mutability: ResourceAttributeMutability.ProviderManaged);
        resource.SetAttribute("user.value", "changed");
        var changes = resource.ApplyChanges();
        var dispatcher = new ResourceChangeApplyDispatcher(
            [
                new TestChangeApplyProvider(
                    resource.Type.TypeId,
                    changeSet => UpdateAttribute(
                        changeSet.ProposedState,
                        "system.generated",
                        "provider-value"))
            ]);

        var result = await dispatcher.ApplyChangesAsync(
            changes,
            new ResourceChangeApplyContext());

        Assert.True(result.IsAccepted);
        Assert.NotNull(result.AcceptedState);
        Assert.Equal("provider-value", result.AcceptedState.ResourceAttributes["system.generated"]);
        Assert.DoesNotContain("system.generated", result.ToAcceptedDefinition()!.ResourceAttributes.Keys);
    }

    [Fact]
    public async Task ApplyChangesAsync_RejectsProviderResultChangingReadOnlyCallerManagedAttribute()
    {
        var resource = CreateReadOnlyResource(
            "generated",
            typeReadOnly: true,
            mutability: ResourceAttributeMutability.CallerManaged);
        resource.SetAttribute("user.value", "changed");
        var changes = resource.ApplyChanges();
        var dispatcher = new ResourceChangeApplyDispatcher(
            [
                new TestChangeApplyProvider(
                    resource.Type.TypeId,
                    changeSet => UpdateAttribute(
                        changeSet.ProposedState,
                        "system.generated",
                        "provider-value"))
            ]);

        var result = await dispatcher.ApplyChangesAsync(
            changes,
            new ResourceChangeApplyContext());

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.False(result.IsAccepted);
        Assert.Null(result.AcceptedState);
        Assert.Equal(ResourceDefinitionDiagnosticCodes.ReadOnlyAttributeChange, diagnostic.Code);
        Assert.Equal("system.generated", diagnostic.Target);
    }

    [Fact]
    public async Task ApplyChangesAsync_AcceptsNoOpChangeSetWithoutProviderDispatch()
    {
        var resource = CreateResource("./api");
        var changes = resource.GetPendingChanges();
        var dispatcher = new ResourceChangeApplyDispatcher([]);

        var result = await dispatcher.ApplyChangesAsync(
            changes,
            new ResourceChangeApplyContext());

        Assert.True(result.IsAccepted);
        Assert.Same(resource.State, result.AcceptedState);
        Assert.Empty(result.Diagnostics);
    }

    private static Resource CreateResource(string executablePath)
    {
        var typeProvider = new ExecutableApplicationResourceTypeProvider();
        var resolver = new ResourceResolver(
            [new ResourceClassDefinition(ExecutableApplicationResourceTypeProvider.ClassId)],
            [typeProvider.TypeDefinition]);

        return resolver.Resolve(new ResourceState(
            "api",
            ExecutableApplicationResourceTypeProvider.ResourceTypeId,
            Attributes: new Dictionary<ResourceAttributeId, string>
            {
                [ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath] = executablePath
            }));
    }

    private static Resource CreateReadOnlyResource(
        string value = "generated",
        bool? classReadOnly = null,
        bool includeTypeAttributeDefinition = true,
        bool? typeReadOnly = true,
        ResourceAttributeMutability? mutability = null)
    {
        ResourceClassId classId = "test";
        ResourceTypeId typeId = "test.read-only";
        var resolver = new ResourceResolver(
            [
                new ResourceClassDefinition(
                    classId,
                    Attributes: classReadOnly.HasValue
                        ? new Dictionary<ResourceAttributeId, ResourceAttributeDefinition>
                        {
                            ["system.generated"] = new(ReadOnly: classReadOnly)
                        }
                        : null)
            ],
            [
                new(
                    typeId,
                    classId,
                    Attributes: includeTypeAttributeDefinition
                        ? new Dictionary<ResourceAttributeId, ResourceAttributeDefinition>
                        {
                            ["system.generated"] = new(
                                ReadOnly: typeReadOnly,
                                Mutability: mutability)
                        }
                        : null)
            ]);

        return resolver.Resolve(new ResourceState(
            "resource",
            typeId,
            Attributes: new Dictionary<ResourceAttributeId, string>
            {
                ["system.generated"] = value
            }));
    }

    private static ResourceState UpdateAttribute(
        ResourceState state,
        ResourceAttributeId attributeId,
        string value)
    {
        var attributes = new Dictionary<ResourceAttributeId, string>(state.ResourceAttributes)
        {
            [attributeId] = value
        };

        return state with { Attributes = attributes };
    }

    private sealed class TestChangeApplyProvider(
        ResourceTypeId typeId,
        Func<ResourceChangeSet, ResourceState> apply) : IResourceChangeApplyProvider
    {
        public ResourceTypeId TypeId { get; } = typeId;

        public bool CanApply(ResourceChangeSet changes) =>
            changes.Resource.Type.TypeId == TypeId;

        public ValueTask<ResourceChangeApplyResult> ApplyChangesAsync(
            ResourceChangeSet changes,
            ResourceChangeApplyContext context,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ResourceChangeApplyResult(
                changes,
                apply(changes),
                changes.Diagnostics));
    }
}
