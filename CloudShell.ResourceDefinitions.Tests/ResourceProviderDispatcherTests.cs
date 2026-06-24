using System.Text.Json;
using CloudShell.ResourceDefinitions.ReferenceProviders;
using CloudShell.ResourceDefinitions.ResourceManager;
using Microsoft.Extensions.DependencyInjection;

namespace CloudShell.ResourceDefinitions.Tests;

public sealed class ResourceProviderDispatcherTests
{
    [Fact]
    public void AddResourceModelResolver_BuildsResolverFromRegisteredTypeProviders()
    {
        var services = new ServiceCollection();
        services.AddExecutableApplicationResourceType();
        services.AddResourceModelResolver();
        using var serviceProvider = services.BuildServiceProvider();

        var resolver = serviceProvider.GetRequiredService<ResourceResolver>();
        var resolved = resolver.Resolve(new ResourceDefinition(
            "api",
            ExecutableApplicationResourceTypeProvider.ResourceTypeId));

        Assert.Equal(ExecutableApplicationResourceTypeProvider.ResourceTypeId, resolved.Type.TypeId);
        Assert.Equal(ExecutableApplicationResourceTypeProvider.ClassId, resolved.Class.ClassId);
    }

    [Fact]
    public void AddResourceModelResolver_AcceptsExplicitClassDefinitions()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IResourceTypeProvider>(new ExecutableApplicationResourceTypeProvider());
        services.AddResourceModelResolver(
            [new(ExecutableApplicationResourceTypeProvider.ClassId)]);
        using var serviceProvider = services.BuildServiceProvider();

        var resolver = serviceProvider.GetRequiredService<ResourceResolver>();
        var resolved = resolver.Resolve(new ResourceDefinition(
            "api",
            ExecutableApplicationResourceTypeProvider.ResourceTypeId));

