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
    public async Task AddAspNetCoreProjectResourceType_RegistersCompleteResourceTypeBoundary()
    {
        var services = new ServiceCollection();
        services.AddLocalVolumeResourceType();
        services.AddAspNetCoreProjectResourceType();
        services.AddResourceModelGraphServices();
        using var serviceProvider = services.BuildServiceProvider();
        var volume = new ResourceDefinition(
            "data",
            LocalVolumeResourceTypeProvider.ResourceTypeId);
        var definition = new ResourceDefinition(
            "api",
            AspNetCoreProjectResourceTypeProvider.ResourceTypeId,
            Attributes: new Dictionary<ResourceAttributeId, string>
            {
                [AspNetCoreProjectResourceTypeProvider.Attributes.ProjectPath] = "src/Api/Api.csproj",
                [AspNetCoreProjectResourceTypeProvider.Attributes.ProjectArguments] = "--urls http://localhost:5010",
                [AspNetCoreProjectResourceTypeProvider.Attributes.HotReload] = bool.TrueString.ToLowerInvariant(),
                [AspNetCoreProjectResourceTypeProvider.Attributes.UseLaunchSettings] = bool.FalseString.ToLowerInvariant()
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
        var projectValidation = validation.Resources.Single(resource =>
            resource.Resource.EffectiveResourceId == definition.EffectiveResourceId);
        Assert.Equal(AspNetCoreProjectResourceTypeProvider.ClassId, projectValidation.Resource.Class.ClassId);
        Assert.Equal("src/Api/Api.csproj", projectValidation.Resource.Attributes.GetString(
            AspNetCoreProjectResourceTypeProvider.Attributes.ProjectPath));
        Assert.True(projectValidation.Resource.Capabilities.Has(
            VolumeConsumerCapabilityProvider.CapabilityIdValue));
        Assert.True(projectValidation.Resource.Operations.Has(
            AspNetCoreProjectResourceTypeProvider.Operations.Start));

        var projectedGraph = await serviceProvider
            .GetRequiredService<ResourceDefinitionGraphProjectionResolver>()
            .ProjectAsync(
                validation,
                new ResourceProjectionContext("local", "developer"));
        var projection = projectedGraph.Find<AspNetCoreProjectResource>(
            definition.EffectiveResourceId);

        Assert.NotNull(projection);
        Assert.Equal("src/Api/Api.csproj", projection.ProjectPath);
        Assert.Equal("--urls http://localhost:5010", projection.Arguments);
        Assert.True(projection.HotReload);
        Assert.False(projection.UseLaunchSettings);
        Assert.Equal(volume.EffectiveResourceId, Assert.Single(await projection.GetVolumesAsync()).Volume);

        var start = await projection.GetStartOperationAsync();
        var restart = await projection.GetRestartOperationAsync();

        Assert.NotNull(start);
        Assert.NotNull(restart);
        Assert.True(await start.CanExecuteAsync());
        Assert.True(await restart.CanExecuteAsync());

        var applyPlan = await serviceProvider
            .GetRequiredService<ResourceDefinitionGraphApplyPlanner>()
            .PlanApplyAsync(
                validation,
                new ResourceDefinitionApplyContext("local", "developer"));

        Assert.False(applyPlan.HasErrors);
        Assert.Contains(applyPlan.Steps, step =>
            step.ResourceId == definition.EffectiveResourceId &&
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
    public async Task AddStorageResourceType_RegistersCompleteResourceTypeBoundary()
    {
        var services = new ServiceCollection();
        services.AddStorageResourceType();
        services.AddResourceModelGraphServices();
        using var serviceProvider = services.BuildServiceProvider();
        var definition = new ResourceDefinition(
            "local",
            StorageResourceTypeProvider.ResourceTypeId,
            Attributes: new Dictionary<ResourceAttributeId, string>
            {
                [StorageResourceTypeProvider.Attributes.Provider] = "Local Storage",
                [StorageResourceTypeProvider.Attributes.Medium] = "FileSystem",
                [StorageResourceTypeProvider.Attributes.Location] = "Data/storage/local"
            });

        var validation = await serviceProvider
            .GetRequiredService<ResourceDefinitionValidationPipeline>()
            .ValidateAsync(
                definition,
                new ResourceDefinitionValidationContext("local", "developer"));

        Assert.False(validation.HasErrors);
        Assert.Equal(StorageResourceTypeProvider.ClassId, validation.Resource.Class.ClassId);
        Assert.Equal("provider", validation.Resource.Attributes.GetString(
            StorageResourceTypeProvider.Attributes.StorageKind));
        Assert.Equal("Local Storage", validation.Resource.Attributes.GetString(
            StorageResourceTypeProvider.Attributes.Provider));
        Assert.Equal("FileSystem", validation.Resource.Attributes.GetString(
            StorageResourceTypeProvider.Attributes.Medium));
        Assert.Equal("Data/storage/local", validation.Resource.Attributes.GetString(
            StorageResourceTypeProvider.Attributes.Location));
        Assert.True(validation.Resource.Capabilities.Has(
            StorageResourceTypeProvider.Capabilities.StorageProvider));
        Assert.True(validation.Resource.Capabilities.Has(
            StorageResourceTypeProvider.Capabilities.StorageMountProvider));
        Assert.True(validation.Resource.Operations.Has(
            StorageResourceTypeProvider.Operations.Inspect));

        var projectedGraph = await serviceProvider
            .GetRequiredService<ResourceDefinitionGraphProjectionResolver>()
            .ProjectAsync(
                new ResourceDefinitionGraphValidationPipelineResult(
                    new ResourceDefinitionGraph([definition]),
                    [validation],
                    []),
                new ResourceProjectionContext("local", "developer"));
        var projection = projectedGraph.Find<StorageResource>(
            definition.EffectiveResourceId);

        Assert.NotNull(projection);
        Assert.Equal("provider", projection.StorageKind);
        Assert.Equal("Local Storage", projection.Provider);
        Assert.Equal("FileSystem", projection.Medium);
        Assert.True(projection.SupportsStorage);
        Assert.True(projection.SupportsMounts);
        var inspect = await projection.GetInspectOperationAsync();

        Assert.NotNull(inspect);
        Assert.True(await inspect.CanExecuteAsync());
        Assert.Equal("FileSystem", inspect.PlanInspection().Medium);
    }

    [Fact]
    public async Task AddCloudShellVolumeResourceType_RegistersCompleteResourceTypeBoundary()
    {
        var services = new ServiceCollection();
        services.AddStorageResourceType();
        services.AddCloudShellVolumeResourceType();
        services.AddResourceModelGraphServices();
        using var serviceProvider = services.BuildServiceProvider();
        var storage = new ResourceDefinition(
            "local",
            StorageResourceTypeProvider.ResourceTypeId);
        var definition = new ResourceDefinition(
            "data",
            CloudShellVolumeResourceTypeProvider.ResourceTypeId,
            DependsOn: [ResourceReference.ResourceId(storage.EffectiveResourceId)],
            Attributes: new Dictionary<ResourceAttributeId, string>
            {
                [CloudShellVolumeResourceTypeProvider.Attributes.Provider] = "Local Storage",
                [CloudShellVolumeResourceTypeProvider.Attributes.StorageMedium] = "FileSystem",
                [CloudShellVolumeResourceTypeProvider.Attributes.SubPath] = "data",
                [CloudShellVolumeResourceTypeProvider.Attributes.AccessMode] = "ReadWriteOnce",
                [CloudShellVolumeResourceTypeProvider.Attributes.Persistent] = bool.TrueString.ToLowerInvariant()
            });

        var validation = await serviceProvider
            .GetRequiredService<ResourceDefinitionGraphValidationPipeline>()
            .ValidateAsync(
                new ResourceDefinitionGraph([storage, definition]),
                new ResourceDefinitionValidationContext("local", "developer"));

        Assert.False(validation.HasErrors);
        var resource = Assert.Single(validation.Resources, result =>
            result.Resource.EffectiveResourceId == definition.EffectiveResourceId).Resource;
        Assert.Equal(CloudShellVolumeResourceTypeProvider.ClassId, resource.Class.ClassId);
        Assert.Equal("volume", resource.Attributes.GetString(
            CloudShellVolumeResourceTypeProvider.Attributes.StorageKind));
        Assert.Equal("FileSystem", resource.Attributes.GetString(
            CloudShellVolumeResourceTypeProvider.Attributes.StorageMedium));
        Assert.True(resource.Capabilities.Has(
            CloudShellVolumeResourceTypeProvider.Capabilities.StorageVolume));
        Assert.True(resource.Operations.Has(
            CloudShellVolumeResourceTypeProvider.Operations.Provision));

        var projectedGraph = await serviceProvider
            .GetRequiredService<ResourceDefinitionGraphProjectionResolver>()
            .ProjectAsync(
                validation,
                new ResourceProjectionContext("local", "developer"));
        var projection = projectedGraph.Find<CloudShellVolumeResource>(
            definition.EffectiveResourceId);

        Assert.NotNull(projection);
        Assert.Equal("data", projection.SubPath);
        Assert.True(projection.Persistent);
        Assert.Equal([storage.EffectiveResourceId], projection.References.Select(reference => reference.Value));
        var provision = await projection.GetProvisionOperationAsync();

        Assert.NotNull(provision);
        Assert.True(await provision.CanExecuteAsync());
        Assert.Equal("FileSystem", provision.PlanProvision().StorageMedium);
        Assert.Equal([storage.EffectiveResourceId], provision.PlanProvision().References.Select(reference => reference.Value));
    }

    [Fact]
    public async Task AddServiceResourceType_RegistersCompleteResourceTypeBoundary()
    {
        var services = new ServiceCollection();
        services.AddServiceResourceType();
        services.AddResourceModelGraphServices();
        using var serviceProvider = services.BuildServiceProvider();
        var definition = new ResourceDefinition(
            "api",
            ServiceResourceTypeProvider.ResourceTypeId,
            DependsOn:
            [
                ResourceReference.ResourceId("application.container-app:api"),
                ResourceReference.ResourceId("cloudshell.network:default")
            ],
            Attributes: new Dictionary<ResourceAttributeId, string>
            {
                [ServiceResourceTypeProvider.Attributes.RoutingMode] = "logical"
            });

        var validation = await serviceProvider
            .GetRequiredService<ResourceDefinitionValidationPipeline>()
            .ValidateAsync(
                definition,
                new ResourceDefinitionValidationContext("local", "developer"));

        Assert.False(validation.HasErrors);
        Assert.Equal(ServiceResourceTypeProvider.ClassId, validation.Resource.Class.ClassId);
        Assert.Equal("service", validation.Resource.Attributes.GetString(
            ServiceResourceTypeProvider.Attributes.ServiceKind));
        Assert.Equal("logical", validation.Resource.Attributes.GetString(
            ServiceResourceTypeProvider.Attributes.RoutingMode));
        Assert.True(validation.Resource.Capabilities.Has(
            ServiceResourceTypeProvider.Capabilities.EndpointSource));
        Assert.True(validation.Resource.Operations.Has(
            ServiceResourceTypeProvider.Operations.Reconcile));

        var projectedGraph = await serviceProvider
            .GetRequiredService<ResourceDefinitionGraphProjectionResolver>()
            .ProjectAsync(
                new ResourceDefinitionGraphValidationPipelineResult(
                    new ResourceDefinitionGraph([definition]),
                    [validation],
                    []),
                new ResourceProjectionContext("local", "developer"));
        var projection = projectedGraph.Find<ServiceResource>(
            definition.EffectiveResourceId);

        Assert.NotNull(projection);
        Assert.Equal("logical", projection.RoutingMode);
        Assert.True(projection.SupportsEndpointSource);
        Assert.Equal(
            ["application.container-app:api", "cloudshell.network:default"],
            projection.References.Select(reference => reference.Value));
        var reconcile = await projection.GetReconcileOperationAsync();

        Assert.NotNull(reconcile);
        Assert.True(await reconcile.CanExecuteAsync());
        Assert.Equal("logical", reconcile.PlanReconcile().RoutingMode);
    }

    [Fact]
    public async Task AddContainerHostResourceType_RegistersCompleteResourceTypeBoundary()
    {
        var services = new ServiceCollection();
        services.AddContainerHostResourceType();
        services.AddResourceModelGraphServices();
        using var serviceProvider = services.BuildServiceProvider();
        var definition = new ResourceDefinition(
            "docker",
            ContainerHostResourceTypeProvider.ResourceTypeId,
            Attributes: new Dictionary<ResourceAttributeId, string>
            {
                [ContainerHostResourceTypeProvider.Attributes.HostKind] = "Docker",
                [ContainerHostResourceTypeProvider.Attributes.Endpoint] = "unix:///var/run/docker.sock",
                [ContainerHostResourceTypeProvider.Attributes.Registry] = "docker.io",
                [ContainerHostResourceTypeProvider.Attributes.IsDefault] = bool.TrueString.ToLowerInvariant()
            });

        var validation = await serviceProvider
            .GetRequiredService<ResourceDefinitionValidationPipeline>()
            .ValidateAsync(
                definition,
                new ResourceDefinitionValidationContext("local", "developer"));

        Assert.False(validation.HasErrors);
        Assert.Equal(ContainerHostResourceTypeProvider.ClassId, validation.Resource.Class.ClassId);
        Assert.Equal("containerHost", validation.Resource.Attributes.GetString(
            ContainerHostResourceTypeProvider.Attributes.InfrastructureKind));
        Assert.Equal("Docker", validation.Resource.Attributes.GetString(
            ContainerHostResourceTypeProvider.Attributes.HostKind));
        Assert.True(validation.Resource.Capabilities.Has(
            ContainerHostResourceTypeProvider.Capabilities.ContainerImage));
        Assert.True(validation.Resource.Capabilities.Has(
            ContainerHostResourceTypeProvider.Capabilities.ContainerBuild));
        Assert.True(validation.Resource.Capabilities.Has(
            ContainerHostResourceTypeProvider.Capabilities.StorageMountFileSystem));
        Assert.True(validation.Resource.Operations.Has(
            ContainerHostResourceTypeProvider.Operations.Inspect));

        var projectedGraph = await serviceProvider
            .GetRequiredService<ResourceDefinitionGraphProjectionResolver>()
            .ProjectAsync(
                new ResourceDefinitionGraphValidationPipelineResult(
                    new ResourceDefinitionGraph([definition]),
                    [validation],
                    []),
                new ResourceProjectionContext("local", "developer"));
        var projection = projectedGraph.Find<ContainerHostResource>(
            definition.EffectiveResourceId);

        Assert.NotNull(projection);
        Assert.Equal("Docker", projection.HostKind);
        Assert.Equal("unix:///var/run/docker.sock", projection.Endpoint);
        Assert.Equal("docker.io", projection.Registry);
        Assert.True(projection.IsDefault);
        Assert.True(projection.SupportsContainerImages);
        Assert.True(projection.SupportsContainerBuild);
        Assert.True(projection.SupportsFileSystemMounts);

        var inspect = await projection.GetInspectOperationAsync();

        Assert.NotNull(inspect);
        Assert.True(await inspect.CanExecuteAsync());
        Assert.Equal("Docker", inspect.PlanInspection().HostKind);
    }

    [Fact]
    public async Task AddDockerHostResourceType_RegistersCompleteResourceTypeBoundary()
    {
        var services = new ServiceCollection();
        services.AddDockerHostResourceType();
        services.AddResourceModelGraphServices();
        using var serviceProvider = services.BuildServiceProvider();
        var definition = new ResourceDefinition(
            "engine",
            DockerHostResourceTypeProvider.ResourceTypeId,
            Attributes: new Dictionary<ResourceAttributeId, string>
            {
                [DockerHostResourceTypeProvider.Attributes.HostKind] = "local",
                [DockerHostResourceTypeProvider.Attributes.Endpoint] = "unix:///var/run/docker.sock",
                [DockerHostResourceTypeProvider.Attributes.Registry] = "docker.io",
                [DockerHostResourceTypeProvider.Attributes.IsDefault] = bool.TrueString.ToLowerInvariant()
            });

        var validation = await serviceProvider
            .GetRequiredService<ResourceDefinitionValidationPipeline>()
            .ValidateAsync(
                definition,
                new ResourceDefinitionValidationContext("local", "developer"));

        Assert.False(validation.HasErrors);
        Assert.Equal(DockerHostResourceTypeProvider.ClassId, validation.Resource.Class.ClassId);
        Assert.Equal("containerHost", validation.Resource.Attributes.GetString(
            DockerHostResourceTypeProvider.Attributes.InfrastructureKind));
        Assert.Equal("local", validation.Resource.Attributes.GetString(
            DockerHostResourceTypeProvider.Attributes.HostKind));
        Assert.True(validation.Resource.Capabilities.Has(
            DockerHostResourceTypeProvider.Capabilities.ContainerImage));
        Assert.True(validation.Resource.Capabilities.Has(
            DockerHostResourceTypeProvider.Capabilities.ContainerBuild));
        Assert.True(validation.Resource.Capabilities.Has(
            DockerHostResourceTypeProvider.Capabilities.StorageMountFileSystem));
        Assert.True(validation.Resource.Operations.Has(
            DockerHostResourceTypeProvider.Operations.Inspect));

        var projectedGraph = await serviceProvider
            .GetRequiredService<ResourceDefinitionGraphProjectionResolver>()
            .ProjectAsync(
                new ResourceDefinitionGraphValidationPipelineResult(
                    new ResourceDefinitionGraph([definition]),
                    [validation],
                    []),
                new ResourceProjectionContext("local", "developer"));
        var projection = projectedGraph.Find<DockerHostResource>(
            definition.EffectiveResourceId);

        Assert.NotNull(projection);
        Assert.Equal("local", projection.HostKind);
        Assert.Equal("unix:///var/run/docker.sock", projection.Endpoint);
        Assert.Equal("docker.io", projection.Registry);
        Assert.True(projection.IsDefault);
        Assert.True(projection.SupportsContainerImages);
        Assert.True(projection.SupportsContainerBuild);
        Assert.True(projection.SupportsFileSystemMounts);
        var inspect = await projection.GetInspectOperationAsync();

        Assert.NotNull(inspect);
        Assert.True(await inspect.CanExecuteAsync());
        Assert.Equal("docker.io", inspect.PlanInspection().Registry);
    }

    [Fact]
    public async Task AddDockerContainerResourceType_RegistersCompleteResourceTypeBoundary()
    {
        var services = new ServiceCollection();
        services.AddDockerContainerResourceType();
        services.AddResourceModelGraphServices();
        using var serviceProvider = services.BuildServiceProvider();
        var definition = new ResourceDefinition(
            "api",
            DockerContainerResourceTypeProvider.ResourceTypeId,
            Attributes: new Dictionary<ResourceAttributeId, string>
            {
                [DockerContainerResourceTypeProvider.Attributes.ContainerImage] = "example/api:1.0",
                [DockerContainerResourceTypeProvider.Attributes.ContainerRegistry] = "registry.local"
            });

        var validation = await serviceProvider
            .GetRequiredService<ResourceDefinitionValidationPipeline>()
            .ValidateAsync(
                definition,
                new ResourceDefinitionValidationContext("local", "developer"));

        Assert.False(validation.HasErrors);
        Assert.Equal(DockerContainerResourceTypeProvider.ClassId, validation.Resource.Class.ClassId);
        Assert.Equal("ContainerImage", validation.Resource.Attributes.GetString(
            DockerContainerResourceTypeProvider.Attributes.WorkloadKind));
        Assert.Equal("example/api:1.0", validation.Resource.Attributes.GetString(
            DockerContainerResourceTypeProvider.Attributes.ContainerImage));
        Assert.Equal("registry.local", validation.Resource.Attributes.GetString(
            DockerContainerResourceTypeProvider.Attributes.ContainerRegistry));
        Assert.Equal("1", validation.Resource.Attributes.GetString(
            DockerContainerResourceTypeProvider.Attributes.ContainerReplicas));
        Assert.Equal("0", validation.Resource.Attributes.GetString(
            DockerContainerResourceTypeProvider.Attributes.EndpointCount));
        Assert.True(validation.Resource.Capabilities.Has(
            DockerContainerResourceTypeProvider.Capabilities.Monitoring));
        Assert.True(validation.Resource.Capabilities.Has(
            DockerContainerResourceTypeProvider.Capabilities.LogSources));
        Assert.True(validation.Resource.Operations.Has(
            DockerContainerResourceTypeProvider.Operations.Start));
        Assert.True(validation.Resource.Operations.Has(
            DockerContainerResourceTypeProvider.Operations.Unpause));

        var projectedGraph = await serviceProvider
            .GetRequiredService<ResourceDefinitionGraphProjectionResolver>()
            .ProjectAsync(
                new ResourceDefinitionGraphValidationPipelineResult(
                    new ResourceDefinitionGraph([definition]),
                    [validation],
                    []),
                new ResourceProjectionContext("local", "developer"));
        var projection = projectedGraph.Find<DockerContainerResource>(
            definition.EffectiveResourceId);

        Assert.NotNull(projection);
        Assert.Equal("example/api:1.0", projection.Image);
        Assert.Equal("registry.local", projection.Registry);
        Assert.Equal(1, projection.Replicas);
        Assert.Equal(0, projection.EndpointCount);
        Assert.True(projection.SupportsMonitoring);
        Assert.True(projection.SupportsLogSources);
        var start = await projection.GetStartOperationAsync();
        var unpause = await projection.GetUnpauseOperationAsync();

        Assert.NotNull(start);
        Assert.NotNull(unpause);
        Assert.True(await start.CanExecuteAsync());
        Assert.True(await unpause.CanExecuteAsync());
    }

    [Fact]
    public async Task AddConfigurationStoreResourceType_RegistersCompleteResourceTypeBoundary()
    {
        var services = new ServiceCollection();
        services.AddConfigurationStoreResourceType();
        services.AddResourceModelGraphServices();
        using var serviceProvider = services.BuildServiceProvider();
        var definition = new ResourceDefinition(
            "settings",
            ConfigurationStoreResourceTypeProvider.ResourceTypeId,
            Attributes: new Dictionary<ResourceAttributeId, string>
            {
                [ConfigurationStoreResourceTypeProvider.Attributes.Endpoint] = "http://localhost:5138",
                [ConfigurationStoreResourceTypeProvider.Attributes.EntryCount] = "3"
            });

        var validation = await serviceProvider
            .GetRequiredService<ResourceDefinitionValidationPipeline>()
            .ValidateAsync(
                definition,
                new ResourceDefinitionValidationContext("local", "developer"));

        Assert.False(validation.HasErrors);
        Assert.Equal(ConfigurationStoreResourceTypeProvider.ClassId, validation.Resource.Class.ClassId);
        Assert.Equal("store", validation.Resource.Attributes.GetString(
            ConfigurationStoreResourceTypeProvider.Attributes.ConfigurationKind));
        Assert.Equal("http://localhost:5138", validation.Resource.Attributes.GetString(
            ConfigurationStoreResourceTypeProvider.Attributes.Endpoint));
        Assert.Equal("3", validation.Resource.Attributes.GetString(
            ConfigurationStoreResourceTypeProvider.Attributes.EntryCount));
        Assert.True(validation.Resource.Operations.Has(
            ConfigurationStoreResourceTypeProvider.Operations.Inspect));

        var projectedGraph = await serviceProvider
            .GetRequiredService<ResourceDefinitionGraphProjectionResolver>()
            .ProjectAsync(
                new ResourceDefinitionGraphValidationPipelineResult(
                    new ResourceDefinitionGraph([definition]),
                    [validation],
                    []),
                new ResourceProjectionContext("local", "developer"));
        var projection = projectedGraph.Find<ConfigurationStoreResource>(
            definition.EffectiveResourceId);

        Assert.NotNull(projection);
        Assert.Equal("store", projection.ConfigurationKind);
        Assert.Equal("http://localhost:5138", projection.Endpoint);
        Assert.Equal(3, projection.EntryCount);
        var inspect = await projection.GetInspectOperationAsync();

        Assert.NotNull(inspect);
        Assert.True(await inspect.CanExecuteAsync());
        Assert.Equal(3, inspect.PlanInspection().EntryCount);
    }

    [Fact]
    public async Task AddHostConfigurationSourceResourceType_RegistersCompleteResourceTypeBoundary()
    {
        var services = new ServiceCollection();
        services.AddHostConfigurationSourceResourceType();
        services.AddResourceModelGraphServices();
        using var serviceProvider = services.BuildServiceProvider();
        var definition = new ResourceDefinition(
            "host-settings",
            HostConfigurationSourceResourceTypeProvider.ResourceTypeId,
            Attributes: new Dictionary<ResourceAttributeId, string>
            {
                [HostConfigurationSourceResourceTypeProvider.Attributes.EntryCount] = "4"
            });

        var validation = await serviceProvider
            .GetRequiredService<ResourceDefinitionValidationPipeline>()
            .ValidateAsync(
                definition,
                new ResourceDefinitionValidationContext("local", "developer"));

        Assert.False(validation.HasErrors);
        Assert.Equal(HostConfigurationSourceResourceTypeProvider.ClassId, validation.Resource.Class.ClassId);
        Assert.Equal("host", validation.Resource.Attributes.GetString(
            HostConfigurationSourceResourceTypeProvider.Attributes.ConfigurationKind));
        Assert.Equal("host", validation.Resource.Attributes.GetString(
            HostConfigurationSourceResourceTypeProvider.Attributes.Source));
        Assert.Equal("4", validation.Resource.Attributes.GetString(
            HostConfigurationSourceResourceTypeProvider.Attributes.EntryCount));
        Assert.True(validation.Resource.Operations.Has(
            HostConfigurationSourceResourceTypeProvider.Operations.Inspect));

        var projectedGraph = await serviceProvider
            .GetRequiredService<ResourceDefinitionGraphProjectionResolver>()
            .ProjectAsync(
                new ResourceDefinitionGraphValidationPipelineResult(
                    new ResourceDefinitionGraph([definition]),
                    [validation],
                    []),
                new ResourceProjectionContext("local", "developer"));
        var projection = projectedGraph.Find<HostConfigurationSourceResource>(
            definition.EffectiveResourceId);

        Assert.NotNull(projection);
        Assert.Equal("host", projection.ConfigurationKind);
        Assert.Equal("host", projection.Source);
        Assert.Equal(4, projection.EntryCount);
        var inspect = await projection.GetInspectOperationAsync();

        Assert.NotNull(inspect);
        Assert.True(await inspect.CanExecuteAsync());
        Assert.Equal(4, inspect.PlanInspection().EntryCount);
    }

    [Fact]
    public async Task AddLoadBalancerResourceType_RegistersCompleteResourceTypeBoundary()
    {
        var services = new ServiceCollection();
        services.AddLoadBalancerResourceType();
        services.AddResourceModelGraphServices();
        using var serviceProvider = services.BuildServiceProvider();
        var definition = new ResourceDefinition(
            "edge",
            LoadBalancerResourceTypeProvider.ResourceTypeId,
            Attributes: new Dictionary<ResourceAttributeId, string>
            {
                [LoadBalancerResourceTypeProvider.Attributes.Provider] = "traefik",
                [LoadBalancerResourceTypeProvider.Attributes.HostResourceId] = "docker:engine",
                [LoadBalancerResourceTypeProvider.Attributes.EntrypointCount] = "2",
                [LoadBalancerResourceTypeProvider.Attributes.RouteCount] = "3",
                [LoadBalancerResourceTypeProvider.Attributes.HttpRouteCount] = "2",
                [LoadBalancerResourceTypeProvider.Attributes.TcpRouteCount] = "1",
                [LoadBalancerResourceTypeProvider.Attributes.EndpointCount] = "2"
            });

        var validation = await serviceProvider
            .GetRequiredService<ResourceDefinitionValidationPipeline>()
            .ValidateAsync(
                definition,
                new ResourceDefinitionValidationContext("local", "developer"));

        Assert.False(validation.HasErrors);
        Assert.Equal(LoadBalancerResourceTypeProvider.ClassId, validation.Resource.Class.ClassId);
        Assert.Equal("traefik", validation.Resource.Attributes.GetString(
            LoadBalancerResourceTypeProvider.Attributes.Provider));
        Assert.Equal("docker:engine", validation.Resource.Attributes.GetString(
            LoadBalancerResourceTypeProvider.Attributes.HostResourceId));
        Assert.True(validation.Resource.Capabilities.Has(
            LoadBalancerResourceTypeProvider.Capabilities.NetworkingLoadBalancer));
        Assert.True(validation.Resource.Operations.Has(
            LoadBalancerResourceTypeProvider.Operations.ApplyConfiguration));

        var projectedGraph = await serviceProvider
            .GetRequiredService<ResourceDefinitionGraphProjectionResolver>()
            .ProjectAsync(
                new ResourceDefinitionGraphValidationPipelineResult(
                    new ResourceDefinitionGraph([definition]),
                    [validation],
                    []),
                new ResourceProjectionContext("local", "developer"));
        var projection = projectedGraph.Find<LoadBalancerResource>(
            definition.EffectiveResourceId);

        Assert.NotNull(projection);
        Assert.Equal("traefik", projection.Provider);
        Assert.Equal("docker:engine", projection.HostResourceId);
        Assert.Equal(2, projection.EntrypointCount);
        Assert.Equal(3, projection.RouteCount);
        Assert.Equal(2, projection.HttpRouteCount);
        Assert.Equal(1, projection.TcpRouteCount);
        Assert.True(projection.SupportsLoadBalancing);
        var apply = await projection.GetApplyConfigurationOperationAsync();

        Assert.NotNull(apply);
        Assert.True(await apply.CanExecuteAsync());
        Assert.Equal(3, apply.PlanApply().RouteCount);
    }

    [Fact]
    public async Task AddNetworkResourceType_RegistersCompleteResourceTypeBoundary()
    {
        var services = new ServiceCollection();
        services.AddNetworkResourceType();
        services.AddResourceModelGraphServices();
        using var serviceProvider = services.BuildServiceProvider();
        var definition = new ResourceDefinition(
            "edge-network",
            NetworkResourceTypeProvider.ResourceTypeId,
            Attributes: new Dictionary<ResourceAttributeId, string>
            {
                [NetworkResourceTypeProvider.Attributes.NetworkKind] = "Virtual",
                [NetworkResourceTypeProvider.Attributes.HostReadiness] = "providerRequired",
                [NetworkResourceTypeProvider.Attributes.MappingProviders] = "traefik"
            });

        var validation = await serviceProvider
            .GetRequiredService<ResourceDefinitionValidationPipeline>()
            .ValidateAsync(
                definition,
                new ResourceDefinitionValidationContext("local", "developer"));

        Assert.False(validation.HasErrors);
        Assert.Equal(NetworkResourceTypeProvider.ClassId, validation.Resource.Class.ClassId);
        Assert.Equal("Virtual", validation.Resource.Attributes.GetString(
            NetworkResourceTypeProvider.Attributes.NetworkKind));
        Assert.Equal("providerRequired", validation.Resource.Attributes.GetString(
            NetworkResourceTypeProvider.Attributes.HostReadiness));
        Assert.True(validation.Resource.Capabilities.Has(
            NetworkResourceTypeProvider.Capabilities.NetworkingEndpointMapper));
        Assert.True(validation.Resource.Operations.Has(
            NetworkResourceTypeProvider.Operations.ReconcileEndpointMappings));

        var projectedGraph = await serviceProvider
            .GetRequiredService<ResourceDefinitionGraphProjectionResolver>()
            .ProjectAsync(
                new ResourceDefinitionGraphValidationPipelineResult(
                    new ResourceDefinitionGraph([definition]),
                    [validation],
                    []),
                new ResourceProjectionContext("local", "developer"));
        var projection = projectedGraph.Find<NetworkResource>(
            definition.EffectiveResourceId);

        Assert.NotNull(projection);
        Assert.Equal("Virtual", projection.NetworkKind);
        Assert.Equal("providerRequired", projection.HostReadiness);
        Assert.Equal("traefik", projection.MappingProviders);
        Assert.True(projection.SupportsEndpointMapping);
        var reconcile = await projection.GetReconcileEndpointMappingsOperationAsync();

        Assert.NotNull(reconcile);
        Assert.True(await reconcile.CanExecuteAsync());
        Assert.Equal("traefik", reconcile.PlanReconcile().MappingProviders);
    }

    [Fact]
    public async Task AddVirtualNetworkResourceType_RegistersCompleteResourceTypeBoundary()
    {
        var services = new ServiceCollection();
        services.AddVirtualNetworkResourceType();
        services.AddResourceModelGraphServices();
        using var serviceProvider = services.BuildServiceProvider();
        var definition = new ResourceDefinition(
            "app",
            VirtualNetworkResourceTypeProvider.ResourceTypeId,
            Attributes: new Dictionary<ResourceAttributeId, string>
            {
                [VirtualNetworkResourceTypeProvider.Attributes.IsDefault] = bool.TrueString.ToLowerInvariant(),
                [VirtualNetworkResourceTypeProvider.Attributes.HostReadiness] = "providerRequired",
                [VirtualNetworkResourceTypeProvider.Attributes.MappingProviders] = "cloudshell.loadBalancer:edge"
            });

        var validation = await serviceProvider
            .GetRequiredService<ResourceDefinitionValidationPipeline>()
            .ValidateAsync(
                definition,
                new ResourceDefinitionValidationContext("local", "developer"));

        Assert.False(validation.HasErrors);
        Assert.Equal(VirtualNetworkResourceTypeProvider.ClassId, validation.Resource.Class.ClassId);
        Assert.Equal("Virtual", validation.Resource.Attributes.GetString(
            VirtualNetworkResourceTypeProvider.Attributes.NetworkKind));
        Assert.Equal(bool.TrueString.ToLowerInvariant(), validation.Resource.Attributes.GetString(
            VirtualNetworkResourceTypeProvider.Attributes.IsDefault));
        Assert.True(validation.Resource.Capabilities.Has(
            VirtualNetworkResourceTypeProvider.Capabilities.NetworkingVirtualNetwork));
        Assert.True(validation.Resource.Capabilities.Has(
            VirtualNetworkResourceTypeProvider.Capabilities.NetworkingIngress));
        Assert.True(validation.Resource.Operations.Has(
            VirtualNetworkResourceTypeProvider.Operations.ReconcileEndpointMappings));

        var projectedGraph = await serviceProvider
            .GetRequiredService<ResourceDefinitionGraphProjectionResolver>()
            .ProjectAsync(
                new ResourceDefinitionGraphValidationPipelineResult(
                    new ResourceDefinitionGraph([definition]),
                    [validation],
                    []),
                new ResourceProjectionContext("local", "developer"));
        var projection = projectedGraph.Find<VirtualNetworkResource>(
            definition.EffectiveResourceId);

        Assert.NotNull(projection);
        Assert.True(projection.IsDefault);
        Assert.Equal("providerRequired", projection.HostReadiness);
        Assert.True(projection.SupportsEndpointMapping);
        Assert.True(projection.SupportsIngress);
        var reconcile = await projection.GetReconcileEndpointMappingsOperationAsync();

        Assert.NotNull(reconcile);
        Assert.True(await reconcile.CanExecuteAsync());
        Assert.Equal("cloudshell.loadBalancer:edge", reconcile.PlanReconcile().MappingProviders);
    }

    [Fact]
    public async Task AddLocalHostNetworkResourceType_RegistersCompleteResourceTypeBoundary()
    {
        var services = new ServiceCollection();
        services.AddLocalHostNetworkResourceType();
        services.AddResourceModelGraphServices();
        using var serviceProvider = services.BuildServiceProvider();
        var definition = new ResourceDefinition(
            "host-local",
            LocalHostNetworkResourceTypeProvider.ResourceTypeId);

        var validation = await serviceProvider
            .GetRequiredService<ResourceDefinitionValidationPipeline>()
            .ValidateAsync(
                definition,
                new ResourceDefinitionValidationContext("local", "developer"));

        Assert.False(validation.HasErrors);
        Assert.Equal(LocalHostNetworkResourceTypeProvider.ClassId, validation.Resource.Class.ClassId);
        Assert.Equal("hostNetworking", validation.Resource.Attributes.GetString(
            LocalHostNetworkResourceTypeProvider.Attributes.InfrastructureKind));
        Assert.Equal("ready", validation.Resource.Attributes.GetString(
            LocalHostNetworkResourceTypeProvider.Attributes.HostReadiness));
        Assert.Equal("cross-platform", validation.Resource.Attributes.GetString(
            LocalHostNetworkResourceTypeProvider.Attributes.HostOperatingSystem));
        Assert.Equal("localProxy", validation.Resource.Attributes.GetString(
            LocalHostNetworkResourceTypeProvider.Attributes.NetworkingMode));
        Assert.True(validation.Resource.Capabilities.Has(
            LocalHostNetworkResourceTypeProvider.Capabilities.NetworkingHostNetwork));
        Assert.True(validation.Resource.Operations.Has(
            LocalHostNetworkResourceTypeProvider.Operations.ReconcileEndpointMappings));

        var projectedGraph = await serviceProvider
            .GetRequiredService<ResourceDefinitionGraphProjectionResolver>()
            .ProjectAsync(
                new ResourceDefinitionGraphValidationPipelineResult(
                    new ResourceDefinitionGraph([definition]),
                    [validation],
                    []),
                new ResourceProjectionContext("local", "developer"));
        var projection = projectedGraph.Find<LocalHostNetworkResource>(
            definition.EffectiveResourceId);

        Assert.NotNull(projection);
        Assert.True(projection.SupportsEndpointMapping);
        Assert.True(projection.SupportsHostNetwork);
        Assert.Equal("localProxy", projection.NetworkingMode);
        var reconcile = await projection.GetReconcileEndpointMappingsOperationAsync();

        Assert.NotNull(reconcile);
        Assert.True(await reconcile.CanExecuteAsync());
        Assert.Equal("localProxy", reconcile.PlanReconcile().NetworkingMode);
    }

    [Fact]
    public async Task AddMacOSHostNetworkResourceType_RegistersCompleteResourceTypeBoundary()
    {
        var services = new ServiceCollection();
        services.AddMacOSHostNetworkResourceType();
        services.AddResourceModelGraphServices();
        using var serviceProvider = services.BuildServiceProvider();
        var definition = new ResourceDefinition(
            "host-macos",
            MacOSHostNetworkResourceTypeProvider.ResourceTypeId);

        var validation = await serviceProvider
            .GetRequiredService<ResourceDefinitionValidationPipeline>()
            .ValidateAsync(
                definition,
                new ResourceDefinitionValidationContext("local", "developer"));

        Assert.False(validation.HasErrors);
        Assert.Equal(MacOSHostNetworkResourceTypeProvider.ClassId, validation.Resource.Class.ClassId);
        Assert.Equal("hostNetworking", validation.Resource.Attributes.GetString(
            MacOSHostNetworkResourceTypeProvider.Attributes.InfrastructureKind));
        Assert.Equal("ready", validation.Resource.Attributes.GetString(
            MacOSHostNetworkResourceTypeProvider.Attributes.HostReadiness));
        Assert.Equal("macos", validation.Resource.Attributes.GetString(
            MacOSHostNetworkResourceTypeProvider.Attributes.HostOperatingSystem));
        Assert.Equal("localProxy", validation.Resource.Attributes.GetString(
            MacOSHostNetworkResourceTypeProvider.Attributes.NetworkingMode));
        Assert.True(validation.Resource.Capabilities.Has(
            MacOSHostNetworkResourceTypeProvider.Capabilities.NetworkingHostNetwork));
        Assert.True(validation.Resource.Operations.Has(
            MacOSHostNetworkResourceTypeProvider.Operations.ReconcileEndpointMappings));

        var projectedGraph = await serviceProvider
            .GetRequiredService<ResourceDefinitionGraphProjectionResolver>()
            .ProjectAsync(
                new ResourceDefinitionGraphValidationPipelineResult(
                    new ResourceDefinitionGraph([definition]),
                    [validation],
                    []),
                new ResourceProjectionContext("local", "developer"));
        var projection = projectedGraph.Find<MacOSHostNetworkResource>(
            definition.EffectiveResourceId);

        Assert.NotNull(projection);
        Assert.True(projection.SupportsEndpointMapping);
        Assert.True(projection.SupportsHostNetwork);
        Assert.Equal("macos", projection.HostOperatingSystem);
        var reconcile = await projection.GetReconcileEndpointMappingsOperationAsync();

        Assert.NotNull(reconcile);
        Assert.True(await reconcile.CanExecuteAsync());
        Assert.Equal("macos", reconcile.PlanReconcile().HostOperatingSystem);
    }

    [Fact]
    public async Task AddDnsZoneResourceType_RegistersCompleteResourceTypeBoundary()
    {
        var services = new ServiceCollection();
        services.AddDnsZoneResourceType();
        services.AddResourceModelGraphServices();
        using var serviceProvider = services.BuildServiceProvider();
        var definition = new ResourceDefinition(
            "local",
            DnsZoneResourceTypeProvider.ResourceTypeId,
            Attributes: new Dictionary<ResourceAttributeId, string>
            {
                [DnsZoneResourceTypeProvider.Attributes.ZoneName] = "local",
                [DnsZoneResourceTypeProvider.Attributes.Provider] = "hosts-file"
            });

        var validation = await serviceProvider
            .GetRequiredService<ResourceDefinitionValidationPipeline>()
            .ValidateAsync(
                definition,
                new ResourceDefinitionValidationContext("local", "developer"));

        Assert.False(validation.HasErrors);
        Assert.Equal(DnsZoneResourceTypeProvider.ClassId, validation.Resource.Class.ClassId);
        Assert.Equal("local", validation.Resource.Attributes.GetString(
            DnsZoneResourceTypeProvider.Attributes.ZoneName));
        Assert.Equal("hosts-file", validation.Resource.Attributes.GetString(
            DnsZoneResourceTypeProvider.Attributes.Provider));
        Assert.True(validation.Resource.Capabilities.Has(
            DnsZoneResourceTypeProvider.Capabilities.NetworkingDnsZone));
        Assert.True(validation.Resource.Operations.Has(
            DnsZoneResourceTypeProvider.Operations.ReconcileNameMappings));

        var projectedGraph = await serviceProvider
            .GetRequiredService<ResourceDefinitionGraphProjectionResolver>()
            .ProjectAsync(
                new ResourceDefinitionGraphValidationPipelineResult(
                    new ResourceDefinitionGraph([definition]),
                    [validation],
                    []),
                new ResourceProjectionContext("local", "developer"));
        var projection = projectedGraph.Find<DnsZoneResource>(
            definition.EffectiveResourceId);

        Assert.NotNull(projection);
        Assert.Equal("local", projection.ZoneName);
        Assert.Equal("hosts-file", projection.Provider);
        Assert.True(projection.SupportsNameMapping);
        var reconcile = await projection.GetReconcileNameMappingsOperationAsync();

        Assert.NotNull(reconcile);
        Assert.True(await reconcile.CanExecuteAsync());
        Assert.Equal("local", reconcile.PlanReconcile().ZoneName);
        Assert.Equal("hosts-file", reconcile.PlanReconcile().Provider);
    }

    [Fact]
    public async Task AddNameMappingResourceType_RegistersCompleteResourceTypeBoundary()
    {
        var services = new ServiceCollection();
        services.AddNameMappingResourceType();
        services.AddResourceModelGraphServices();
        using var serviceProvider = services.BuildServiceProvider();
        var definition = new ResourceDefinition(
            "api-local",
            NameMappingResourceTypeProvider.ResourceTypeId,
            DependsOn:
            [
                ResourceReference.ResourceId("cloudshell.dnsZone:local"),
                ResourceReference.ResourceId("application.executable:api")
            ],
            Attributes: new Dictionary<ResourceAttributeId, string>
            {
                [NameMappingResourceTypeProvider.Attributes.HostName] = "api.local",
                [NameMappingResourceTypeProvider.Attributes.TargetEndpointName] = "http",
                [NameMappingResourceTypeProvider.Attributes.Exposure] = "Public"
            });

        var validation = await serviceProvider
            .GetRequiredService<ResourceDefinitionValidationPipeline>()
            .ValidateAsync(
                definition,
                new ResourceDefinitionValidationContext("local", "developer"));

        Assert.False(validation.HasErrors);
        Assert.Equal(NameMappingResourceTypeProvider.ClassId, validation.Resource.Class.ClassId);
        Assert.Equal("api.local", validation.Resource.Attributes.GetString(
            NameMappingResourceTypeProvider.Attributes.HostName));
        Assert.Equal("http", validation.Resource.Attributes.GetString(
            NameMappingResourceTypeProvider.Attributes.TargetEndpointName));
        Assert.True(validation.Resource.Capabilities.Has(
            NameMappingResourceTypeProvider.Capabilities.NetworkingNameMapping));

        var projectedGraph = await serviceProvider
            .GetRequiredService<ResourceDefinitionGraphProjectionResolver>()
            .ProjectAsync(
                new ResourceDefinitionGraphValidationPipelineResult(
                    new ResourceDefinitionGraph([definition]),
                    [validation],
                    []),
                new ResourceProjectionContext("local", "developer"));
        var projection = projectedGraph.Find<NameMappingResource>(
            definition.EffectiveResourceId);

        Assert.NotNull(projection);
        Assert.Equal("api.local", projection.HostName);
        Assert.Equal("http", projection.TargetEndpointName);
        Assert.Equal("Public", projection.Exposure);
        Assert.True(projection.SupportsNameMapping);
        Assert.Equal(
            ["cloudshell.dnsZone:local", "application.executable:api"],
            projection.References.Select(reference => reference.Value));
    }

    [Fact]
    public async Task AddSecretsVaultResourceType_RegistersCompleteResourceTypeBoundary()
    {
        var services = new ServiceCollection();
        services.AddSecretsVaultResourceType();
        services.AddResourceModelGraphServices();
        using var serviceProvider = services.BuildServiceProvider();
        var definition = new ResourceDefinition(
            "vault",
            SecretsVaultResourceTypeProvider.ResourceTypeId,
            Attributes: new Dictionary<ResourceAttributeId, string>
            {
                [SecretsVaultResourceTypeProvider.Attributes.Endpoint] = "http://localhost:6138",
                [SecretsVaultResourceTypeProvider.Attributes.SecretCount] = "2"
            });

        var validation = await serviceProvider
            .GetRequiredService<ResourceDefinitionValidationPipeline>()
            .ValidateAsync(
                definition,
                new ResourceDefinitionValidationContext("local", "developer"));

        Assert.False(validation.HasErrors);
        Assert.Equal(SecretsVaultResourceTypeProvider.ClassId, validation.Resource.Class.ClassId);
        Assert.Equal("vault", validation.Resource.Attributes.GetString(
            SecretsVaultResourceTypeProvider.Attributes.SecretsKind));
        Assert.Equal("http://localhost:6138", validation.Resource.Attributes.GetString(
            SecretsVaultResourceTypeProvider.Attributes.Endpoint));
        Assert.Equal("2", validation.Resource.Attributes.GetString(
            SecretsVaultResourceTypeProvider.Attributes.SecretCount));
        Assert.True(validation.Resource.Operations.Has(
            SecretsVaultResourceTypeProvider.Operations.Inspect));

        var projectedGraph = await serviceProvider
            .GetRequiredService<ResourceDefinitionGraphProjectionResolver>()
            .ProjectAsync(
                new ResourceDefinitionGraphValidationPipelineResult(
                    new ResourceDefinitionGraph([definition]),
                    [validation],
                    []),
                new ResourceProjectionContext("local", "developer"));
        var projection = projectedGraph.Find<SecretsVaultResource>(
            definition.EffectiveResourceId);

        Assert.NotNull(projection);
        Assert.Equal("vault", projection.SecretsKind);
        Assert.Equal("http://localhost:6138", projection.Endpoint);
        Assert.Equal(2, projection.SecretCount);
        var inspect = await projection.GetInspectOperationAsync();

        Assert.NotNull(inspect);
        Assert.True(await inspect.CanExecuteAsync());
        Assert.Equal(2, inspect.PlanInspection().SecretCount);
    }

    [Fact]
    public async Task AddIdentityProvisioningResourceType_RegistersCompleteResourceTypeBoundary()
    {
        var services = new ServiceCollection();
        services.AddIdentityProvisioningResourceType();
        services.AddResourceModelGraphServices();
        using var serviceProvider = services.BuildServiceProvider();
        var definition = new ResourceDefinition(
            "built-in",
            IdentityProvisioningResourceTypeProvider.ResourceTypeId,
            Attributes: new Dictionary<ResourceAttributeId, string>
            {
                [IdentityProvisioningResourceTypeProvider.Attributes.IdentityProvider] = "Built-in Identity",
                [IdentityProvisioningResourceTypeProvider.Attributes.ProviderKind] = "built-in"
            });

        var validation = await serviceProvider
            .GetRequiredService<ResourceDefinitionValidationPipeline>()
            .ValidateAsync(
                definition,
                new ResourceDefinitionValidationContext("local", "developer"));

        Assert.False(validation.HasErrors);
        Assert.Equal(IdentityProvisioningResourceTypeProvider.ClassId, validation.Resource.Class.ClassId);
        Assert.Equal("identity-provisioning", validation.Resource.Attributes.GetString(
            IdentityProvisioningResourceTypeProvider.Attributes.InfrastructureKind));
        Assert.Equal("Built-in Identity", validation.Resource.Attributes.GetString(
            IdentityProvisioningResourceTypeProvider.Attributes.IdentityProvider));
        Assert.True(validation.Resource.Capabilities.Has(
            IdentityProvisioningResourceTypeProvider.Capabilities.IdentityProvisioning));
        Assert.True(validation.Resource.Operations.Has(
            IdentityProvisioningResourceTypeProvider.Operations.Setup));

        var projectedGraph = await serviceProvider
            .GetRequiredService<ResourceDefinitionGraphProjectionResolver>()
            .ProjectAsync(
                new ResourceDefinitionGraphValidationPipelineResult(
                    new ResourceDefinitionGraph([definition]),
                    [validation],
                    []),
                new ResourceProjectionContext("local", "developer"));
        var projection = projectedGraph.Find<IdentityProvisioningResource>(
            definition.EffectiveResourceId);

        Assert.NotNull(projection);
        Assert.Equal("Built-in Identity", projection.IdentityProvider);
        Assert.Equal("built-in", projection.ProviderKind);
        Assert.True(projection.SupportsIdentityProvisioning);
        var setup = await projection.GetSetupOperationAsync();

        Assert.NotNull(setup);
        Assert.True(await setup.CanExecuteAsync());
        Assert.Equal("Built-in Identity", setup.PlanSetup().IdentityProvider);
    }

    [Fact]
    public async Task AddContainerApplicationResourceType_RegistersCompleteResourceTypeBoundary()
    {
        var services = new ServiceCollection();
        services.AddLocalVolumeResourceType();
        services.AddContainerHostResourceType();
        services.AddContainerApplicationResourceType();
        services.AddResourceModelGraphServices();
        using var serviceProvider = services.BuildServiceProvider();
        var volume = new ResourceDefinition(
            "data",
            LocalVolumeResourceTypeProvider.ResourceTypeId);
        var host = new ResourceDefinition(
            "docker",
            ContainerHostResourceTypeProvider.ResourceTypeId);
        var definition = new ResourceDefinition(
            "api",
            ContainerApplicationResourceTypeProvider.ResourceTypeId,
            DependsOn:
            [
                new(
                    host.EffectiveResourceId,
                    ResourceReferenceRelationships.DependsOn,
                    ResourceReferenceAddressingModes.ResourceId,
                    TypeId: ContainerHostResourceTypeProvider.ResourceTypeId)
            ],
            Attributes: new Dictionary<ResourceAttributeId, string>
            {
                [ContainerApplicationResourceTypeProvider.Attributes.ContainerImage] = "ghcr.io/example/api:latest",
                [ContainerApplicationResourceTypeProvider.Attributes.ContainerReplicas] = "2"
            },
            Capabilities: new Dictionary<ResourceCapabilityId, JsonElement>
            {
                [VolumeConsumerCapabilityProvider.CapabilityIdValue] =
                    ResourceDefinitionJson.FromValue(new VolumeConsumerDefinition(
                    [
                        new(volume.EffectiveResourceId, "/data")
                    ]))
            });

        var validation = await serviceProvider
            .GetRequiredService<ResourceDefinitionGraphValidationPipeline>()
            .ValidateAsync(
                new ResourceDefinitionGraph([host, volume, definition]),
                new ResourceDefinitionValidationContext("local", "developer"));

        Assert.False(validation.HasErrors);
        var containerValidation = validation.Resources.Single(resource =>
            resource.Resource.EffectiveResourceId == definition.EffectiveResourceId);
        Assert.Equal(ContainerApplicationResourceTypeProvider.ClassId, containerValidation.Resource.Class.ClassId);
        Assert.Equal("ghcr.io/example/api:latest", containerValidation.Resource.Attributes.GetString(
            ContainerApplicationResourceTypeProvider.Attributes.ContainerImage));
        Assert.Equal("2", containerValidation.Resource.Attributes.GetString(
            ContainerApplicationResourceTypeProvider.Attributes.ContainerReplicas));
        Assert.True(containerValidation.Resource.Capabilities.Has(
            VolumeConsumerCapabilityProvider.CapabilityIdValue));
        Assert.True(containerValidation.Resource.Operations.Has(
            ContainerApplicationResourceTypeProvider.Operations.Start));
        Assert.True(containerValidation.Resource.Operations.Has(
            ContainerApplicationResourceTypeProvider.Operations.Restart));
        Assert.True(containerValidation.Resource.Operations.Has(
            ContainerApplicationResourceTypeProvider.Operations.UpdateImage));

        var projectedGraph = await serviceProvider
            .GetRequiredService<ResourceDefinitionGraphProjectionResolver>()
            .ProjectAsync(
                validation,
                new ResourceProjectionContext("local", "developer"));
        var projection = projectedGraph.Find<ContainerApplicationResource>(
            definition.EffectiveResourceId);

        Assert.NotNull(projection);
        Assert.Equal("ghcr.io/example/api:latest", projection.Image);
        Assert.Equal(2, projection.Replicas);
        Assert.Equal(host.EffectiveResourceId, projection.ContainerHostResourceId);
        Assert.Equal(volume.EffectiveResourceId, Assert.Single(await projection.GetVolumesAsync()).Volume);
        Assert.True(await (await projection.GetStartOperationAsync())!.CanExecuteAsync());
        Assert.True(await (await projection.GetRestartOperationAsync())!.CanExecuteAsync());
        var imageUpdate = await projection.GetImageUpdateOperationAsync();

        Assert.NotNull(imageUpdate);
        Assert.True(await imageUpdate.CanExecuteAsync());

        var changes = imageUpdate.UpdateImage("ghcr.io/example/api:v2", replicas: 3);

        Assert.Equal(2, changes.AttributeChanges.Count);
        Assert.Equal("ghcr.io/example/api:v2", changes.ProposedState.ResourceAttributes[
            ContainerApplicationResourceTypeProvider.Attributes.ContainerImage]);
        Assert.Equal("3", changes.ProposedState.ResourceAttributes[
            ContainerApplicationResourceTypeProvider.Attributes.ContainerReplicas]);

        var applied = await serviceProvider
            .GetRequiredService<ResourceChangeApplyDispatcher>()
            .ApplyChangesAsync(
                changes,
                new ResourceChangeApplyContext("local", "developer"));

        Assert.True(applied.IsAccepted);
        Assert.Equal("ghcr.io/example/api:v2", applied.AcceptedState!.ResourceAttributes[
            ContainerApplicationResourceTypeProvider.Attributes.ContainerImage]);

        var applyPlan = await serviceProvider
            .GetRequiredService<ResourceDefinitionGraphApplyPlanner>()
            .PlanApplyAsync(
                validation,
                new ResourceDefinitionApplyContext("local", "developer"));

        Assert.False(applyPlan.HasErrors);
        Assert.Contains(applyPlan.Steps, step =>
            step.ResourceId == definition.EffectiveResourceId &&
            step.Kind == ResourceDefinitionApplyStepKind.MaterializeRuntime);
    }

    [Fact]
    public async Task AddSqlServerResourceType_RegistersCompleteResourceTypeBoundary()
    {
        var services = new ServiceCollection();
        services.AddLocalVolumeResourceType();
        services.AddSqlServerResourceType();
        services.AddResourceModelGraphServices();
        using var serviceProvider = services.BuildServiceProvider();
        var volume = new ResourceDefinition(
            "sql-data",
            LocalVolumeResourceTypeProvider.ResourceTypeId);
        var definition = new ResourceDefinition(
            "sql",
            SqlServerResourceTypeProvider.ResourceTypeId,
            Attributes: new Dictionary<ResourceAttributeId, string>
            {
                [SqlServerResourceTypeProvider.Attributes.Version] = "2022",
                [SqlServerResourceTypeProvider.Attributes.Edition] = "Developer"
            },
            Configuration: new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                [SqlServerResourceTypeProvider.ConfigurationSection] =
                    ResourceDefinitionJson.FromValue(new SqlServerConfiguration(
                    [
                        new("appdb", "Application DB", EnsureCreated: true)
                    ]))
            },
            Capabilities: new Dictionary<ResourceCapabilityId, JsonElement>
            {
                [VolumeConsumerCapabilityProvider.CapabilityIdValue] =
                    ResourceDefinitionJson.FromValue(new VolumeConsumerDefinition(
                    [
                        new(volume.EffectiveResourceId, "/var/opt/mssql")
                    ]))
            });

        var validation = await serviceProvider
            .GetRequiredService<ResourceDefinitionGraphValidationPipeline>()
            .ValidateAsync(
                new ResourceDefinitionGraph([volume, definition]),
                new ResourceDefinitionValidationContext("local", "developer"));

        Assert.False(validation.HasErrors);
        var sqlValidation = validation.Resources.Single(resource =>
            resource.Resource.EffectiveResourceId == definition.EffectiveResourceId);
        Assert.Equal(SqlServerResourceTypeProvider.ClassId, sqlValidation.Resource.Class.ClassId);
        Assert.Equal("2022", sqlValidation.Resource.Attributes.GetString(
            SqlServerResourceTypeProvider.Attributes.Version));
        Assert.True(sqlValidation.Resource.Capabilities.Has(
            VolumeConsumerCapabilityProvider.CapabilityIdValue));
        Assert.True(sqlValidation.Resource.Operations.Has(
            SqlServerResourceTypeProvider.Operations.ReconcileAccess));

        var projectedGraph = await serviceProvider
            .GetRequiredService<ResourceDefinitionGraphProjectionResolver>()
            .ProjectAsync(
                validation,
                new ResourceProjectionContext("local", "developer"));
        var projection = projectedGraph.Find<SqlServerResource>(
            definition.EffectiveResourceId);

        Assert.NotNull(projection);
        Assert.Equal("2022", projection.Version);
        Assert.Equal("Developer", projection.Edition);
        Assert.Equal("appdb", Assert.Single(projection.Databases).Name);

        var reconcile = await projection.GetReconcileAccessOperationAsync();

        Assert.NotNull(reconcile);
        Assert.True(await reconcile.CanExecuteAsync());
        Assert.Equal("appdb", Assert.Single(reconcile.PlanReconciliation().Databases).Name);

        var applyPlan = await serviceProvider
            .GetRequiredService<ResourceDefinitionGraphApplyPlanner>()
            .PlanApplyAsync(
                validation,
                new ResourceDefinitionApplyContext("local", "developer"));

        Assert.False(applyPlan.HasErrors);
        Assert.Contains(applyPlan.Steps, step =>
            step.ResourceId == definition.EffectiveResourceId &&
            step.Kind == ResourceDefinitionApplyStepKind.MaterializeRuntime);
    }

    [Fact]
    public async Task AddSqlDatabaseResourceType_RegistersCompleteResourceTypeBoundary()
    {
        var services = new ServiceCollection();
        services.AddSqlServerResourceType();
        services.AddSqlDatabaseResourceType();
        services.AddResourceModelGraphServices();
        using var serviceProvider = services.BuildServiceProvider();
        var server = new ResourceDefinition(
            "sql",
            SqlServerResourceTypeProvider.ResourceTypeId);
        var definition = new ResourceDefinition(
            "appdb",
            SqlDatabaseResourceTypeProvider.ResourceTypeId,
            DependsOn: [ResourceReference.ResourceId(server.EffectiveResourceId)],
            Attributes: new Dictionary<ResourceAttributeId, string>
            {
                [SqlDatabaseResourceTypeProvider.Attributes.DatabaseName] = "appdb",
                [SqlDatabaseResourceTypeProvider.Attributes.EnsureCreated] = bool.TrueString.ToLowerInvariant()
            });

        var validation = await serviceProvider
            .GetRequiredService<ResourceDefinitionGraphValidationPipeline>()
            .ValidateAsync(
                new ResourceDefinitionGraph([server, definition]),
                new ResourceDefinitionValidationContext("local", "developer"));

        Assert.False(validation.HasErrors);
        var databaseValidation = validation.Resources.Single(resource =>
            resource.Resource.EffectiveResourceId == definition.EffectiveResourceId);
        Assert.Equal(SqlDatabaseResourceTypeProvider.ClassId, databaseValidation.Resource.Class.ClassId);
        Assert.Equal("appdb", databaseValidation.Resource.Attributes.GetString(
            SqlDatabaseResourceTypeProvider.Attributes.DatabaseName));
        Assert.Equal("declared", databaseValidation.Resource.Attributes.GetString(
            SqlDatabaseResourceTypeProvider.Attributes.Source));
        Assert.True(databaseValidation.Resource.Operations.Has(
            SqlDatabaseResourceTypeProvider.Operations.EnsureCreated));

        var projectedGraph = await serviceProvider
            .GetRequiredService<ResourceDefinitionGraphProjectionResolver>()
            .ProjectAsync(
                validation,
                new ResourceProjectionContext("local", "developer"));
        var projection = projectedGraph.Find<SqlDatabaseResource>(
            definition.EffectiveResourceId);

        Assert.NotNull(projection);
        Assert.Equal("appdb", projection.DatabaseName);
        Assert.Equal(server.EffectiveResourceId, projection.ServerResourceId);
        Assert.True(projection.EnsureCreated);
        Assert.Equal("declared", projection.Source);

        var ensureCreated = await projection.GetEnsureCreatedOperationAsync();

        Assert.NotNull(ensureCreated);
        Assert.True(await ensureCreated.CanExecuteAsync());
        Assert.Equal(definition.EffectiveResourceId, ensureCreated.PlanEnsureCreated().ResourceId);

        var applyPlan = await serviceProvider
            .GetRequiredService<ResourceDefinitionGraphApplyPlanner>()
            .PlanApplyAsync(
                validation,
                new ResourceDefinitionApplyContext("local", "developer"));

        Assert.False(applyPlan.HasErrors);
        Assert.Contains(applyPlan.Steps, step =>
            step.ResourceId == definition.EffectiveResourceId &&
            step.Kind == ResourceDefinitionApplyStepKind.MaterializeRuntime);
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
    public async Task ValidateCapabilitiesAsync_AllowsPassiveCapabilityDeclarations()
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

        Assert.Empty(result.Diagnostics);
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
