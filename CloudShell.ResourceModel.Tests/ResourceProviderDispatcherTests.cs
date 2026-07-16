using System.Text.Json;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.Providers;
using CloudShell.ControlPlane.ResourceModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CloudShell.ResourceModel.Tests;

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
    public void AddBuiltInResourceModelProviderTypes_RegistersDefaultProviderCatalog()
    {
        var services = new ServiceCollection();
        services.AddBuiltInResourceModelProviderTypes();
        services.AddConfigurationStoreResourceType(runtime =>
        {
            runtime.ServiceProjectPath = "services/configuration-store.csproj";
            runtime.Settings.Add(new("Sample:Message", "Hello from graph"));
        });
        services.AddSecretsVaultResourceType(runtime =>
        {
            runtime.ServiceProjectPath = "services/secrets-vault.csproj";
            runtime.Secrets.Add(new("sample-api-key", "secret-value", "v1"));
        });
        using var serviceProvider = services.BuildServiceProvider();

        var typeIds = serviceProvider
            .GetServices<IResourceTypeProvider>()
            .Select(provider => provider.TypeId)
            .ToHashSet();

        Assert.Contains(ExecutableApplicationResourceTypeProvider.ResourceTypeId, typeIds);
        Assert.Contains(AspNetCoreProjectResourceTypeProvider.ResourceTypeId, typeIds);
        Assert.Contains(ContainerApplicationResourceTypeProvider.ResourceTypeId, typeIds);
        Assert.Contains(DockerHostResourceTypeProvider.ResourceTypeId, typeIds);
        Assert.Contains(DockerContainerResourceTypeProvider.ResourceTypeId, typeIds);
        Assert.Contains(ContainerHostResourceTypeProvider.ResourceTypeId, typeIds);
        Assert.Contains(StorageResourceTypeProvider.ResourceTypeId, typeIds);
        Assert.Contains(CloudShellVolumeResourceTypeProvider.ResourceTypeId, typeIds);
        Assert.Contains(LocalVolumeResourceTypeProvider.ResourceTypeId, typeIds);
        Assert.Contains(SqlServerResourceTypeProvider.ResourceTypeId, typeIds);
        Assert.Contains(SqlDatabaseResourceTypeProvider.ResourceTypeId, typeIds);
        Assert.Contains(ConfigurationStoreResourceTypeProvider.ResourceTypeId, typeIds);
        Assert.Contains(SecretsVaultResourceTypeProvider.ResourceTypeId, typeIds);
        Assert.Contains(HostConfigurationSourceResourceTypeProvider.ResourceTypeId, typeIds);
        Assert.Contains(IdentityProvisioningResourceTypeProvider.ResourceTypeId, typeIds);
        Assert.Contains(NetworkResourceTypeProvider.ResourceTypeId, typeIds);
        Assert.Contains(VirtualNetworkResourceTypeProvider.ResourceTypeId, typeIds);
        Assert.Contains(LocalHostNetworkResourceTypeProvider.ResourceTypeId, typeIds);
        Assert.Contains(MacOSHostNetworkResourceTypeProvider.ResourceTypeId, typeIds);
        Assert.Contains(DnsZoneResourceTypeProvider.ResourceTypeId, typeIds);
        Assert.Contains(NameMappingResourceTypeProvider.ResourceTypeId, typeIds);
        Assert.Contains(LoadBalancerResourceTypeProvider.ResourceTypeId, typeIds);
        Assert.Contains(ServiceResourceTypeProvider.ResourceTypeId, typeIds);

        var configurationStore = serviceProvider.GetRequiredService<ConfigurationStoreRuntimeOptions>();
        Assert.Equal("services/configuration-store.csproj", configurationStore.ServiceProjectPath);
        Assert.Equal("Sample:Message", Assert.Single(configurationStore.Settings).Name);

        var secretsVault = serviceProvider.GetRequiredService<SecretsVaultRuntimeOptions>();
        Assert.Equal("services/secrets-vault.csproj", secretsVault.ServiceProjectPath);
        Assert.Equal("sample-api-key", Assert.Single(secretsVault.Secrets).Name);
    }

    [Fact]
    public void AddBuiltInResourceModelProviderTypes_CanDisableHostRunApplicationResourceTypes()
    {
        var services = new ServiceCollection();
        services.AddBuiltInResourceModelProviderTypes(options =>
        {
            options.EnableHostRunApplicationResourceTypes = false;
        });
        using var serviceProvider = services.BuildServiceProvider();

        var typeIds = serviceProvider
            .GetServices<IResourceTypeProvider>()
            .Select(provider => provider.TypeId)
            .ToHashSet();

        Assert.DoesNotContain(ExecutableApplicationResourceTypeProvider.ResourceTypeId, typeIds);
        Assert.DoesNotContain(AspNetCoreProjectResourceTypeProvider.ResourceTypeId, typeIds);
        Assert.DoesNotContain(JavaScriptAppResourceTypeProvider.ResourceTypeId, typeIds);
        Assert.DoesNotContain(JavaAppResourceTypeProvider.ResourceTypeId, typeIds);
        Assert.DoesNotContain(GoAppResourceTypeProvider.ResourceTypeId, typeIds);
        Assert.DoesNotContain(PythonAppResourceTypeProvider.ResourceTypeId, typeIds);
        Assert.Contains(ContainerApplicationResourceTypeProvider.ResourceTypeId, typeIds);
        Assert.Contains(DockerHostResourceTypeProvider.ResourceTypeId, typeIds);
        Assert.Contains(SqlServerResourceTypeProvider.ResourceTypeId, typeIds);
    }

    [Fact]
    public void AddBuiltInResourceModelRuntimeAdapters_RegistersProviderOwnedResourceModelRuntimeAdapters()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph();
        services.AddResourceModelGraphServices();
        services.AddBuiltInResourceModelRuntimeAdapters();
        using var serviceProvider = services.BuildServiceProvider();

        Assert.IsType<ResourceModelGraphEndpointMappingReconciler>(
            serviceProvider.GetRequiredService<INetworkEndpointMappingReconciler>());
        Assert.IsType<ResourceModelGraphEndpointMappingReconciler>(
            serviceProvider.GetRequiredService<IVirtualNetworkEndpointMappingReconciler>());
        Assert.IsType<ResourceModelGraphEndpointMappingReconciler>(
            serviceProvider.GetRequiredService<ILocalHostNetworkEndpointMappingReconciler>());
        Assert.IsType<ResourceModelGraphEndpointMappingReconciler>(
            serviceProvider.GetRequiredService<IMacOSHostNetworkEndpointMappingReconciler>());
        Assert.IsType<ResourceModelGraphDnsZoneNameMappingReconciler>(
            serviceProvider.GetRequiredService<IDnsZoneNameMappingReconciler>());
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
            DependsOn: [ResourceReference.DependsOnResourceId(volume.EffectiveResourceId)],
            Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeValue>
            {
                [ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath] = "dotnet",
                [ExecutableApplicationResourceTypeProvider.Attributes.Command] =
                    ResourceAttributeValue.FromObject(new ExecutableApplicationConfiguration("dotnet", "run"))
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

        Assert.False(
            validation.HasErrors,
            string.Join(Environment.NewLine, validation.Diagnostics
                .Concat(validation.Resources.SelectMany(resource => resource.Diagnostics))
                .Select(diagnostic => diagnostic.Message)));

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
    public async Task AddDotnetAppResourceType_RegistersCompleteResourceTypeBoundary()
    {
        var services = new ServiceCollection();
        services.AddLocalVolumeResourceType();
        services.AddDotnetAppResourceType();
        services.AddResourceModelGraphServices();
        using var serviceProvider = services.BuildServiceProvider();
        var volume = new ResourceDefinition(
            "data",
            LocalVolumeResourceTypeProvider.ResourceTypeId);
        var definition = new ResourceDefinition(
            "api",
            AspNetCoreProjectResourceTypeProvider.ResourceTypeId,
            Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeValue>
            {
                [AspNetCoreProjectResourceTypeProvider.Attributes.ProjectPath] = "src/Api/Api.csproj",
                [AspNetCoreProjectResourceTypeProvider.Attributes.ProjectArguments] = "--urls http://localhost:5010",
                [AspNetCoreProjectResourceTypeProvider.Attributes.HotReload] = true,
                [AspNetCoreProjectResourceTypeProvider.Attributes.UseLaunchSettings] = false,
                [AspNetCoreProjectResourceTypeProvider.Attributes.EndpointRequests] =
                    ResourceAttributeValue.FromObject(new[]
                    {
                        new NetworkingEndpointRequestValue(
                            "http",
                            "http",
                            Host: "localhost",
                            Port: 5010,
                            Exposure: "Local")
                    })
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
        Assert.True(projectValidation.Resource.Operations.Has(
            AspNetCoreProjectResourceTypeProvider.Operations.Stop));
        Assert.True(projectValidation.Resource.Operations.Has(
            AspNetCoreProjectResourceTypeProvider.Operations.Restart));

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
        var endpointRequest = Assert.Single(projection.EndpointRequests);
        Assert.Equal("http", endpointRequest.Name);
        Assert.Equal(5010, endpointRequest.Port);
        Assert.True(projection.HotReload);
        Assert.False(projection.UseLaunchSettings);
        Assert.Equal(volume.EffectiveResourceId, Assert.Single(await projection.GetVolumesAsync()).Volume);

        var start = await projection.GetStartOperationAsync();
        var stop = await projection.GetStopOperationAsync();
        var restart = await projection.GetRestartOperationAsync();

        Assert.NotNull(start);
        Assert.NotNull(stop);
        Assert.NotNull(restart);
        Assert.True(await start.CanExecuteAsync());
        Assert.False(await stop.CanExecuteAsync());
        Assert.False(await restart.CanExecuteAsync());

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
    public async Task AddDotnetAppResourceType_AcceptsExecutablePathSourceMode()
    {
        var services = new ServiceCollection();
        services.AddDotnetAppResourceType();
        services.AddResourceModelGraphServices();
        using var serviceProvider = services.BuildServiceProvider();
        var definition = new ResourceDefinition(
            "api",
            AspNetCoreProjectResourceTypeProvider.ResourceTypeId,
            Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeValue>
            {
                [AspNetCoreProjectResourceTypeProvider.Attributes.ExecutablePath] = "publish/Api.dll",
                [AspNetCoreProjectResourceTypeProvider.Attributes.ProjectArguments] = "--urls http://localhost:5010"
            });

        var validation = await serviceProvider
            .GetRequiredService<ResourceDefinitionGraphValidationPipeline>()
            .ValidateAsync(
                new ResourceDefinitionGraph([definition]),
                new ResourceDefinitionValidationContext("local", "developer"));

        Assert.False(validation.HasErrors);
        var resourceValidation = Assert.Single(validation.Resources);
        Assert.Equal("publish/Api.dll", resourceValidation.Resource.Attributes.GetString(
            AspNetCoreProjectResourceTypeProvider.Attributes.ExecutablePath));

        var projectedGraph = await serviceProvider
            .GetRequiredService<ResourceDefinitionGraphProjectionResolver>()
            .ProjectAsync(
                validation,
                new ResourceProjectionContext("local", "developer"));
        var projection = projectedGraph.Find<AspNetCoreProjectResource>(
            definition.EffectiveResourceId);

        Assert.NotNull(projection);
        Assert.Equal("publish/Api.dll", projection.ExecutablePath);
        Assert.Null(projection.ProjectPath);
    }

    [Fact]
    public async Task AddDotnetAppResourceType_RejectsConflictingLocalSourceModes()
    {
        var services = new ServiceCollection();
        services.AddDotnetAppResourceType();
        services.AddResourceModelGraphServices();
        using var serviceProvider = services.BuildServiceProvider();
        var definition = new ResourceDefinition(
            "api",
            AspNetCoreProjectResourceTypeProvider.ResourceTypeId,
            Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeValue>
            {
                [AspNetCoreProjectResourceTypeProvider.Attributes.ProjectPath] = "src/Api/Api.csproj",
                [AspNetCoreProjectResourceTypeProvider.Attributes.ExecutablePath] = "publish/Api.dll"
            });

        var validation = await serviceProvider
            .GetRequiredService<ResourceDefinitionGraphValidationPipeline>()
            .ValidateAsync(
                new ResourceDefinitionGraph([definition]),
                new ResourceDefinitionValidationContext("local", "developer"));

        Assert.True(validation.HasErrors);
        Assert.Contains(validation.Resources.SelectMany(resource => resource.Diagnostics), diagnostic =>
            diagnostic.Code == "application.dotnetApp.sourceConflict");
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
        Assert.False(await provision.CanExecuteAsync());
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
            DependsOn: [ResourceReference.DependsOnResourceId(storage.EffectiveResourceId)],
            Attributes: new Dictionary<ResourceAttributeId, string>
            {
                [CloudShellVolumeResourceTypeProvider.Attributes.Provider] = "Local Storage",
                [CloudShellVolumeResourceTypeProvider.Attributes.StorageMedium] = "FileSystem",
                [CloudShellVolumeResourceTypeProvider.Attributes.SubPath] = "data",
                [CloudShellVolumeResourceTypeProvider.Attributes.AccessMode] = "ReadWriteOnce",
                [CloudShellVolumeResourceTypeProvider.Attributes.Persistent] = bool.TrueString.ToLowerInvariant(),
                [CloudShellVolumeResourceTypeProvider.Attributes.MaxSizeBytes] = "1048576",
                [CloudShellVolumeResourceTypeProvider.Attributes.MaxSizeEnforcement] = VolumeMaxSizeEnforcementModes.Advisory
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
        Assert.Equal(StorageVolumeAccessMode.ReadWriteOnce, projection.AccessMode);
        Assert.True(projection.Persistent);
        Assert.Equal(1048576, projection.MaxSizeBytes);
        Assert.Equal(VolumeMaxSizeEnforcementModes.Advisory, projection.MaxSizeEnforcement);
        Assert.Equal([storage.EffectiveResourceId], projection.References.Select(reference => reference.Value));
        var provision = await projection.GetProvisionOperationAsync();

        Assert.NotNull(provision);
        Assert.False(await provision.CanExecuteAsync());
        Assert.Equal("FileSystem", provision.PlanProvision().StorageMedium);
        Assert.Equal([storage.EffectiveResourceId], provision.PlanProvision().References.Select(reference => reference.Value));
    }

    [Fact]
    public async Task AddCloudShellVolumeResourceType_RejectsUnknownAccessMode()
    {
        var services = new ServiceCollection();
        services.AddStorageResourceType();
        services.AddCloudShellVolumeResourceType();
        services.AddResourceModelGraphServices();
        using var serviceProvider = services.BuildServiceProvider();
        var definition = new ResourceDefinition(
            "data",
            CloudShellVolumeResourceTypeProvider.ResourceTypeId,
            Attributes: new Dictionary<ResourceAttributeId, string>
            {
                [CloudShellVolumeResourceTypeProvider.Attributes.StorageMedium] = "FileSystem",
                [CloudShellVolumeResourceTypeProvider.Attributes.AccessMode] = "ReadWriteSometimes"
            });

        var validation = await serviceProvider
            .GetRequiredService<ResourceDefinitionValidationPipeline>()
            .ValidateAsync(
                definition,
                new ResourceDefinitionValidationContext("local", "developer"));

        var diagnostic = Assert.Single(validation.Diagnostics, diagnostic =>
            diagnostic.Code == "storage.volume.accessModeInvalid");
        Assert.Equal(CloudShellVolumeResourceTypeProvider.Attributes.AccessMode.ToString(), diagnostic.Target);
    }

    [Fact]
    public async Task AddCloudShellVolumeResourceType_RejectsInvalidMaxSize()
    {
        var services = new ServiceCollection();
        services.AddCloudShellVolumeResourceType();
        services.AddResourceModelGraphServices();
        using var serviceProvider = services.BuildServiceProvider();
        var definition = new ResourceDefinition(
            "data",
            CloudShellVolumeResourceTypeProvider.ResourceTypeId,
            Attributes: new Dictionary<ResourceAttributeId, string>
            {
                [CloudShellVolumeResourceTypeProvider.Attributes.StorageMedium] = "FileSystem",
                [CloudShellVolumeResourceTypeProvider.Attributes.AccessMode] = "ReadWriteOnce",
                [CloudShellVolumeResourceTypeProvider.Attributes.MaxSizeBytes] = "0"
            });

        var validation = await serviceProvider
            .GetRequiredService<ResourceDefinitionValidationPipeline>()
            .ValidateAsync(
                definition,
                new ResourceDefinitionValidationContext("local", "developer"));

        var diagnostic = Assert.Single(validation.Diagnostics, diagnostic =>
            diagnostic.Code == "storage.volume.maxSizeBytesInvalid");
        Assert.Equal(CloudShellVolumeResourceTypeProvider.Attributes.MaxSizeBytes.ToString(), diagnostic.Target);
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
                ResourceReference.DependsOnResourceId("application.container-app:api"),
                ResourceReference.DependsOnResourceId("cloudshell.network:default")
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
            ResourceCommonCapabilityIds.EndpointSource));
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
        var descriptorProvider = Assert.Single(serviceProvider
            .GetServices<CloudShell.Abstractions.ResourceManager.IResourceOrchestrationDescriptorProvider>()
            .OfType<ContainerHostOrchestrationDescriptorProvider>());
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
        Assert.NotNull(descriptorProvider);
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
            ResourceCommonCapabilityIds.Monitoring));
        Assert.False(validation.Resource.Capabilities.Has(
            ResourceLogSourceCapabilityIds.LogSources));
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
        Assert.False(projection.SupportsLogSources);
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
                [ConfigurationStoreResourceTypeProvider.Attributes.Endpoint] = "http://localhost:5138"
            });

        var validation = await serviceProvider
            .GetRequiredService<ResourceDefinitionValidationPipeline>()
            .ValidateAsync(
                definition,
                new ResourceDefinitionValidationContext("local", "developer"));

        Assert.False(validation.HasErrors);
        Assert.Equal(ConfigurationStoreResourceTypeProvider.ClassId, validation.Resource.Class.ClassId);
        Assert.Equal("store", validation.Resource.Attributes.GetString(
            ConfigurationStoreResourceTypeProvider.Attributes.Kind));
        Assert.Equal("http://localhost:5138", validation.Resource.Attributes.GetString(
            ConfigurationStoreResourceTypeProvider.Attributes.Endpoint));
        Assert.Equal("0", validation.Resource.Attributes.GetString(
            ConfigurationStoreResourceTypeProvider.Attributes.SettingCount));
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
        Assert.Equal(0, projection.SettingCount);
        var inspect = await projection.GetInspectOperationAsync();

        Assert.NotNull(inspect);
        Assert.True(await inspect.CanExecuteAsync());
        Assert.Equal(0, inspect.PlanInspection().SettingCount);
    }

    [Fact]
    public void AddConfigurationStoreResourceType_ConfiguresProviderOwnedRuntimeOptions()
    {
        var services = new ServiceCollection();
        services.AddConfigurationStoreResourceType(options =>
        {
            options.ServiceProjectPath = "services/configuration-store.csproj";
            options.ServiceWorkingDirectory = "/repo";
            options.ServiceBearerAuthority = "http://identity.local/realms/cloudshell";
            options.ServiceBearerIssuer = "http://identity.local/realms/cloudshell";
            options.ServiceBearerRequireHttpsMetadata = false;
            options.Settings.Add(new("Sample:Message", "Hello from graph"));
        });
        using var serviceProvider = services.BuildServiceProvider();

        var options = serviceProvider.GetRequiredService<ConfigurationStoreRuntimeOptions>();

        Assert.Equal("services/configuration-store.csproj", options.ServiceProjectPath);
        Assert.Equal("/repo", options.ServiceWorkingDirectory);
        Assert.Equal("http://identity.local/realms/cloudshell", options.ServiceBearerAuthority);
        Assert.Equal("http://identity.local/realms/cloudshell", options.ServiceBearerIssuer);
        Assert.False(options.ServiceBearerRequireHttpsMetadata);
        var setting = Assert.Single(options.Settings);
        Assert.Equal("Sample:Message", setting.Name);
        Assert.Equal("Hello from graph", setting.Value);
    }

    [Fact]
    public async Task ConfigurationStoreRuntimeSettingManager_UpdatesProviderOwnedRuntimeSettings()
    {
        var definitionsDirectory = Path.Combine(
            Path.GetTempPath(),
            $"cloudshell-configuration-store-test-{Guid.NewGuid():N}");
        var services = new ServiceCollection();
        services.AddConfigurationStoreResourceType(options =>
        {
            options.DefinitionsDirectory = definitionsDirectory;
            options.Settings.Add(new("Sample:Message", "Hello from graph"));
        });
        using var serviceProvider = services.BuildServiceProvider();

        var manager = serviceProvider.GetRequiredService<IConfigurationStoreRuntimeSettingManager>();
        var initial = await manager.ListSettingsAsync("configuration.store:settings");

        Assert.Equal("Sample:Message", Assert.Single(initial).Name);

        await manager.UpdateSettingsAsync(
            new ProviderRuntimeResourceContext(
                "configuration.store:settings",
                "settings",
                "Settings",
                "http://localhost:5138"),
            [new("Sample:Mode", "Development")]);

        var options = serviceProvider.GetRequiredService<ConfigurationStoreRuntimeOptions>();
        var setting = Assert.Single(options.Settings);
        Assert.Equal("Sample:Mode", setting.Name);
        Assert.Equal("Development", setting.Value);

        var definitionsPath = Path.Combine(
            definitionsDirectory,
            "configuration.store_settings",
            "configuration-stores.json");
        using var document = JsonDocument.Parse(File.ReadAllText(definitionsPath));
        var store = Assert.Single(document.RootElement.EnumerateArray());
        Assert.Equal("configuration.store:settings", store.GetProperty("id").GetString());
        var jsonEntry = Assert.Single(store.GetProperty("settings").EnumerateArray());
        Assert.Equal("Sample:Mode", jsonEntry.GetProperty("name").GetString());
        Assert.Equal("Development", jsonEntry.GetProperty("value").GetString());
    }

    [Fact]
    public async Task ConfigurationStoreRuntimeInspector_ReportsConfiguredRuntimeSettingCount()
    {
        var options = new ConfigurationStoreRuntimeOptions();
        options.Settings.Add(new("Sample:Message", "Hello from graph"));
        options.Settings.Add(new("Sample:Mode", "Graph"));
        var resource = new ResourceResolver(
            [ConfigurationStoreResourceTypeProvider.ClassDefinition],
            [new ConfigurationStoreResourceTypeProvider().TypeDefinition])
            .Resolve(new ResourceState(
                "settings",
                ConfigurationStoreResourceTypeProvider.ResourceTypeId));
        var inspector = new ConfigurationStoreRuntimeInspector(options);

        var diagnostics = await inspector.InspectAsync(resource);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(ResourceDefinitionDiagnosticSeverity.Information, diagnostic.Severity);
        Assert.Equal("configuration.store.inspect.runtimeSettings", diagnostic.Code);
        Assert.Equal(resource.EffectiveResourceId, diagnostic.Target);
        Assert.Contains("2 configured settings", diagnostic.Message, StringComparison.Ordinal);
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
            Attributes: new Dictionary<ResourceAttributeId, string>());

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
        Assert.Equal("0", validation.Resource.Attributes.GetString(
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
        Assert.Equal(0, projection.EntryCount);
        var inspect = await projection.GetInspectOperationAsync();

        Assert.NotNull(inspect);
        Assert.True(await inspect.CanExecuteAsync());
        Assert.Equal(0, inspect.PlanInspection().EntryCount);
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
                [LoadBalancerResourceTypeProvider.Attributes.HostResourceId] = "docker:engine"
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
        Assert.Equal(0, projection.EntrypointCount);
        Assert.Equal(0, projection.RouteCount);
        Assert.Equal(0, projection.HttpRouteCount);
        Assert.Equal(0, projection.TcpRouteCount);
        Assert.True(projection.SupportsLoadBalancing);
        var apply = await projection.GetApplyConfigurationOperationAsync();

        Assert.NotNull(apply);
        Assert.False(await apply.CanExecuteAsync());
        Assert.Contains(
            "No load-balancer configuration applier is registered for provider 'traefik'.",
            apply.UnavailableReason);
        Assert.Equal(0, apply.PlanApply().RouteCount);
    }

    [Fact]
    public async Task AddLoadBalancerResourceType_RejectsRoutesWithMissingEntrypoints()
    {
        var services = new ServiceCollection();
        services.AddLoadBalancerResourceType();
        services.AddResourceModelGraphServices();
        using var serviceProvider = services.BuildServiceProvider();
        var definition = new ResourceDefinition(
            "edge",
            LoadBalancerResourceTypeProvider.ResourceTypeId,
            Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeValue>
            {
                [LoadBalancerResourceTypeProvider.Attributes.Provider] = "traefik",
                [LoadBalancerResourceTypeProvider.Attributes.Entrypoints] =
                    ResourceAttributeValue.FromObject(new[]
                    {
                        new LoadBalancerEntrypointValue(
                            "web",
                            ResourceEndpointProtocol.Http.ToString(),
                            8080)
                    }),
                [LoadBalancerResourceTypeProvider.Attributes.Routes] =
                    ResourceAttributeValue.FromObject(new[]
                    {
                        new LoadBalancerRouteValue(
                            "api",
                            "API",
                            LoadBalancerRouteKind.Http.ToString(),
                            "admin",
                            new LoadBalancerRouteMatchValue("api.local", "/"),
                            new LoadBalancerRouteTargetValue(
                                ResourceReference.ReferenceResourceId("application:api"),
                                "http"))
                    })
            });

        var validation = await serviceProvider
            .GetRequiredService<ResourceDefinitionGraphValidationPipeline>()
            .ValidateAsync(
                new ResourceDefinitionGraph([definition]),
                new ResourceDefinitionValidationContext("local", "developer"));

        Assert.True(validation.HasErrors);
        Assert.Contains(validation.Diagnostics, diagnostic =>
            diagnostic.Code == ResourceDefinitionDiagnosticCodes.ResourceCapabilityReferenceInvalid &&
            diagnostic.Target == definition.EffectiveResourceId &&
            diagnostic.Message.Contains(
                "route 'api' references missing entrypoint 'admin'",
                StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AddLoadBalancerResourceType_RejectsInvalidCertificateEntrypointReferences()
    {
        var services = new ServiceCollection();
        services.AddLoadBalancerResourceType();
        services.AddSecretsVaultResourceType();
        services.AddConfigurationStoreResourceType();
        services.AddResourceModelGraphServices();
        using var serviceProvider = services.BuildServiceProvider();
        var vault = new ResourceDefinition(
            "edge-certificates",
            SecretsVaultResourceTypeProvider.ResourceTypeId);
        var wrongType = new ResourceDefinition(
            "settings",
            ConfigurationStoreResourceTypeProvider.ResourceTypeId);
        var definition = new ResourceDefinition(
            "edge",
            LoadBalancerResourceTypeProvider.ResourceTypeId,
            Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeValue>
            {
                [LoadBalancerResourceTypeProvider.Attributes.Entrypoints] =
                    ResourceAttributeValue.FromObject(new[]
                    {
                        new LoadBalancerEntrypointValue(
                            "web",
                            ResourceEndpointProtocol.Http.ToString(),
                            8080,
                            CertificateRef: new LoadBalancerCertificateReferenceValue(
                                vault.EffectiveResourceId,
                                "EdgeTls")),
                        new LoadBalancerEntrypointValue(
                            "https",
                            ResourceEndpointProtocol.Https.ToString(),
                            8443,
                            CertificateRef: new LoadBalancerCertificateReferenceValue(
                                wrongType.EffectiveResourceId,
                                "Wrong")),
                        new LoadBalancerEntrypointValue(
                            "missing",
                            ResourceEndpointProtocol.Https.ToString(),
                            9443,
                            CertificateRef: new LoadBalancerCertificateReferenceValue(
                                "secrets.vault:missing",
                                "Missing"))
                    })
            });

        var validation = await serviceProvider
            .GetRequiredService<ResourceDefinitionGraphValidationPipeline>()
            .ValidateAsync(
                new ResourceDefinitionGraph([vault, wrongType, definition]),
                new ResourceDefinitionValidationContext("local", "developer"));

        Assert.True(validation.HasErrors);
        Assert.Contains(validation.Diagnostics, diagnostic =>
            diagnostic.Code == ResourceDefinitionDiagnosticCodes.AttributeValueInvalid &&
            diagnostic.Message.Contains(
                "Certificates are only valid for HTTPS entrypoints",
                StringComparison.OrdinalIgnoreCase));
        Assert.Contains(validation.Diagnostics, diagnostic =>
            diagnostic.Code == ResourceDefinitionDiagnosticCodes.ResourceReferenceTypeMismatch &&
            diagnostic.Message.Contains(
                $"expected '{SecretsVaultResourceTypeProvider.ResourceTypeId}'",
                StringComparison.OrdinalIgnoreCase));
        Assert.Contains(validation.Diagnostics, diagnostic =>
            diagnostic.Code == ResourceDefinitionDiagnosticCodes.ResourceReferenceMissing &&
            diagnostic.Message.Contains(
                "references missing certificate vault",
                StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AddLoadBalancerResourceType_RejectsRoutesWithMissingTargetResources()
    {
        var services = new ServiceCollection();
        services.AddLoadBalancerResourceType();
        services.AddResourceModelGraphServices();
        using var serviceProvider = services.BuildServiceProvider();
        var definition = new ResourceDefinition(
            "edge",
            LoadBalancerResourceTypeProvider.ResourceTypeId,
            Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeValue>
            {
                [LoadBalancerResourceTypeProvider.Attributes.Provider] = "traefik",
                [LoadBalancerResourceTypeProvider.Attributes.Entrypoints] =
                    ResourceAttributeValue.FromObject(new[]
                    {
                        new LoadBalancerEntrypointValue(
                            "web",
                            ResourceEndpointProtocol.Http.ToString(),
                            8080)
                    }),
                [LoadBalancerResourceTypeProvider.Attributes.Routes] =
                    ResourceAttributeValue.FromObject(new[]
                    {
                        new LoadBalancerRouteValue(
                            "api",
                            "API",
                            LoadBalancerRouteKind.Http.ToString(),
                            "web",
                            new LoadBalancerRouteMatchValue("api.local", "/"),
                            new LoadBalancerRouteTargetValue(
                                ResourceReference.ReferenceResourceId("application:api"),
                                "http"))
                    })
            });

        var validation = await serviceProvider
            .GetRequiredService<ResourceDefinitionGraphValidationPipeline>()
            .ValidateAsync(
                new ResourceDefinitionGraph([definition]),
                new ResourceDefinitionValidationContext("local", "developer"));

        Assert.True(validation.HasErrors);
        Assert.Contains(validation.Diagnostics, diagnostic =>
            diagnostic.Code == ResourceDefinitionDiagnosticCodes.ResourceReferenceMissing &&
            diagnostic.Target == definition.EffectiveResourceId &&
            diagnostic.Message.Contains(
                "route 'api' references missing target resource 'application:api'",
                StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AddLoadBalancerResourceType_RejectsRoutesWithMissingTargetEndpoints()
    {
        var services = new ServiceCollection();
        services.AddLoadBalancerResourceType();
        services.AddContainerApplicationResourceType();
        services.AddResourceModelGraphServices();
        using var serviceProvider = services.BuildServiceProvider();
        var target = new ResourceDefinition(
            "api",
            ContainerApplicationResourceTypeProvider.ResourceTypeId,
            Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeValue>
            {
                [ContainerApplicationResourceTypeProvider.Attributes.ContainerImage] = "sample/api:1.0",
                [ContainerApplicationResourceTypeProvider.Attributes.EndpointRequests] =
                    ResourceAttributeValue.FromObject(new[]
                    {
                        new NetworkingEndpointRequestValue(
                            "http",
                            ResourceEndpointProtocol.Http.ToString(),
                            TargetPort: 8080)
                    })
            });
        var definition = new ResourceDefinition(
            "edge",
            LoadBalancerResourceTypeProvider.ResourceTypeId,
            Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeValue>
            {
                [LoadBalancerResourceTypeProvider.Attributes.Provider] = "traefik",
                [LoadBalancerResourceTypeProvider.Attributes.Entrypoints] =
                    ResourceAttributeValue.FromObject(new[]
                    {
                        new LoadBalancerEntrypointValue(
                            "web",
                            ResourceEndpointProtocol.Http.ToString(),
                            8080)
                    }),
                [LoadBalancerResourceTypeProvider.Attributes.Routes] =
                    ResourceAttributeValue.FromObject(new[]
                    {
                        new LoadBalancerRouteValue(
                            "api",
                            "API",
                            LoadBalancerRouteKind.Http.ToString(),
                            "web",
                            new LoadBalancerRouteMatchValue("api.local", "/"),
                            new LoadBalancerRouteTargetValue(
                                ResourceReference.ReferenceResourceId(
                                    target.EffectiveResourceId,
                                    ContainerApplicationResourceTypeProvider.ResourceTypeId),
                                "admin"))
                    })
            });

        var validation = await serviceProvider
            .GetRequiredService<ResourceDefinitionGraphValidationPipeline>()
            .ValidateAsync(
                new ResourceDefinitionGraph([target, definition]),
                new ResourceDefinitionValidationContext("local", "developer"));

        Assert.True(validation.HasErrors);
        Assert.Contains(validation.Diagnostics, diagnostic =>
            diagnostic.Code == ResourceDefinitionDiagnosticCodes.ResourceCapabilityReferenceInvalid &&
            diagnostic.Target == definition.EffectiveResourceId &&
            diagnostic.Message.Contains(
                "route 'api' target endpoint 'admin' could not be found on resource 'application.container-app:api'",
                StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AddLoadBalancerResourceType_RejectsInvalidRouteTargetReferences()
    {
        var services = new ServiceCollection();
        services.AddLoadBalancerResourceType();
        services.AddNetworkResourceType();
        services.AddResourceModelGraphServices();
        using var serviceProvider = services.BuildServiceProvider();
        var network = new ResourceDefinition(
            "edge-network",
            NetworkResourceTypeProvider.ResourceTypeId);
        var definition = new ResourceDefinition(
            "edge",
            LoadBalancerResourceTypeProvider.ResourceTypeId,
            Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeValue>
            {
                [LoadBalancerResourceTypeProvider.Attributes.Provider] = "traefik",
                [LoadBalancerResourceTypeProvider.Attributes.Entrypoints] =
                    ResourceAttributeValue.FromObject(new[]
                    {
                        new LoadBalancerEntrypointValue(
                            "web",
                            ResourceEndpointProtocol.Http.ToString(),
                            8080)
                    }),
                [LoadBalancerResourceTypeProvider.Attributes.Routes] =
                    ResourceAttributeValue.FromObject(new[]
                    {
                        new LoadBalancerRouteValue(
                            "wrong-relationship",
                            "Wrong relationship",
                            LoadBalancerRouteKind.Http.ToString(),
                            "web",
                            new LoadBalancerRouteMatchValue("relationship.local", "/"),
                            new LoadBalancerRouteTargetValue(
                                ResourceReference.DependsOnResourceId(network.EffectiveResourceId),
                                "http")),
                        new LoadBalancerRouteValue(
                            "wrong-addressing",
                            "Wrong addressing",
                            LoadBalancerRouteKind.Http.ToString(),
                            "web",
                            new LoadBalancerRouteMatchValue("addressing.local", "/"),
                            new LoadBalancerRouteTargetValue(
                                new ResourceReference(
                                    "native-api",
                                    ResourceReferenceRelationships.Reference,
                                    ResourceReferenceAddressingModes.ProviderNative),
                                "http")),
                        new LoadBalancerRouteValue(
                            "wrong-type",
                            "Wrong type",
                            LoadBalancerRouteKind.Http.ToString(),
                            "web",
                            new LoadBalancerRouteMatchValue("network.local", "/"),
                            new LoadBalancerRouteTargetValue(
                                ResourceReference.ReferenceResourceId(
                                    network.EffectiveResourceId,
                                    ContainerApplicationResourceTypeProvider.ResourceTypeId),
                                "http"))
                    })
            });

        var validation = await serviceProvider
            .GetRequiredService<ResourceDefinitionGraphValidationPipeline>()
            .ValidateAsync(
                new ResourceDefinitionGraph([network, definition]),
                new ResourceDefinitionValidationContext("local", "developer"));

        Assert.True(validation.HasErrors);
        Assert.Contains(validation.Diagnostics, diagnostic =>
            diagnostic.Code == ResourceDefinitionDiagnosticCodes.ResourceCapabilityReferenceInvalid &&
            diagnostic.Target == definition.EffectiveResourceId &&
            diagnostic.Message.Contains(
                "route 'wrong-relationship' target",
                StringComparison.OrdinalIgnoreCase) &&
            diagnostic.Message.Contains(
                "uses relationship 'dependsOn'",
                StringComparison.OrdinalIgnoreCase));
        Assert.Contains(validation.Diagnostics, diagnostic =>
            diagnostic.Code == ResourceDefinitionDiagnosticCodes.ResourceCapabilityReferenceInvalid &&
            diagnostic.Target == definition.EffectiveResourceId &&
            diagnostic.Message.Contains(
                "route 'wrong-addressing' target",
                StringComparison.OrdinalIgnoreCase) &&
            diagnostic.Message.Contains(
                "uses addressing mode 'providerNative'",
                StringComparison.OrdinalIgnoreCase));
        Assert.Contains(validation.Diagnostics, diagnostic =>
            diagnostic.Code == ResourceDefinitionDiagnosticCodes.ResourceReferenceTypeMismatch &&
            diagnostic.Target == definition.EffectiveResourceId &&
            diagnostic.Message.Contains(
                "route 'wrong-type' references resource 'cloudshell.network:edge-network' with type 'cloudshell.network'",
                StringComparison.OrdinalIgnoreCase));
        Assert.Contains(validation.Diagnostics, diagnostic =>
            diagnostic.Code == ResourceDefinitionDiagnosticCodes.ResourceCapabilityReferenceInvalid &&
            diagnostic.Target == definition.EffectiveResourceId &&
            diagnostic.Message.Contains(
                "cannot use resource type 'cloudshell.network' as a backend target",
                StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AddLoadBalancerResourceType_RejectsDuplicateEntrypointNamesAndRouteIds()
    {
        var services = new ServiceCollection();
        services.AddLoadBalancerResourceType();
        services.AddResourceModelGraphServices();
        using var serviceProvider = services.BuildServiceProvider();
        var definition = new ResourceDefinition(
            "edge",
            LoadBalancerResourceTypeProvider.ResourceTypeId,
            Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeValue>
            {
                [LoadBalancerResourceTypeProvider.Attributes.Provider] = "traefik",
                [LoadBalancerResourceTypeProvider.Attributes.Entrypoints] =
                    ResourceAttributeValue.FromObject(new[]
                    {
                        new LoadBalancerEntrypointValue("web", ResourceEndpointProtocol.Http.ToString(), 8080),
                        new LoadBalancerEntrypointValue(" WEB ", ResourceEndpointProtocol.Https.ToString(), 8443)
                    }),
                [LoadBalancerResourceTypeProvider.Attributes.Routes] =
                    ResourceAttributeValue.FromObject(new[]
                    {
                        new LoadBalancerRouteValue(
                            "api",
                            "API",
                            LoadBalancerRouteKind.Http.ToString(),
                            "web",
                            new LoadBalancerRouteMatchValue("api.local", "/"),
                            new LoadBalancerRouteTargetValue(
                                ResourceReference.ReferenceResourceId("application:api"),
                                "http")),
                        new LoadBalancerRouteValue(
                            " API ",
                            "API copy",
                            LoadBalancerRouteKind.Http.ToString(),
                            "web",
                            new LoadBalancerRouteMatchValue("copy.local", "/"),
                            new LoadBalancerRouteTargetValue(
                                ResourceReference.ReferenceResourceId("application:api-copy"),
                                "http"))
                    })
            });

        var validation = await serviceProvider
            .GetRequiredService<ResourceDefinitionGraphValidationPipeline>()
            .ValidateAsync(
                new ResourceDefinitionGraph([definition]),
                new ResourceDefinitionValidationContext("local", "developer"));

        Assert.True(validation.HasErrors);
        Assert.Contains(validation.Diagnostics, diagnostic =>
            diagnostic.Code == ResourceDefinitionDiagnosticCodes.AttributeValueInvalid &&
            diagnostic.Target == definition.EffectiveResourceId &&
            diagnostic.Message.Contains(
                "multiple entrypoints named 'web'",
                StringComparison.OrdinalIgnoreCase));
        Assert.Contains(validation.Diagnostics, diagnostic =>
            diagnostic.Code == ResourceDefinitionDiagnosticCodes.AttributeValueInvalid &&
            diagnostic.Target == definition.EffectiveResourceId &&
            diagnostic.Message.Contains(
                "multiple routes with id 'api'",
                StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AddLoadBalancerResourceType_RejectsIncompatibleAndConflictingRoutes()
    {
        var services = new ServiceCollection();
        services.AddLoadBalancerResourceType();
        services.AddResourceModelGraphServices();
        using var serviceProvider = services.BuildServiceProvider();
        var definition = new ResourceDefinition(
            "edge",
            LoadBalancerResourceTypeProvider.ResourceTypeId,
            Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeValue>
            {
                [LoadBalancerResourceTypeProvider.Attributes.Provider] = "traefik",
                [LoadBalancerResourceTypeProvider.Attributes.Entrypoints] =
                    ResourceAttributeValue.FromObject(new[]
                    {
                        new LoadBalancerEntrypointValue(
                            "tcp",
                            ResourceEndpointProtocol.Tcp.ToString(),
                            15432)
                    }),
                [LoadBalancerResourceTypeProvider.Attributes.Routes] =
                    ResourceAttributeValue.FromObject(new[]
                    {
                        new LoadBalancerRouteValue(
                            "api",
                            "API",
                            LoadBalancerRouteKind.Http.ToString(),
                            "tcp",
                            new LoadBalancerRouteMatchValue("api.local", "/"),
                            new LoadBalancerRouteTargetValue(
                                ResourceReference.ReferenceResourceId("application:api"),
                                "http")),
                        new LoadBalancerRouteValue(
                            "api-copy",
                            "API copy",
                            LoadBalancerRouteKind.Http.ToString(),
                            "tcp",
                            new LoadBalancerRouteMatchValue("api.local", "/"),
                            new LoadBalancerRouteTargetValue(
                                ResourceReference.ReferenceResourceId("application:api-copy"),
                                "http"))
                    })
            });

        var validation = await serviceProvider
            .GetRequiredService<ResourceDefinitionGraphValidationPipeline>()
            .ValidateAsync(
                new ResourceDefinitionGraph([definition]),
                new ResourceDefinitionValidationContext("local", "developer"));

        Assert.True(validation.HasErrors);
        Assert.Contains(validation.Diagnostics, diagnostic =>
            diagnostic.Code == ResourceDefinitionDiagnosticCodes.ResourceCapabilityReferenceInvalid &&
            diagnostic.Target == definition.EffectiveResourceId &&
            diagnostic.Message.Contains(
                "route 'api' is a http route but entrypoint 'tcp' uses protocol 'Tcp'",
                StringComparison.OrdinalIgnoreCase));
        Assert.Contains(validation.Diagnostics, diagnostic =>
            diagnostic.Code == ResourceDefinitionDiagnosticCodes.AttributeValueInvalid &&
            diagnostic.Target == definition.EffectiveResourceId &&
            diagnostic.Message.Contains("conflicting route match", StringComparison.OrdinalIgnoreCase) &&
            diagnostic.Message.Contains("api", StringComparison.OrdinalIgnoreCase) &&
            diagnostic.Message.Contains("api-copy", StringComparison.OrdinalIgnoreCase));
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
        Assert.False(await reconcile.CanExecuteAsync());
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
        Assert.False(await reconcile.CanExecuteAsync());
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
        Assert.False(await reconcile.CanExecuteAsync());
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
        Assert.False(await reconcile.CanExecuteAsync());
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
        Assert.False(await reconcile.CanExecuteAsync());
        Assert.Contains(
            "No DNS name-mapping reconciler is registered for provider 'hosts-file'.",
            reconcile.UnavailableReason);
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
                ResourceReference.BelongsToResourceId(
                    "cloudshell.dnsZone:local",
                    DnsZoneResourceTypeProvider.ResourceTypeId),
                ResourceReference.ReferenceResourceId("application.executable:api")
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
        Assert.Empty(projection.Resource.State.StartupDependencyIds);
        Assert.Contains(projection.References, reference =>
            reference.Relationship == ResourceReferenceRelationships.BelongsTo &&
            reference.Value == "cloudshell.dnsZone:local");
        Assert.Contains(projection.References, reference =>
            reference.Relationship == ResourceReferenceRelationships.Reference &&
            reference.Value == "application.executable:api");
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
                [SecretsVaultResourceTypeProvider.Attributes.Endpoint] = "http://localhost:6138"
            });

        var validation = await serviceProvider
            .GetRequiredService<ResourceDefinitionValidationPipeline>()
            .ValidateAsync(
                definition,
                new ResourceDefinitionValidationContext("local", "developer"));

        Assert.False(validation.HasErrors);
        Assert.Equal(SecretsVaultResourceTypeProvider.ClassId, validation.Resource.Class.ClassId);
        Assert.Equal("vault", validation.Resource.Attributes.GetString(
            SecretsVaultResourceTypeProvider.Attributes.Kind));
        Assert.Equal("http://localhost:6138", validation.Resource.Attributes.GetString(
            SecretsVaultResourceTypeProvider.Attributes.Endpoint));
        Assert.Equal("0", validation.Resource.Attributes.GetString(
            SecretsVaultResourceTypeProvider.Attributes.SecretCount));
        Assert.Equal("0", validation.Resource.Attributes.GetString(
            SecretsVaultResourceTypeProvider.Attributes.CertificateCount));
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
        Assert.Equal(0, projection.SecretCount);
        Assert.Equal(0, projection.CertificateCount);
        var inspect = await projection.GetInspectOperationAsync();

        Assert.NotNull(inspect);
        Assert.True(await inspect.CanExecuteAsync());
        Assert.Equal(0, inspect.PlanInspection().SecretCount);
    }

    [Fact]
    public void AddSecretsVaultResourceType_ConfiguresProviderOwnedRuntimeOptions()
    {
        var services = new ServiceCollection();
        services.AddSecretsVaultResourceType(options =>
        {
            options.ServiceProjectPath = "services/secrets-vault.csproj";
            options.ServiceWorkingDirectory = "/repo";
            options.Secrets.Add(new("sample-api-key", "secret-value", "v1"));
            options.Certificates.Add(new(
                "api-tls",
                "certificate-value",
                "v1",
                "application/x-pem-file",
                "ABC123",
                "CN=api.local",
                HasPrivateKey: true));
        });
        using var serviceProvider = services.BuildServiceProvider();

        var options = serviceProvider.GetRequiredService<SecretsVaultRuntimeOptions>();

        Assert.Equal("services/secrets-vault.csproj", options.ServiceProjectPath);
        Assert.Equal("/repo", options.ServiceWorkingDirectory);
        var secret = Assert.Single(options.Secrets);
        Assert.Equal("sample-api-key", secret.Name);
        Assert.Equal("secret-value", secret.Value);
        Assert.Equal("v1", secret.Version);
        var certificate = Assert.Single(options.Certificates);
        Assert.Equal("api-tls", certificate.Name);
        Assert.Equal("certificate-value", certificate.Value);
        Assert.Equal("application/x-pem-file", certificate.ContentType);
        Assert.True(certificate.HasPrivateKey);
    }

    [Fact]
    public async Task SecretsVaultRuntimeSecretManager_UpdatesProviderOwnedRuntimeSecrets()
    {
        var definitionsDirectory = Path.Combine(
            Path.GetTempPath(),
            $"cloudshell-secrets-vault-test-{Guid.NewGuid():N}");
        var services = new ServiceCollection();
        services.AddSecretsVaultResourceType(options =>
        {
            options.DefinitionsDirectory = definitionsDirectory;
            options.Secrets.Add(new("sample-api-key", "secret-value", "v1"));
        });
        using var serviceProvider = services.BuildServiceProvider();

        var manager = serviceProvider.GetRequiredService<ISecretsVaultRuntimeSecretManager>();
        var initial = await manager.ListSecretsAsync("secrets.vault:vault");

        Assert.Equal("sample-api-key", Assert.Single(initial).Name);

        await manager.UpdateSecretsAsync(
            new ProviderRuntimeResourceContext(
                "secrets.vault:vault",
                "vault",
                "Vault",
                "http://localhost:6138"),
            [new("new-api-key", "new-secret", "v2")]);

        var options = serviceProvider.GetRequiredService<SecretsVaultRuntimeOptions>();
        var secret = Assert.Single(options.Secrets);
        Assert.Equal("new-api-key", secret.Name);
        Assert.Equal("new-secret", secret.Value);
        Assert.Equal("v2", secret.Version);

        var definitionsPath = Path.Combine(
            definitionsDirectory,
            "secrets.vault_vault",
            "secrets-vaults.json");
        using var document = JsonDocument.Parse(File.ReadAllText(definitionsPath));
        var vault = Assert.Single(document.RootElement.EnumerateArray());
        Assert.Equal("secrets.vault:vault", vault.GetProperty("id").GetString());
        var jsonSecret = Assert.Single(vault.GetProperty("secrets").EnumerateArray());
        Assert.Equal("new-api-key", jsonSecret.GetProperty("name").GetString());
        Assert.Equal("new-secret", jsonSecret.GetProperty("value").GetString());
        Assert.Equal("v2", jsonSecret.GetProperty("version").GetString());
    }

    [Fact]
    public async Task SecretsVaultRuntimeSecretManager_AssignsSecretVersionsAndKeepsChangedPayloadHistory()
    {
        var definitionsDirectory = Path.Combine(
            Path.GetTempPath(),
            $"cloudshell-secrets-vault-test-{Guid.NewGuid():N}");
        var services = new ServiceCollection();
        services.AddSecretsVaultResourceType(options =>
        {
            options.DefinitionsDirectory = definitionsDirectory;
        });
        using var serviceProvider = services.BuildServiceProvider();
        var context = new ProviderRuntimeResourceContext(
            "secrets.vault:vault",
            "vault",
            "Vault",
            "http://localhost:6138");
        var manager = serviceProvider.GetRequiredService<ISecretsVaultRuntimeSecretManager>();

        await manager.UpdateSecretsAsync(
            context,
            [new("api-key", "secret-v1")]);
        var firstVersion = Assert.Single(await manager.ListSecretsAsync("secrets.vault:vault")).Version;
        await manager.UpdateSecretsAsync(
            context,
            [new("api-key", "secret-v2", firstVersion)]);

        Assert.False(string.IsNullOrWhiteSpace(firstVersion));
        var versions = (await manager.ListSecretsAsync("secrets.vault:vault"))
            .OrderBy(secret => secret.Value, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(2, versions.Length);
        Assert.Equal("secret-v1", versions[0].Value);
        Assert.Equal(firstVersion, versions[0].Version);
        Assert.Equal("secret-v2", versions[1].Value);
        Assert.NotEqual(firstVersion, versions[1].Version);

        var definitionsPath = Path.Combine(
            definitionsDirectory,
            "secrets.vault_vault",
            "secrets-vaults.json");
        using var document = JsonDocument.Parse(File.ReadAllText(definitionsPath));
        var vault = Assert.Single(document.RootElement.EnumerateArray());
        Assert.Equal(2, vault.GetProperty("secrets").EnumerateArray().Count());
    }

    [Fact]
    public async Task SecretsVaultRuntimeSecretManager_UpdatesProviderOwnedRuntimeCertificates()
    {
        var definitionsDirectory = Path.Combine(
            Path.GetTempPath(),
            $"cloudshell-secrets-vault-test-{Guid.NewGuid():N}");
        var services = new ServiceCollection();
        services.AddSecretsVaultResourceType(options =>
        {
            options.DefinitionsDirectory = definitionsDirectory;
            options.Secrets.Add(new("sample-api-key", "secret-value", "v1"));
        });
        using var serviceProvider = services.BuildServiceProvider();

        var manager = serviceProvider.GetRequiredService<ISecretsVaultRuntimeSecretManager>();
        await manager.UpdateCertificatesAsync(
            new ProviderRuntimeResourceContext(
                "secrets.vault:vault",
                "vault",
                "Vault",
                "http://localhost:6138"),
            [new(
                "api-tls",
                "certificate-value",
                "v1",
                "application/x-pem-file",
                "ABC123",
                "CN=api.local",
                HasPrivateKey: true)]);

        var options = serviceProvider.GetRequiredService<SecretsVaultRuntimeOptions>();
        Assert.Equal("sample-api-key", Assert.Single(options.Secrets).Name);
        var certificate = Assert.Single(options.Certificates);
        Assert.Equal("api-tls", certificate.Name);
        Assert.Equal("certificate-value", certificate.Value);
        Assert.Equal("application/x-pem-file", certificate.ContentType);
        Assert.True(certificate.HasPrivateKey);

        var definitionsPath = Path.Combine(
            definitionsDirectory,
            "secrets.vault_vault",
            "secrets-vaults.json");
        using var document = JsonDocument.Parse(File.ReadAllText(definitionsPath));
        var vault = Assert.Single(document.RootElement.EnumerateArray());
        var jsonSecret = Assert.Single(vault.GetProperty("secrets").EnumerateArray());
        Assert.Equal("sample-api-key", jsonSecret.GetProperty("name").GetString());
        var jsonCertificate = Assert.Single(vault.GetProperty("certificates").EnumerateArray());
        Assert.Equal("api-tls", jsonCertificate.GetProperty("name").GetString());
        Assert.Equal("certificate-value", jsonCertificate.GetProperty("value").GetString());
        Assert.Equal("v1", jsonCertificate.GetProperty("version").GetString());
        Assert.Equal("application/x-pem-file", jsonCertificate.GetProperty("contentType").GetString());
        Assert.Equal("ABC123", jsonCertificate.GetProperty("thumbprint").GetString());
        Assert.True(jsonCertificate.GetProperty("hasPrivateKey").GetBoolean());
    }

    [Fact]
    public async Task SecretsVaultRuntimeSecretManager_AssignsCertificateVersionsAndKeepsChangedPayloadHistory()
    {
        var definitionsDirectory = Path.Combine(
            Path.GetTempPath(),
            $"cloudshell-secrets-vault-test-{Guid.NewGuid():N}");
        var services = new ServiceCollection();
        services.AddSecretsVaultResourceType(options =>
        {
            options.DefinitionsDirectory = definitionsDirectory;
        });
        using var serviceProvider = services.BuildServiceProvider();
        var context = new ProviderRuntimeResourceContext(
            "secrets.vault:vault",
            "vault",
            "Vault",
            "http://localhost:6138");
        var manager = serviceProvider.GetRequiredService<ISecretsVaultRuntimeSecretManager>();

        await manager.UpdateCertificatesAsync(
            context,
            [new(
                "api-tls",
                "certificate-v1",
                ContentType: "application/x-pem-file",
                Thumbprint: "V1")]);
        var firstVersion = Assert.Single(
            await manager.ListCertificatesAsync("secrets.vault:vault")).Version;
        await manager.UpdateCertificatesAsync(
            context,
            [new(
                "api-tls",
                "certificate-v2",
                firstVersion,
                "application/x-pem-file",
                "V2")]);

        Assert.False(string.IsNullOrWhiteSpace(firstVersion));
        var versions = (await manager.ListCertificatesAsync("secrets.vault:vault"))
            .OrderBy(certificate => certificate.Value, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(2, versions.Length);
        Assert.Equal("certificate-v1", versions[0].Value);
        Assert.Equal(firstVersion, versions[0].Version);
        Assert.Equal("certificate-v2", versions[1].Value);
        Assert.Equal("V2", versions[1].Thumbprint);
        Assert.NotEqual(firstVersion, versions[1].Version);

        var definitionsPath = Path.Combine(
            definitionsDirectory,
            "secrets.vault_vault",
            "secrets-vaults.json");
        using var document = JsonDocument.Parse(File.ReadAllText(definitionsPath));
        var vault = Assert.Single(document.RootElement.EnumerateArray());
        Assert.Equal(2, vault.GetProperty("certificates").EnumerateArray().Count());
    }

    [Fact]
    public async Task SecretsVaultRuntimeSecretReferenceResolver_ResolvesCertificates()
    {
        var options = new SecretsVaultRuntimeOptions();
        options.Certificates.Add(new(
            "api-tls",
            "certificate-value",
            ContentType: "application/x-pem-file",
            Thumbprint: "ABC123",
            Subject: "CN=api.local",
            HasPrivateKey: true));
        var resolver = new SecretsVaultRuntimeSecretReferenceResolver(options);

        var result = await resolver.ResolveCertificateAsync(
            new CertificateReference("secrets.vault:vault", "api-tls"),
            new ResourceSettingResolutionContext("application:api"));

        Assert.True(result.IsResolved);
        Assert.Equal("certificate-value", result.Value);
        Assert.Equal("application/x-pem-file", result.ContentType);
        Assert.Equal("ABC123", result.Thumbprint);
        Assert.Equal("CN=api.local", result.Subject);
    }

    [Fact]
    public async Task SecretsVaultRuntimeSecretReferenceResolver_ResolvesVersionedSecrets()
    {
        var options = new SecretsVaultRuntimeOptions();
        options.Secrets.Add(new("api-key", "secret-v1", "v1"));
        options.Secrets.Add(new("api-key", "secret-v2", "v2"));
        var resolver = new SecretsVaultRuntimeSecretReferenceResolver(options);

        var latest = await resolver.ResolveSecretAsync(
            new SecretReference("secrets.vault:vault", "api-key"),
            new ResourceSettingResolutionContext("application:api"));
        var versioned = await resolver.ResolveSecretAsync(
            new SecretReference("secrets.vault:vault", "api-key", "v1"),
            new ResourceSettingResolutionContext("application:api"));

        Assert.True(latest.IsResolved);
        Assert.Equal("secret-v2", latest.Value);
        Assert.True(versioned.IsResolved);
        Assert.Equal("secret-v1", versioned.Value);
    }

    [Fact]
    public async Task SecretsVaultRuntimeSecretReferenceResolver_ResolvesVersionedCertificates()
    {
        var options = new SecretsVaultRuntimeOptions();
        options.Certificates.Add(new(
            "api-tls",
            "certificate-v1",
            "v1",
            ContentType: "application/x-pem-file",
            Thumbprint: "V1"));
        options.Certificates.Add(new(
            "api-tls",
            "certificate-v2",
            "v2",
            ContentType: "application/x-pem-file",
            Thumbprint: "V2"));
        var resolver = new SecretsVaultRuntimeSecretReferenceResolver(options);

        var latest = await resolver.ResolveCertificateAsync(
            new CertificateReference("secrets.vault:vault", "api-tls"),
            new ResourceSettingResolutionContext("application:api"));
        var versioned = await resolver.ResolveCertificateAsync(
            new CertificateReference("secrets.vault:vault", "api-tls", "v1"),
            new ResourceSettingResolutionContext("application:api"));

        Assert.True(latest.IsResolved);
        Assert.Equal("certificate-v2", latest.Value);
        Assert.Equal("V2", latest.Thumbprint);
        Assert.True(versioned.IsResolved);
        Assert.Equal("certificate-v1", versioned.Value);
        Assert.Equal("V1", versioned.Thumbprint);
    }

    [Fact]
    public async Task SecretsVaultRuntimeInspector_ReportsConfiguredRuntimeSecretCount()
    {
        var options = new SecretsVaultRuntimeOptions();
        options.Secrets.Add(new("sample-api-key", "secret-value"));
        options.Certificates.Add(new("api-tls", "certificate-value"));
        var resource = new ResourceResolver(
            [SecretsVaultResourceTypeProvider.ClassDefinition],
            [new SecretsVaultResourceTypeProvider().TypeDefinition])
            .Resolve(new ResourceState(
                "vault",
                SecretsVaultResourceTypeProvider.ResourceTypeId));
        var inspector = new SecretsVaultRuntimeInspector(options);

        var diagnostics = await inspector.InspectAsync(resource);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(ResourceDefinitionDiagnosticSeverity.Information, diagnostic.Severity);
        Assert.Equal("secrets.vault.inspect.runtimeSecrets", diagnostic.Code);
        Assert.Equal(resource.EffectiveResourceId, diagnostic.Target);
        Assert.Contains("1 configured secret", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("1 configured certificate", diagnostic.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-value", diagnostic.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("certificate-value", diagnostic.Message, StringComparison.Ordinal);
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
        Assert.False(await setup.CanExecuteAsync());
        Assert.Contains("no identity provisioning setup handler", setup.UnavailableReason, StringComparison.Ordinal);
        Assert.Equal("Built-in Identity", setup.PlanSetup().IdentityProvider);
    }

    [Fact]
    public async Task AddContainerApplicationResourceType_RegistersCompleteResourceTypeBoundary()
    {
        var services = new ServiceCollection();
        services.AddLocalVolumeResourceType();
        services.AddContainerApplicationResourceType();
        services.AddResourceModelGraphServices();
        using var serviceProvider = services.BuildServiceProvider();
        var typeIds = serviceProvider
            .GetServices<IResourceTypeProvider>()
            .Select(provider => provider.TypeId)
            .ToHashSet();
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
            Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeValue>
            {
                [ContainerApplicationResourceTypeProvider.Attributes.ContainerImage] = "ghcr.io/example/api:latest",
                [ContainerApplicationResourceTypeProvider.Attributes.ContainerRegistry] = "registry.local",
                [ContainerApplicationResourceTypeProvider.Attributes.ContainerReplicas] = "2",
                [ContainerApplicationResourceTypeProvider.Attributes.EndpointRequests] =
                    ResourceAttributeValue.FromObject(new[]
                    {
                        new NetworkingEndpointRequestValue(
                            "http",
                            "http",
                            TargetPort: 8080,
                            Host: "localhost",
                            Port: 5092,
                            Exposure: "Local")
                    })
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
        Assert.Contains(ContainerHostResourceTypeProvider.ResourceTypeId, typeIds);
        var containerValidation = validation.Resources.Single(resource =>
            resource.Resource.EffectiveResourceId == definition.EffectiveResourceId);
        Assert.Equal(ContainerApplicationResourceTypeProvider.ClassId, containerValidation.Resource.Class.ClassId);
        Assert.Equal("ghcr.io/example/api:latest", containerValidation.Resource.Attributes.GetString(
            ContainerApplicationResourceTypeProvider.Attributes.ContainerImage));
        Assert.Equal("registry.local", containerValidation.Resource.Attributes.GetString(
            ContainerApplicationResourceTypeProvider.Attributes.ContainerRegistry));
        Assert.Equal("2", containerValidation.Resource.Attributes.GetString(
            ContainerApplicationResourceTypeProvider.Attributes.ContainerReplicas));
        var endpointRequest = Assert.Single(containerValidation.Resource.Attributes
            .GetObject<NetworkingEndpointRequestValue[]>(
                ContainerApplicationResourceTypeProvider.Attributes.EndpointRequests) ?? []);
        Assert.Equal("http", endpointRequest.Name);
        Assert.Equal(8080, endpointRequest.TargetPort);
        Assert.True(containerValidation.Resource.Capabilities.Has(
            VolumeConsumerCapabilityProvider.CapabilityIdValue));
        Assert.True(containerValidation.Resource.Operations.Has(
            ContainerApplicationResourceTypeProvider.Operations.Start));
        Assert.True(containerValidation.Resource.Operations.Has(
            ContainerApplicationResourceTypeProvider.Operations.Stop));
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
        Assert.Equal("registry.local", projection.Registry);
        Assert.Equal(2, projection.Replicas);
        Assert.Equal(5092, Assert.Single(projection.EndpointRequests).Port);
        Assert.Equal(host.EffectiveResourceId, projection.ContainerHostResourceId);
        Assert.Equal(volume.EffectiveResourceId, Assert.Single(await projection.GetVolumesAsync()).Volume);
        Assert.True(await (await projection.GetStartOperationAsync())!.CanExecuteAsync());
        Assert.True(await (await projection.GetStopOperationAsync())!.CanExecuteAsync());
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
    public void AddLocalContainerApplicationResourceTypes_RegistersDockerHostAndContainerAppTypes()
    {
        var services = new ServiceCollection();

        services.AddLocalContainerApplicationResourceTypes();
        using var serviceProvider = services.BuildServiceProvider();
        var typeIds = serviceProvider
            .GetServices<IResourceTypeProvider>()
            .Select(provider => provider.TypeDefinition.TypeId)
            .ToArray();
        var changeApplyTypeIds = serviceProvider
            .GetServices<IResourceChangeApplyProvider>()
            .Select(provider => provider.TypeId)
            .ToArray();
        var definitionApplyTypeIds = serviceProvider
            .GetServices<IResourceDefinitionApplyProvider>()
            .Select(provider => provider.TypeId)
            .ToArray();

        Assert.Contains(DockerHostResourceTypeProvider.ResourceTypeId, typeIds);
        Assert.Contains(ContainerApplicationResourceTypeProvider.ResourceTypeId, typeIds);
        Assert.Contains(DockerHostResourceTypeProvider.ResourceTypeId, changeApplyTypeIds);
        Assert.Contains(ContainerApplicationResourceTypeProvider.ResourceTypeId, changeApplyTypeIds);
        Assert.Contains(DockerHostResourceTypeProvider.ResourceTypeId, definitionApplyTypeIds);
        Assert.Contains(ContainerApplicationResourceTypeProvider.ResourceTypeId, definitionApplyTypeIds);
    }

    [Fact]
    public async Task AddLocalContainerApplicationProcessRuntime_ReplacesNoopContainerAppRuntimeHandler()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IHostEnvironment>(
            new TestHostEnvironment(Directory.GetCurrentDirectory()));
        services.AddSingleton<ILogger<LocalContainerApplicationProcessRuntimeBridge>>(
            NullLogger<LocalContainerApplicationProcessRuntimeBridge>.Instance);

        services
            .AddLocalContainerApplicationResourceTypes()
            .AddLocalContainerApplicationProcessRuntime(options =>
                options.AddProject(
                    "application.container-app:api",
                    "/tmp/api.csproj",
                    runtime => runtime.ReplicaPortStart = 6100));

        await using var serviceProvider = services.BuildServiceProvider();
        var handler = serviceProvider.GetRequiredService<IContainerApplicationRuntimeHandler>();
        var options = serviceProvider
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<
                LocalContainerApplicationProcessRuntimeOptions>>()
            .Value;

        Assert.IsType<LocalContainerApplicationProcessRuntimeHandler>(handler);
        var application = Assert.Single(options.Applications);
        Assert.Equal("application.container-app:api", application.Key);
        Assert.Equal(6100, application.Value.ReplicaPortStart);
    }

    [Fact]
    public void AddStorageBackedSqlServerResourceTypes_RegistersStorageVolumeAndSqlServerTypes()
    {
        var services = new ServiceCollection();

        services.AddStorageBackedSqlServerResourceTypes();
        using var serviceProvider = services.BuildServiceProvider();
        var typeIds = serviceProvider
            .GetServices<IResourceTypeProvider>()
            .Select(provider => provider.TypeDefinition.TypeId)
            .ToArray();
        var changeApplyTypeIds = serviceProvider
            .GetServices<IResourceChangeApplyProvider>()
            .Select(provider => provider.TypeId)
            .ToArray();
        var definitionApplyTypeIds = serviceProvider
            .GetServices<IResourceDefinitionApplyProvider>()
            .Select(provider => provider.TypeId)
            .ToArray();

        Assert.Contains(StorageResourceTypeProvider.ResourceTypeId, typeIds);
        Assert.Contains(CloudShellVolumeResourceTypeProvider.ResourceTypeId, typeIds);
        Assert.Contains(SqlServerResourceTypeProvider.ResourceTypeId, typeIds);
        Assert.DoesNotContain(SqlDatabaseResourceTypeProvider.ResourceTypeId, typeIds);
        Assert.Contains(StorageResourceTypeProvider.ResourceTypeId, changeApplyTypeIds);
        Assert.Contains(CloudShellVolumeResourceTypeProvider.ResourceTypeId, changeApplyTypeIds);
        Assert.Contains(SqlServerResourceTypeProvider.ResourceTypeId, changeApplyTypeIds);
        Assert.DoesNotContain(SqlDatabaseResourceTypeProvider.ResourceTypeId, changeApplyTypeIds);
        Assert.Contains(StorageResourceTypeProvider.ResourceTypeId, definitionApplyTypeIds);
        Assert.Contains(CloudShellVolumeResourceTypeProvider.ResourceTypeId, definitionApplyTypeIds);
        Assert.Contains(SqlServerResourceTypeProvider.ResourceTypeId, definitionApplyTypeIds);
        Assert.DoesNotContain(SqlDatabaseResourceTypeProvider.ResourceTypeId, definitionApplyTypeIds);
    }

    [Fact]
    public async Task AddSqlServerResourceType_RegistersCompleteResourceTypeBoundary()
    {
        var services = new ServiceCollection();
        services.AddLocalVolumeResourceType();
        services.AddSqlServerResourceType();
        services.AddResourceModelGraphServices();
        using var serviceProvider = services.BuildServiceProvider();
        var typeIds = serviceProvider
            .GetServices<IResourceTypeProvider>()
            .Select(provider => provider.TypeId)
            .ToHashSet();
        var volume = new ResourceDefinition(
            "sql-data",
            LocalVolumeResourceTypeProvider.ResourceTypeId);
        var host = new ResourceDefinition(
            "docker",
            ContainerHostResourceTypeProvider.ResourceTypeId);
        var definition = new ResourceDefinition(
            "sql",
            SqlServerResourceTypeProvider.ResourceTypeId,
            DependsOn:
            [
                ResourceReference.DependsOnResourceId(
                    host.EffectiveResourceId,
                    typeId: ContainerHostResourceTypeProvider.ResourceTypeId)
            ],
            Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeValue>
            {
                [SqlServerResourceTypeProvider.Attributes.Version] = "2022",
                [SqlServerResourceTypeProvider.Attributes.Edition] = "Developer",
                [SqlServerResourceTypeProvider.Attributes.EndpointRequests] =
                    ResourceAttributeValue.FromObject(new[]
                    {
                        new NetworkingEndpointRequestValue(
                            "tds",
                            "tcp",
                            TargetPort: 1433,
                            Host: "localhost",
                            Port: 14334,
                            Exposure: "Local")
                    }),
                [SqlServerResourceTypeProvider.Attributes.Databases] =
                    ResourceAttributeValue.FromObject(new SqlServerDatabaseDefinition[]
                    {
                        new("appdb", "Application DB", EnsureCreated: true)
                    })
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
                new ResourceDefinitionGraph([host, volume, definition]),
                new ResourceDefinitionValidationContext("local", "developer"));

        Assert.False(validation.HasErrors);
        Assert.Contains(ContainerHostResourceTypeProvider.ResourceTypeId, typeIds);
        var sqlValidation = validation.Resources.Single(resource =>
            resource.Resource.EffectiveResourceId == definition.EffectiveResourceId);
        Assert.Equal(SqlServerResourceTypeProvider.ClassId, sqlValidation.Resource.Class.ClassId);
        Assert.Equal("2022", sqlValidation.Resource.Attributes.GetString(
            SqlServerResourceTypeProvider.Attributes.Version));
        Assert.True(sqlValidation.Resource.Capabilities.Has(
            VolumeConsumerCapabilityProvider.CapabilityIdValue));
        Assert.True(sqlValidation.Resource.Operations.Has(
            SqlServerResourceTypeProvider.Operations.Start));
        Assert.True(sqlValidation.Resource.Operations.Has(
            SqlServerResourceTypeProvider.Operations.Stop));
        Assert.True(sqlValidation.Resource.Operations.Has(
            SqlServerResourceTypeProvider.Operations.Restart));
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
        var endpointRequest = Assert.Single(projection.EndpointRequests);
        Assert.Equal("tds", endpointRequest.Name);
        Assert.Equal("tcp", endpointRequest.Protocol);
        Assert.Equal(14334, endpointRequest.Port);
        Assert.Equal("appdb", Assert.Single(projection.Databases).Name);

        var start = await projection.GetStartOperationAsync();
        var stop = await projection.GetStopOperationAsync();
        var restart = await projection.GetRestartOperationAsync();
        var reconcile = await projection.GetReconcileAccessOperationAsync();

        Assert.NotNull(start);
        Assert.True(await start.CanExecuteAsync());
        Assert.NotNull(stop);
        Assert.True(await stop.CanExecuteAsync());
        Assert.NotNull(restart);
        Assert.True(await restart.CanExecuteAsync());
        Assert.NotNull(reconcile);
        Assert.False(await reconcile.CanExecuteAsync());
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
            DependsOn:
            [
                ResourceReference.DependsOnResourceId(
                    server.EffectiveResourceId,
                    typeId: SqlServerResourceTypeProvider.ResourceTypeId)
            ],
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
        var databaseAttributeDefinitions = databaseValidation.Resource.Type.Definition.Attributes;
        Assert.NotNull(databaseAttributeDefinitions);
        var serverAttribute = databaseAttributeDefinitions[SqlDatabaseResourceTypeProvider.Attributes.Server];
        Assert.Equal(ResourceAttributeValueType.ResourceReference, serverAttribute.ValueType);
        Assert.True(serverAttribute.ReadOnly);
        Assert.Equal(ResourceAttributeMutability.ProviderManaged, serverAttribute.Mutability);
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
        Assert.Equal(server.EffectiveResourceId, projection.OwningServerResourceId);
        Assert.Equal(server.EffectiveResourceId, projection.ServerResourceId);
        Assert.NotNull(projection.OwningServerReference);
        Assert.Equal(
            ResourceReferenceRelationships.BelongsTo,
            projection.OwningServerReference.Relationship);
        Assert.Equal(
            SqlServerResourceTypeProvider.ResourceTypeId,
            projection.OwningServerReference.TypeId);
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
    public async Task AddSqlDatabaseResourceType_RejectsCallerAuthoredServerAttribute()
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
            DependsOn:
            [
                ResourceReference.DependsOnResourceId(
                    server.EffectiveResourceId,
                    typeId: SqlServerResourceTypeProvider.ResourceTypeId)
            ],
            Attributes: new Dictionary<ResourceAttributeId, string>
            {
                [SqlDatabaseResourceTypeProvider.Attributes.DatabaseName] = "appdb",
                [SqlDatabaseResourceTypeProvider.Attributes.Server] = server.EffectiveResourceId
            });

        var validation = await serviceProvider
            .GetRequiredService<ResourceDefinitionGraphValidationPipeline>()
            .ValidateAsync(
                new ResourceDefinitionGraph([server, definition]),
                new ResourceDefinitionValidationContext("local", "developer"));

        var databaseValidation = validation.Resources.Single(resource =>
            resource.Resource.EffectiveResourceId == definition.EffectiveResourceId);

        Assert.True(databaseValidation.HasErrors);
        Assert.Contains(
            databaseValidation.Diagnostics,
            diagnostic =>
                diagnostic.Code == "application.sqlDatabase.serverAttributeUnsupported" &&
                diagnostic.Target == SqlDatabaseResourceTypeProvider.Attributes.Server.ToString());
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
            configuration: new ExecutableApplicationConfiguration("dotnet", "run"));

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
            configuration: new ExecutableApplicationConfiguration("", "run"));

        var result = await dispatcher.ValidateResourceTypeAsync(
            resolved,
            new ResourceProviderContext());

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("application.executable.pathRequired", diagnostic.Code);
        Assert.Equal(ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath, diagnostic.Target);
    }

    private static Resource ResolveExecutable(
        ExecutableApplicationConfiguration? configuration = null,
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

        var attributes = configuration is null
            ? null
            : new Dictionary<ResourceAttributeId, ResourceAttributeValue>
            {
                [ExecutableApplicationResourceTypeProvider.Attributes.Command] =
                    ResourceAttributeValue.FromObject(configuration)
            };

        return resolver.Resolve(new ResourceDefinition(
            "api",
            ExecutableApplicationResourceTypeProvider.ResourceTypeId,
            Attributes: attributes,
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

    private sealed class TestHostEnvironment(string contentRootPath) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;

        public string ApplicationName { get; set; } = "CloudShell.ResourceModel.Tests";

        public string ContentRootPath { get; set; } = contentRootPath;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
