using CloudShell.ResourceDefinitions.ReferenceProviders;

namespace CloudShell.ResourceDefinitions.Tests;

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

    private static Resource CreateReadOnlyResource(string value = "generated")
    {
        ResourceClassId classId = "test";
        ResourceTypeId typeId = "test.read-only";
        var resolver = new ResourceResolver(
            [new ResourceClassDefinition(classId)],
            [
                new(
                    typeId,
                    classId,
                    Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeDefinition>
                    {
                        ["system.generated"] = new(ReadOnly: true)
                    })
            ]);

        return resolver.Resolve(new ResourceState(
            "resource",
            typeId,
            Attributes: new Dictionary<ResourceAttributeId, string>
            {
                ["system.generated"] = value
            }));
    }
}