        Assert.Equal(ExecutableApplicationResourceTypeProvider.ResourceTypeId, resolved.Type.TypeId);
        Assert.Equal(ExecutableApplicationResourceTypeProvider.ClassId, resolved.Class.ClassId);
    }

    [Fact]
    public async Task AddExecutableApplicationResourceType_RegistersCompleteResourceTypeBoundary()
    {
        var services = new ServiceCollection();
        services.AddLocalVolumeResourceType();
        services.AddExecutableApplicationResourceType();
        services.AddInMemoryResourceModelGraph();
        services.AddResourceModelGraphServices();
        using var serviceProvider = services.BuildServiceProvider();

        Assert.NotNull(serviceProvider.GetRequiredService<ResourceModelGraphResourceResolver>());
        Assert.NotNull(serviceProvider.GetRequiredService<ResourceDefinitionGraphChangeApplier>());

        var volume = new ResourceDefinition(
            "data",
            LocalVolumeResourceTypeProvider.ResourceTypeId);
        var definition = new ResourceDefinition(
            "api",
            ExecutableApplicationResourceTypeProvider.ResourceTypeId,
            DependsOn: [ResourceReference.ResourceId(volume.EffectiveResourceId)],
            Attributes: new Dictionary<ResourceAttributeId, string>
            {
                [ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath] = "dotnet"
            },
            Configuration: new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                [ExecutableApplicationResourceTypeProvider.ConfigurationSection] =
                    ResourceDefinitionJson.FromValue(new ExecutableApplicationConfiguration("dotnet", "run"))
            },
            Capabilities: new Dictionary<ResourceCapabilityId, JsonElement>
            {
                [VolumeConsumerCapabilityProvider.CapabilityIdValue] =
                    ResourceDefinitionJson.FromValue(new VolumeConsumerDefinition(
                    [
                        new(volume.EffectiveResourceId, "App_Data")
                    ]))
            });
        var validation = await serviceProvider
            .GetRequiredService<ResourceDefinitionGraphValidationPipeline>()
            .ValidateAsync(
                new ResourceDefinitionGraph([volume, definition]),
                new ResourceDefinitionValidationContext("local", "developer"));

        Assert.False(validation.HasErrors);

        var projectedGraph = await serviceProvider
            .GetRequiredService<ResourceDefinitionGraphProjectionResolver>()
            .ProjectAsync(
                validation,
                new ResourceProjectionContext("local", "developer"));
        var projection = projectedGraph.Find<ExecutableApplicationResource>(
            "application.executable:api");

        Assert.NotNull(projection);

        var volumes = await projection.GetVolumesAsync();
        var start = await projection.GetStartOperationAsync();

        Assert.NotNull(start);
        Assert.Equal(volume.EffectiveResourceId, Assert.Single(volumes).Volume);
        Assert.True(await start.CanExecuteAsync());
        Assert.Equal("dotnet", projection.ExecutablePath);

        var applyPlan = await serviceProvider
            .GetRequiredService<ResourceDefinitionGraphApplyPlanner>()
            .PlanApplyAsync(
                validation,
                new ResourceDefinitionApplyContext("local", "developer"));

        Assert.False(applyPlan.HasErrors);
        Assert.Contains(applyPlan.Steps, step =>
            step.Kind == ResourceDefinitionApplyStepKind.AcceptDefinition);
        Assert.Contains(applyPlan.Steps, step =>
            step.Kind == ResourceDefinitionApplyStepKind.MaterializeRuntime);
    }

    [Fact]
    public async Task AddLocalVolumeResourceType_RegistersCompleteResourceTypeBoundary()
    {
        var services = new ServiceCollection();
        services.AddLocalVolumeResourceType();
        services.AddResourceModelGraphServices();
        using var serviceProvider = services.BuildServiceProvider();
        var definition = new ResourceDefinition(
            "data",
            LocalVolumeResourceTypeProvider.ResourceTypeId);

        var validation = await serviceProvider
            .GetRequiredService<ResourceDefinitionValidationPipeline>()
            .ValidateAsync(
                definition,
                new ResourceDefinitionValidationContext("local", "developer"));

        Assert.False(validation.HasErrors);
        Assert.Equal(LocalVolumeResourceTypeProvider.ClassId, validation.Resource.Class.ClassId);
        Assert.Equal("volume", validation.Resource.Attributes.GetString(
            LocalVolumeResourceTypeProvider.Attributes.StorageKind));
        Assert.Equal("local", validation.Resource.Attributes.GetString(
            LocalVolumeResourceTypeProvider.Attributes.StorageMedium));
        Assert.True(validation.Resource.Operations.Has(
            LocalVolumeResourceTypeProvider.Operations.Provision));

        var applyPlan = await serviceProvider
            .GetRequiredService<ResourceDefinitionGraphApplyPlanner>()
            .PlanApplyAsync(
                new ResourceDefinitionGraphValidationPipelineResult(
                    new ResourceDefinitionGraph([definition]),
                    [validation],
                    []),
                new ResourceDefinitionApplyContext("local", "developer"));

        Assert.False(applyPlan.HasErrors);
        Assert.Contains(applyPlan.Steps, step =>
            step.ResourceId == "storage.volume:data" &&
            step.Kind == ResourceDefinitionApplyStepKind.MaterializeRuntime);

        var projectedGraph = await serviceProvider
            .GetRequiredService<ResourceDefinitionGraphProjectionResolver>()
            .ProjectAsync(
                new ResourceDefinitionGraphValidationPipelineResult(
                    new ResourceDefinitionGraph([definition]),
                    [validation],
                    []),
                new ResourceProjectionContext("local", "developer"));
        var projection = projectedGraph.Find<LocalVolumeResource>("storage.volume:data");

        Assert.NotNull(projection);
        Assert.Equal("volume", projection.StorageKind);
        Assert.Equal("local", projection.StorageMedium);

        var provision = await projection.GetProvisionOperationAsync();

        Assert.NotNull(provision);
        Assert.True(await provision.CanExecuteAsync());
    }

    [Fact]
    public async Task ValidateCapabilitiesAsync_UsesRegisteredCapabilityProvider()
    {
        var dispatcher = CreateDispatcher(
            typeProviders: [new ExecutableApplicationResourceTypeProvider()],
            capabilityProviders: [new VolumeConsumerCapabilityProvider()],
            operationProviders: []);
        var resolved = ResolveExecutable(
            capabilities: new Dictionary<ResourceCapabilityId, JsonElement>
            {
                [VolumeConsumerCapabilityProvider.CapabilityIdValue] = ResourceDefinitionJson.FromValue(new
                {
                    mounts = new[]
                    {
                        new { volume = "volume:data", targetPath = "App_Data" }
                    }
                })
            });

        var result = await dispatcher.ValidateCapabilitiesAsync(
            resolved,
            new ResourceProviderContext("local", "developer"));

        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public async Task ValidateCapabilitiesAsync_ReportsMissingCapabilityProvider()
    {
        var dispatcher = CreateDispatcher(
            typeProviders: [new ExecutableApplicationResourceTypeProvider()],
            capabilityProviders: [],
            operationProviders: []);
        var resolved = ResolveExecutable(
            capabilities: new Dictionary<ResourceCapabilityId, JsonElement>
            {
                [VolumeConsumerCapabilityProvider.CapabilityIdValue] = ResourceDefinitionJson.EmptyObject
            });

        var result = await dispatcher.ValidateCapabilitiesAsync(
            resolved,
            new ResourceProviderContext());

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(ResourceDefinitionDiagnosticCodes.CapabilityProviderMissing, diagnostic.Code);
        Assert.Equal("storage.volumeConsumer", diagnostic.Target);
    }

    [Fact]
    public async Task ValidateOperationsAsync_UsesMatchingOperationProviderAndResolutionLevel()
    {
        var dispatcher = CreateDispatcher(
            typeProviders: [new ExecutableApplicationResourceTypeProvider()],
            capabilityProviders: [],
            operationProviders: [new ExecutableStartOperationProvider()]);
        var resolved = ResolveExecutable();

        var result = await dispatcher.ValidateOperationsAsync(
            resolved,
            new ResourceProviderContext("local", "developer"));

        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public async Task ValidateResourceTypeAsync_UsesRegisteredTypeProvider()
    {
        var dispatcher = CreateDispatcher(
            typeProviders: [new ExecutableApplicationResourceTypeProvider()],
            capabilityProviders: [],
            operationProviders: []);
        var resolved = ResolveExecutable(
            configuration: new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                [ExecutableApplicationResourceTypeProvider.ConfigurationSection] =
                    ResourceDefinitionJson.FromValue(new ExecutableApplicationConfiguration("dotnet", "run"))
            });

        var result = await dispatcher.ValidateResourceTypeAsync(
            resolved,
            new ResourceProviderContext("local", "developer"));

        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public async Task ValidateResourceTypeAsync_ReportsTypeOwnedConfigurationDiagnostics()
    {
        var dispatcher = CreateDispatcher(
            typeProviders: [new ExecutableApplicationResourceTypeProvider()],
            capabilityProviders: [],
            operationProviders: []);
        var resolved = ResolveExecutable(
            configuration: new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                [ExecutableApplicationResourceTypeProvider.ConfigurationSection] =
                    ResourceDefinitionJson.FromValue(new ExecutableApplicationConfiguration("", "run"))
            });

        var result = await dispatcher.ValidateResourceTypeAsync(
            resolved,
            new ResourceProviderContext());

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("application.executable.pathRequired", diagnostic.Code);
        Assert.Equal(ExecutableApplicationResourceTypeProvider.ConfigurationSection, diagnostic.Target);
    }

    private static Resource ResolveExecutable(
        IReadOnlyDictionary<string, JsonElement>? configuration = null,
        IReadOnlyDictionary<ResourceCapabilityId, JsonElement>? capabilities = null)
    {
        var typeProvider = new ExecutableApplicationResourceTypeProvider();
        var resolver = new ResourceResolver(
            [
                new(ExecutableApplicationResourceTypeProvider.ClassId)
            ],
            [
                typeProvider.TypeDefinition
            ]);

        return resolver.Resolve(new ResourceDefinition(
            "api",
            ExecutableApplicationResourceTypeProvider.ResourceTypeId,
            Configuration: configuration,
            Capabilities: capabilities));
    }

    private static ResourceProviderDispatcher CreateDispatcher(
        IReadOnlyList<IResourceTypeProvider> typeProviders,
        IReadOnlyList<IResourceCapabilityProvider> capabilityProviders,
        IReadOnlyList<IResourceOperationProvider> operationProviders)
    {
        var services = new ServiceCollection();

        foreach (var provider in typeProviders)
        {
            services.AddSingleton<IResourceTypeProvider>(provider);
        }

        foreach (var provider in capabilityProviders)
        {
            services.AddSingleton<IResourceCapabilityProvider>(provider);
        }

        foreach (var provider in operationProviders)
        {
            services.AddSingleton<IResourceOperationProvider>(provider);
        }

        services.AddSingleton<ResourceProviderDispatcher>();

        return services
            .BuildServiceProvider()
            .GetRequiredService<ResourceProviderDispatcher>();
    }

    private sealed class VolumeConsumerCapabilityProvider : IResourceCapabilityProvider
    {
        public static readonly ResourceCapabilityId CapabilityIdValue = "storage.volumeConsumer";

        public ResourceCapabilityId CapabilityId => CapabilityIdValue;

        public bool CanValidate(
            Resource resource,
            ResourceCapabilityResolution capability) =>
            resource.Type.TypeId == ExecutableApplicationResourceTypeProvider.ResourceTypeId;

        public ValueTask<ResourceDefinitionValidationResult> ValidateAsync(
            Resource resource,
            ResourceCapabilityResolution capability,
            ResourceProviderContext context,
            CancellationToken cancellationToken = default)
        {
            using var document = JsonDocument.Parse(capability.Payload.GetRawText());
            var hasMounts = document.RootElement.TryGetProperty("mounts", out var mounts) &&
                mounts.ValueKind == JsonValueKind.Array &&
                mounts.GetArrayLength() > 0;

            var result = hasMounts
                ? ResourceDefinitionValidationResult.Success
                : ResourceDefinitionValidationResult.FromDiagnostics(
                    [
                        ResourceDefinitionDiagnostic.Error(
                            "storage.volumeConsumer.mountsRequired",
                            "At least one volume mount is required.",
                            capability.Id)
                    ]);

            return ValueTask.FromResult(result);
        }
    }

    private sealed class ExecutableStartOperationProvider : IResourceOperationProvider
    {
        public static readonly ResourceOperationId OperationIdValue =
            ExecutableApplicationResourceTypeProvider.Operations.Start;

        public ResourceOperationId OperationId => OperationIdValue;

        public ResourceDefinitionValueSource ResolutionLevel =>
            ResourceDefinitionValueSource.TypeDefinition;

        public bool CanHandle(
            Resource resource,
            ResourceOperationResolution operation) =>
            resource.Type.TypeId == ExecutableApplicationResourceTypeProvider.ResourceTypeId &&
            operation.IsAvailable;

        public ValueTask<ResourceDefinitionValidationResult> ValidateAsync(
            Resource resource,
            ResourceOperationResolution operation,
            ResourceProviderContext context,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(ResourceDefinitionValidationResult.Success);
    }

}
