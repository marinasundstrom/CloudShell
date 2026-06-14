using System.Globalization;
using System.Text.Json;
using CloudShell.Abstractions.Authorization;
using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.ControlPlane.ResourceManager;

public sealed class PlatformResourceProvider(
    PlatformResourceStore store,
    PlatformResourceOptions options,
    IHostLocalNetworkEnvironment? hostLocalNetworkEnvironment = null,
    IEnumerable<IResourceEndpointMappingProvisioner>? endpointMappingProvisioners = null,
    IEnumerable<ILoadBalancerProvider>? loadBalancerProviders = null) :
    IResourceProvider,
    IResourceCreationProvider,
    IResourceProcedureProvider,
    IProgrammaticResourceDeclarationProvider,
    IResourceAutoStartPolicyProvider,
    IResourceOrchestrationDescriptorProvider,
    IResourceActionAvailabilityProvider
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public const string ProviderId = "cloudshell.platform";
    public const string HostNetworkResourceId = "network:host";
    public const string NetworkResourceType = "cloudshell.network";
    public const string VirtualNetworkResourceType = "cloudshell.virtualNetwork";
    public const string ServiceResourceType = "cloudshell.service";
    public const string StorageResourceType = "cloudshell.storage";
    public const string VolumeResourceType = "cloudshell.volume";
    public const string LoadBalancerResourceType = "cloudshell.loadBalancer";
    public const string DnsZoneResourceType = "cloudshell.dnsZone";
    public const string NameMappingResourceType = "cloudshell.nameMapping";
    public const string ReconcileEndpointMappingsActionId = "reconcileEndpointMappings";
    public const string ApplyLoadBalancerConfigurationActionId = "applyLoadBalancerConfiguration";
    private readonly IHostLocalNetworkEnvironment hostLocalNetwork =
        hostLocalNetworkEnvironment ?? new HostLocalNetworkEnvironment();
    private readonly IReadOnlyList<IResourceEndpointMappingProvisioner> endpointMappingProvisioners =
        endpointMappingProvisioners?.ToArray() ?? [];
    private readonly IReadOnlyList<ILoadBalancerProvider> loadBalancerProviders =
        loadBalancerProviders?.ToArray() ?? [];

    private static readonly ResourceAction ReconcileEndpointMappingsAction = new(
        ReconcileEndpointMappingsActionId,
        "Reconcile endpoint mappings",
        Description: "Validate and apply endpoint mappings for the network resource.",
        RequiredPermission: NetworkResourceOperationPermissions.ReconcileEndpointMappings);

    private static readonly ResourceAction ApplyLoadBalancerConfigurationAction = new(
        ApplyLoadBalancerConfigurationActionId,
        "Apply load balancer configuration",
        Description: "Validate and materialize load balancer routes for the selected provider.",
        RequiredPermission: LoadBalancerResourceOperationPermissions.ApplyConfiguration);

    public string Id => ProviderId;

    public string DisplayName => "CloudShell";

    public IReadOnlyList<Resource> GetResources()
    {
        var networks = store.GetNetworks();
        if (networks.Count == 0)
        {
            networks = [CreateHostNetworkDefinition()];
        }

        return
        [
            .. networks.Select(CreateNetworkResource),
            .. store.GetServices().Select(CreateServiceResource),
            .. store.GetStorages().Select(CreateStorageResource),
            .. store.GetVolumes().Select(CreateVolumeResource),
            .. store.GetLoadBalancers().Select(CreateLoadBalancerResource),
            .. store.GetDnsZones().Select(CreateDnsZoneResource),
            .. store.GetDnsZones().SelectMany(CreateNameMappingResources)
        ];
    }

    public bool CanCreate(ResourceCreationRequest request) =>
        IsNetworkResourceType(request.ResourceType) ||
        string.Equals(request.ResourceType, LoadBalancerResourceType, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(request.ResourceType, DnsZoneResourceType, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(request.ResourceType, StorageResourceType, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(request.ResourceType, VolumeResourceType, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(request.ResourceType, ServiceResourceType, StringComparison.OrdinalIgnoreCase);

    public async Task CreateAsync(
        ResourceCreationRequest request,
        ResourceCreationContext context,
        CancellationToken cancellationToken = default)
    {
        if (IsNetworkResourceType(request.ResourceType))
        {
            var definition = request.Configuration.Deserialize<NetworkResourceDefinition>(SerializerOptions)
                ?? throw new InvalidOperationException("Network resource configuration is required.");
            await SetupNetworkAsync(
                definition with
                {
                    Id = string.IsNullOrWhiteSpace(definition.Id) ? request.ResourceId : definition.Id,
                    Name = string.IsNullOrWhiteSpace(definition.Name) ? request.Name : definition.Name,
                    Kind = string.Equals(request.ResourceType, VirtualNetworkResourceType, StringComparison.OrdinalIgnoreCase)
                        ? NetworkResourceKind.Virtual
                        : definition.Kind
                },
                request.ResourceGroupId,
                context.Registrations,
                cancellationToken);
            return;
        }

        if (string.Equals(request.ResourceType, ServiceResourceType, StringComparison.OrdinalIgnoreCase))
        {
            var definition = request.Configuration.Deserialize<ServiceResourceDefinition>(SerializerOptions)
                ?? throw new InvalidOperationException("Service resource configuration is required.");
            await SetupServiceAsync(
                definition with
                {
                    Id = string.IsNullOrWhiteSpace(definition.Id) ? request.ResourceId : definition.Id,
                    Name = string.IsNullOrWhiteSpace(definition.Name) ? request.Name : definition.Name
                },
                request.ResourceGroupId,
                context.Registrations,
                cancellationToken);
            return;
        }

        if (string.Equals(request.ResourceType, DnsZoneResourceType, StringComparison.OrdinalIgnoreCase))
        {
            var definition = request.Configuration.Deserialize<DnsZoneResourceDefinition>(SerializerOptions)
                ?? throw new InvalidOperationException("DNS zone resource configuration is required.");
            await SetupDnsZoneAsync(
                definition with
                {
                    Id = string.IsNullOrWhiteSpace(definition.Id) ? request.ResourceId : definition.Id,
                    Name = string.IsNullOrWhiteSpace(definition.Name) ? request.Name : definition.Name
                },
                request.ResourceGroupId,
                context.Registrations,
                cancellationToken);
            return;
        }

        if (string.Equals(request.ResourceType, LoadBalancerResourceType, StringComparison.OrdinalIgnoreCase))
        {
            var definition = request.Configuration.Deserialize<LoadBalancerResourceDefinition>(SerializerOptions)
                ?? throw new InvalidOperationException("Load balancer resource configuration is required.");
            await SetupLoadBalancerAsync(
                definition with
                {
                    Id = string.IsNullOrWhiteSpace(definition.Id) ? request.ResourceId : definition.Id,
                    Name = string.IsNullOrWhiteSpace(definition.Name) ? request.Name : definition.Name
                },
                request.ResourceGroupId,
                context.Registrations,
                cancellationToken);
            return;
        }

        if (string.Equals(request.ResourceType, VolumeResourceType, StringComparison.OrdinalIgnoreCase))
        {
            var definition = request.Configuration.Deserialize<VolumeResourceDefinition>(SerializerOptions)
                ?? throw new InvalidOperationException("Volume resource configuration is required.");
            await SetupVolumeAsync(
                definition with
                {
                    Id = string.IsNullOrWhiteSpace(definition.Id) ? request.ResourceId : definition.Id,
                    Name = string.IsNullOrWhiteSpace(definition.Name) ? request.Name : definition.Name
                },
                request.ResourceGroupId,
                context.Registrations,
                cancellationToken);
            return;
        }

        if (string.Equals(request.ResourceType, StorageResourceType, StringComparison.OrdinalIgnoreCase))
        {
            var definition = request.Configuration.Deserialize<StorageResourceDefinition>(SerializerOptions)
                ?? throw new InvalidOperationException("Storage resource configuration is required.");
            await SetupStorageAsync(
                definition with
                {
                    Id = string.IsNullOrWhiteSpace(definition.Id) ? request.ResourceId : definition.Id,
                    Name = string.IsNullOrWhiteSpace(definition.Name) ? request.Name : definition.Name
                },
                request.ResourceGroupId,
                context.Registrations,
                cancellationToken);
            return;
        }

        throw new InvalidOperationException(
            $"Platform resource type '{request.ResourceType}' is not supported.");
    }

    public async Task SetupNetworkAsync(
        NetworkResourceDefinition definition,
        string? resourceGroupId,
        IResourceRegistrationStore registrations,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeNetwork(definition);
        ValidatePlatformEndpointAssignments(
            normalized.Id,
            CreateNetworkEndpoints(normalized));
        store.SaveNetwork(normalized);
        await registrations.RegisterAsync(
            Id,
            normalized.Id,
            NormalizeGroupId(resourceGroupId),
            cancellationToken: cancellationToken);
    }

    public async Task SetupServiceAsync(
        ServiceResourceDefinition definition,
        string? resourceGroupId,
        IResourceRegistrationStore registrations,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeService(definition);
        ValidatePlatformEndpointAssignments(
            normalized.Id,
            CreateEndpoints(normalized));
        store.SaveService(normalized);
        await registrations.RegisterAsync(
            Id,
            normalized.Id,
            NormalizeGroupId(resourceGroupId),
            CreateServiceDependencies(normalized),
            cancellationToken);
    }

    public async Task SetupLoadBalancerAsync(
        LoadBalancerResourceDefinition definition,
        string? resourceGroupId,
        IResourceRegistrationStore registrations,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeLoadBalancer(definition);
        ValidateLoadBalancerRoutes(normalized);
        ValidatePlatformEndpointAssignments(
            normalized.Id,
            CreateLoadBalancerEndpoints(normalized));
        store.SaveLoadBalancer(normalized);
        await registrations.RegisterAsync(
            Id,
            normalized.Id,
            NormalizeGroupId(resourceGroupId),
            CreateLoadBalancerDependencies(normalized),
            cancellationToken);
    }

    public async Task SetupDnsZoneAsync(
        DnsZoneResourceDefinition definition,
        string? resourceGroupId,
        IResourceRegistrationStore registrations,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeDnsZone(definition);
        store.SaveDnsZone(normalized);
        await registrations.RegisterAsync(
            Id,
            normalized.Id,
            NormalizeGroupId(resourceGroupId),
            CreateDnsZoneDependencies(normalized),
            cancellationToken);
    }

    public async Task SetupVolumeAsync(
        VolumeResourceDefinition definition,
        string? resourceGroupId,
        IResourceRegistrationStore registrations,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeVolume(definition);
        store.SaveVolume(normalized);
        await registrations.RegisterAsync(
            Id,
            normalized.Id,
            NormalizeGroupId(resourceGroupId),
            CreateVolumeDependencies(normalized),
            cancellationToken);
    }

    public async Task SetupStorageAsync(
        StorageResourceDefinition definition,
        string? resourceGroupId,
        IResourceRegistrationStore registrations,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeStorage(definition);
        store.SaveStorage(normalized);
        await registrations.RegisterAsync(
            Id,
            normalized.Id,
            NormalizeGroupId(resourceGroupId),
            cancellationToken: cancellationToken);
    }

    public async Task<ResourceProcedureResult> DeleteAsync(
        ResourceProcedureContext context,
        CancellationToken cancellationToken = default)
    {
        if (string.Equals(context.Resource.EffectiveTypeId, VolumeResourceType, StringComparison.OrdinalIgnoreCase))
        {
            EnsureVolumeIsNotInUse(context);
        }

        if (string.Equals(context.Resource.EffectiveTypeId, StorageResourceType, StringComparison.OrdinalIgnoreCase))
        {
            EnsureStorageIsNotInUse(context);
        }

        ResourceProcedureResult? cleanupResult = null;
        if (string.Equals(context.Resource.EffectiveTypeId, LoadBalancerResourceType, StringComparison.OrdinalIgnoreCase))
        {
            cleanupResult = await DeleteLoadBalancerRuntimeAsync(context, cancellationToken);
        }

        store.Remove(context.Resource.Id);
        await context.Registrations.RemoveAsync(context.Resource.Id, cancellationToken);
        return ResourceProcedureResult.Completed(
            cleanupResult is null
                ? "Platform resource registration removed."
                : $"{cleanupResult.Message} Platform resource registration removed.");
    }

    private static void EnsureVolumeIsNotInUse(ResourceProcedureContext context)
    {
        var resourceManager = context.ResourceManager
            ?? throw new InvalidOperationException("Resource Manager is required to delete volume resources.");
        var dependents = resourceManager
            .GetResources()
            .Where(resource => !string.Equals(resource.Id, context.Resource.Id, StringComparison.OrdinalIgnoreCase))
            .Where(resource => resource.DependsOn.Any(dependency =>
                string.Equals(dependency, context.Resource.Id, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(resource => resource.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (dependents.Length == 0)
        {
            return;
        }

        var dependentNames = string.Join(", ", dependents.Select(resource => resource.Name));
        throw new InvalidOperationException(
            $"Volume resource '{context.Resource.Id}' cannot be deleted because it is used by: {dependentNames}.");
    }

    private void EnsureStorageIsNotInUse(ResourceProcedureContext context)
    {
        var volumes = store.GetVolumes()
            .Where(volume => string.Equals(
                volume.StorageResourceId,
                context.Resource.Id,
                StringComparison.OrdinalIgnoreCase))
            .OrderBy(volume => volume.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (volumes.Length == 0)
        {
            return;
        }

        var volumeNames = string.Join(", ", volumes.Select(volume => volume.Name));
        throw new InvalidOperationException(
            $"Storage resource '{context.Resource.Id}' cannot be deleted because it owns volumes: {volumeNames}.");
    }

    public Task<ResourceProcedureResult> ExecuteActionAsync(
        ResourceProcedureContext context,
        ResourceAction action,
        CancellationToken cancellationToken = default)
    {
        if (string.Equals(action.Id, ReconcileEndpointMappingsActionId, StringComparison.OrdinalIgnoreCase))
        {
            if (!IsNetworkResourceType(context.Resource.EffectiveTypeId))
            {
                throw new InvalidOperationException(
                    $"Endpoint mappings can only be reconciled for network resources.");
            }

            return ReconcileEndpointMappingsAsync(context, cancellationToken);
        }

        if (string.Equals(action.Id, ApplyLoadBalancerConfigurationActionId, StringComparison.OrdinalIgnoreCase))
        {
            if (!string.Equals(context.Resource.EffectiveTypeId, LoadBalancerResourceType, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Load balancer configuration can only be applied for load balancer resources.");
            }

            return ApplyLoadBalancerConfigurationAsync(context, cancellationToken);
        }

        if (string.Equals(context.Resource.EffectiveTypeId, LoadBalancerResourceType, StringComparison.OrdinalIgnoreCase) &&
            action.Kind is ResourceActionKind.Start or ResourceActionKind.Stop)
        {
            return ExecuteLoadBalancerLifecycleAsync(context, action, cancellationToken);
        }

        throw new NotSupportedException(
            $"CloudShell platform resources do not support action '{action.DisplayName}'.");
    }

    public bool CanEvaluateAction(Resource resource, ResourceAction action) =>
        string.Equals(resource.EffectiveTypeId, LoadBalancerResourceType, StringComparison.OrdinalIgnoreCase) &&
        (string.Equals(action.Id, ApplyLoadBalancerConfigurationActionId, StringComparison.OrdinalIgnoreCase) ||
            action.Kind is ResourceActionKind.Start or ResourceActionKind.Stop);

    public Task<string?> GetActionUnavailableReasonAsync(
        ResourceProcedureContext context,
        ResourceAction action,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.Equals(context.Resource.EffectiveTypeId, LoadBalancerResourceType, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult<string?>(null);
        }

        var definition = store.GetLoadBalancer(context.Resource.Id);
        if (definition is null)
        {
            return Task.FromResult<string?>(
                $"Load balancer resource '{context.Resource.Id}' is not configured.");
        }

        if (!string.Equals(action.Id, ApplyLoadBalancerConfigurationActionId, StringComparison.OrdinalIgnoreCase) &&
            action.Kind is not (ResourceActionKind.Start or ResourceActionKind.Stop))
        {
            return Task.FromResult<string?>(null);
        }

        LoadBalancerProviderContext providerContext;
        try
        {
            providerContext = CreateLoadBalancerProviderContext(context);
        }
        catch (InvalidOperationException exception)
        {
            return Task.FromResult<string?>(exception.Message);
        }

        if (string.Equals(action.Id, ApplyLoadBalancerConfigurationActionId, StringComparison.OrdinalIgnoreCase))
        {
            var provider = GetApplyProvider(providerContext);
            return Task.FromResult(provider is null
                ? $"No activated load balancer provider can apply provider '{definition.Provider}' for resource '{context.Resource.Id}'."
                : null);
        }

        var runtimeProvider = GetRuntimeProvider(providerContext);
        return Task.FromResult(runtimeProvider is null
            ? $"No activated load balancer provider can manage runtime for provider '{definition.Provider}' on resource '{context.Resource.Id}'."
            : null);
    }

    public bool CanApplyDeclaration(ResourceDeclaration declaration) =>
        string.Equals(declaration.ProviderId, Id, StringComparison.OrdinalIgnoreCase);

    public bool CanEvaluateAutoStartPolicy(ResourceDeclaration declaration) =>
        CanApplyDeclaration(declaration);

    public ResourceAutoStartPolicy GetAutoStartPolicy(ResourceDeclaration declaration) =>
        new(
            StartOnControlPlaneStart: false,
            StartAsDependency: true,
            StartAfterCreate: false);

    public Task ApplyDeclarationAsync(
        ResourceDeclaration declaration,
        IResourceRegistrationStore registrations,
        CancellationToken cancellationToken = default)
    {
        var declaredNetwork = options.DeclaredNetworks.FirstOrDefault(network =>
            string.Equals(network.Definition.Id, declaration.ResourceId, StringComparison.OrdinalIgnoreCase));
        if (declaredNetwork is not null)
        {
            var normalized = NormalizeNetwork(declaredNetwork.Definition);
            ValidatePlatformEndpointAssignments(
                normalized.Id,
                CreateNetworkEndpoints(normalized));

            if (declaration.Persistence == ResourceDeclarationPersistence.Persisted)
            {
                store.SaveNetwork(
                    normalized,
                    persist: true);
            }

            return registrations.RegisterAsync(
                Id,
                normalized.Id,
                NormalizeGroupId(declaration.ResourceGroupId),
                declaration.DependsOn,
                cancellationToken);
        }

        var declaredService = options.DeclaredServices.FirstOrDefault(service =>
            string.Equals(service.Definition.Id, declaration.ResourceId, StringComparison.OrdinalIgnoreCase));

        if (declaredService is not null)
        {
            var normalized = NormalizeService(declaredService.Definition);
            ValidatePlatformEndpointAssignments(
                normalized.Id,
                CreateEndpoints(normalized));
            if (declaration.Persistence == ResourceDeclarationPersistence.Persisted)
            {
                store.SaveService(
                    normalized,
                    persist: true);
            }

            return registrations.RegisterAsync(
                Id,
                normalized.Id,
                NormalizeGroupId(declaration.ResourceGroupId),
                CreateServiceDependencies(normalized)
                    .Concat(declaration.DependsOn)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                cancellationToken);
        }

        var declaredVolume = options.DeclaredVolumes.FirstOrDefault(volume =>
            string.Equals(volume.Definition.Id, declaration.ResourceId, StringComparison.OrdinalIgnoreCase));

        if (declaredVolume is not null)
        {
            var normalized = NormalizeVolume(declaredVolume.Definition);
            if (declaration.Persistence == ResourceDeclarationPersistence.Persisted)
            {
                store.SaveVolume(
                    normalized,
                    persist: true);
            }

            return registrations.RegisterAsync(
                Id,
                normalized.Id,
                NormalizeGroupId(declaration.ResourceGroupId),
                CreateVolumeDependencies(normalized)
                    .Concat(declaration.DependsOn)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                cancellationToken);
        }

        var declaredStorage = options.DeclaredStorages.FirstOrDefault(storage =>
            string.Equals(storage.Definition.Id, declaration.ResourceId, StringComparison.OrdinalIgnoreCase));

        if (declaredStorage is not null)
        {
            var normalized = NormalizeStorage(declaredStorage.Definition);
            if (declaration.Persistence == ResourceDeclarationPersistence.Persisted)
            {
                store.SaveStorage(
                    normalized,
                    persist: true);
            }

            return registrations.RegisterAsync(
                Id,
                normalized.Id,
                NormalizeGroupId(declaration.ResourceGroupId),
                declaration.DependsOn,
                cancellationToken);
        }

        var declaredLoadBalancer = options.DeclaredLoadBalancers.FirstOrDefault(loadBalancer =>
                string.Equals(loadBalancer.Definition.Id, declaration.ResourceId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                $"Platform resource declaration '{declaration.ResourceId}' was not found.");

        var normalizedLoadBalancer = NormalizeLoadBalancer(declaredLoadBalancer.Definition);
        ValidateLoadBalancerRoutes(normalizedLoadBalancer);
        ValidatePlatformEndpointAssignments(
            normalizedLoadBalancer.Id,
            CreateLoadBalancerEndpoints(normalizedLoadBalancer));
        if (declaration.Persistence == ResourceDeclarationPersistence.Persisted)
        {
            store.SaveLoadBalancer(
                normalizedLoadBalancer,
                persist: true);
        }

        return registrations.RegisterAsync(
            Id,
            normalizedLoadBalancer.Id,
            NormalizeGroupId(declaration.ResourceGroupId),
            CreateLoadBalancerDependencies(normalizedLoadBalancer)
                .Concat(declaration.DependsOn)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            cancellationToken);
    }

    public bool CanDescribe(Resource resource) =>
        IsNetworkResourceType(resource.EffectiveTypeId) ||
        string.Equals(resource.EffectiveTypeId, LoadBalancerResourceType, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(resource.EffectiveTypeId, VolumeResourceType, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(resource.EffectiveTypeId, ServiceResourceType, StringComparison.OrdinalIgnoreCase);

    public Task<ResourceOrchestrationDescriptor> DescribeAsync(
        Resource resource,
        ResourceOrchestrationDescriptorContext context,
        CancellationToken cancellationToken = default)
    {
        if (IsNetworkResourceType(resource.EffectiveTypeId))
        {
            var network = GetNetworkDefinition(resource.Id)
                ?? throw new InvalidOperationException($"Network resource '{resource.Id}' is not configured.");
            return Task.FromResult(new ResourceOrchestrationDescriptor(
                resource.Id,
                resource.EffectiveTypeId,
                resource.DependsOn,
                [],
                resource.Endpoints,
                "1.0",
                JsonSerializer.SerializeToElement(network, SerializerOptions)));
        }

        if (string.Equals(resource.EffectiveTypeId, LoadBalancerResourceType, StringComparison.OrdinalIgnoreCase))
        {
            var loadBalancer = store.GetLoadBalancer(resource.Id)
                ?? throw new InvalidOperationException($"Load balancer resource '{resource.Id}' is not configured.");
            return Task.FromResult(new ResourceOrchestrationDescriptor(
                resource.Id,
                resource.EffectiveTypeId,
                resource.DependsOn,
                [],
                resource.Endpoints,
                "1.0",
                JsonSerializer.SerializeToElement(loadBalancer, SerializerOptions)));
        }

        if (string.Equals(resource.EffectiveTypeId, VolumeResourceType, StringComparison.OrdinalIgnoreCase))
        {
            var volume = store.GetVolume(resource.Id)
                ?? throw new InvalidOperationException($"Volume resource '{resource.Id}' is not configured.");
            return Task.FromResult(new ResourceOrchestrationDescriptor(
                resource.Id,
                resource.EffectiveTypeId,
                resource.DependsOn,
                [],
                resource.Endpoints,
                "1.0",
                JsonSerializer.SerializeToElement(volume, SerializerOptions)));
        }

        var service = store.GetService(resource.Id)
            ?? throw new InvalidOperationException($"Service resource '{resource.Id}' is not configured.");
        return Task.FromResult(new ResourceOrchestrationDescriptor(
            resource.Id,
            resource.EffectiveTypeId,
            resource.DependsOn,
            service.NetworkIds,
            resource.Endpoints,
            "1.0",
            JsonSerializer.SerializeToElement(service, SerializerOptions)));
    }

    private Resource CreateNetworkResource(NetworkResourceDefinition definition)
    {
        var endpoints = CreateNetworkEndpoints(definition);
        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [ResourceAttributeNames.NetworkKind] = GetNetworkKindAttribute(definition),
            [ResourceAttributeNames.EndpointCount] = endpoints.Count.ToString(CultureInfo.InvariantCulture)
        };
        if (definition.Kind == NetworkResourceKind.Virtual)
        {
            var mappingProviderIds = GetExternalMappingProviderIds(definition);
            if (mappingProviderIds.Count == 0)
            {
                attributes[ResourceAttributeNames.NetworkHostReadiness] = "logicalOnly";
            }
            else
            {
                attributes[ResourceAttributeNames.NetworkHostReadiness] = "providerRequired";
                attributes[ResourceAttributeNames.NetworkMappingProviders] = string.Join(",", mappingProviderIds);
            }
        }

        return new(
            definition.Id,
            definition.Name,
            "Network",
            "CloudShell",
            "logical",
            ResourceState.Running,
            endpoints,
            definition.IsDefault ? "host default" : "host local",
            DateTimeOffset.UtcNow,
            [],
            TypeId: GetNetworkResourceType(definition),
            Actions: definition.NetworkEndpointMappings.Count > 0
                ? [ReconcileEndpointMappingsAction]
                : null,
            ResourceClass: ResourceClass.Network,
            Attributes: attributes,
            Capabilities: CreateNetworkCapabilities(definition),
            EndpointMappings: definition.NetworkEndpointMappings);
    }

    private async Task<ResourceProcedureResult> ReconcileEndpointMappingsAsync(
        ResourceProcedureContext context,
        CancellationToken cancellationToken)
    {
        var resourceManager = context.ResourceManager
            ?? throw new InvalidOperationException("Resource Manager is required to reconcile endpoint mappings.");
        var network = GetNetworkDefinition(context.Resource.Id)
            ?? throw new InvalidOperationException($"Network resource '{context.Resource.Id}' is not configured.");
        if (network.NetworkEndpointMappings.Count == 0)
        {
            return ResourceProcedureResult.Completed("No endpoint mappings to reconcile.");
        }

        ValidateEndpointMappings(context.Resource.Id, network);

        var provisionedCount = 0;
        foreach (var mapping in network.NetworkEndpointMappings)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = ResolveEndpointReference(resourceManager, mapping.Id, mapping.Source, "source");
            var target = ResolveEndpointReference(resourceManager, mapping.Id, mapping.Target, "target");
            var provider = ValidateMappingProvider(resourceManager, mapping);
            if (await TryProvisionEndpointMappingAsync(
                    context.Resource,
                    network,
                    mapping,
                    source,
                    target,
                    provider,
                    resourceManager,
                    cancellationToken))
            {
                provisionedCount++;
            }
        }

        var message = provisionedCount == 0
            ? $"Reconciled {network.NetworkEndpointMappings.Count} endpoint mapping(s)."
            : $"Reconciled {network.NetworkEndpointMappings.Count} endpoint mapping(s), provisioned {provisionedCount}.";
        return ResourceProcedureResult.Completed(message);
    }

    private static void ValidateEndpointMappings(
        string networkResourceId,
        NetworkResourceDefinition network)
    {
        foreach (var mapping in network.NetworkEndpointMappings)
        {
            if (!string.Equals(mapping.Source.ResourceId, networkResourceId, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Endpoint mapping '{mapping.Id}' source endpoint '{mapping.Source.EndpointName}' must belong to network resource '{networkResourceId}'.");
            }
        }

        var duplicateSource = network.NetworkEndpointMappings
            .GroupBy(
                mapping => mapping.Source.EndpointName,
                StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateSource is not null)
        {
            var mappingIds = string.Join(", ", duplicateSource.Select(mapping => mapping.Id));
            throw new InvalidOperationException(
                $"Network resource '{networkResourceId}' source endpoint '{duplicateSource.Key}' is already used by multiple endpoint mappings: {mappingIds}.");
        }
    }

    private async Task<ResourceProcedureResult> ApplyLoadBalancerConfigurationAsync(
        ResourceProcedureContext context,
        CancellationToken cancellationToken)
    {
        var providerContext = CreateLoadBalancerProviderContext(context);
        var provider = GetApplyProvider(providerContext);
        if (provider is null)
        {
            throw new InvalidOperationException(
                $"No activated load balancer provider can apply provider '{providerContext.Definition.Provider}' for resource '{context.Resource.Id}'.");
        }

        return await provider.ApplyAsync(providerContext, cancellationToken);
    }

    private ILoadBalancerProvider? GetApplyProvider(LoadBalancerProviderContext context) =>
        loadBalancerProviders.FirstOrDefault(candidate =>
            string.Equals(candidate.ProviderName, context.Definition.Provider, StringComparison.OrdinalIgnoreCase) &&
            candidate.CanApply(context));

    private async Task<ResourceProcedureResult> ExecuteLoadBalancerLifecycleAsync(
        ResourceProcedureContext context,
        ResourceAction action,
        CancellationToken cancellationToken)
    {
        var providerContext = CreateLoadBalancerProviderContext(context);
        var provider = GetRuntimeProvider(providerContext)
            ?? throw new InvalidOperationException(
                $"No activated load balancer provider can manage runtime for provider '{providerContext.Definition.Provider}' on resource '{context.Resource.Id}'.");

        var result = action.Kind switch
        {
            ResourceActionKind.Start => await provider.StartAsync(providerContext, cancellationToken),
            ResourceActionKind.Stop => await provider.StopAsync(providerContext, cancellationToken),
            _ => throw new NotSupportedException(
                $"Load balancer lifecycle action '{action.DisplayName}' is not supported.")
        };

        store.SaveLoadBalancer(
            providerContext.Definition with
            {
                RuntimeState = action.Kind == ResourceActionKind.Start
                    ? ResourceState.Running
                    : ResourceState.Stopped
            });

        return result;
    }

    private async Task<ResourceProcedureResult?> DeleteLoadBalancerRuntimeAsync(
        ResourceProcedureContext context,
        CancellationToken cancellationToken)
    {
        var providerContext = CreateLoadBalancerProviderContext(context);
        var provider = GetRuntimeProvider(providerContext);
        if (provider is null)
        {
            return null;
        }

        return await provider.DeleteAsync(providerContext, cancellationToken);
    }

    private LoadBalancerProviderContext CreateLoadBalancerProviderContext(ResourceProcedureContext context)
    {
        var resourceManager = context.ResourceManager
            ?? throw new InvalidOperationException("Resource Manager is required to apply load balancer configuration.");
        var definition = store.GetLoadBalancer(context.Resource.Id)
            ?? throw new InvalidOperationException($"Load balancer resource '{context.Resource.Id}' is not configured.");
        var host = ResolveLoadBalancerHost(resourceManager, definition);
        return new LoadBalancerProviderContext(
            context.Resource,
            definition,
            host,
            ResolveLoadBalancerRoutes(resourceManager, definition),
            resourceManager);
    }

    private ILoadBalancerRuntimeProvider? GetRuntimeProvider(LoadBalancerProviderContext context) =>
        loadBalancerProviders
            .OfType<ILoadBalancerRuntimeProvider>()
            .FirstOrDefault(candidate =>
                string.Equals(candidate.ProviderName, context.Definition.Provider, StringComparison.OrdinalIgnoreCase) &&
                candidate.CanManageRuntime(context.Definition));

    private static Resource? ResolveLoadBalancerHost(
        IResourceManagerStore resourceManager,
        LoadBalancerResourceDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(definition.HostResourceId))
        {
            return null;
        }

        return resourceManager.GetResource(definition.HostResourceId)
            ?? throw new InvalidOperationException(
                $"Load balancer resource '{definition.Id}' host resource '{definition.HostResourceId}' could not be found.");
    }

    private IReadOnlyList<LoadBalancerRouteResolution> ResolveLoadBalancerRoutes(
        IResourceManagerStore resourceManager,
        LoadBalancerResourceDefinition definition) =>
        definition.LoadBalancerRoutes
            .Select(route => ResolveLoadBalancerRoute(resourceManager, definition.Id, route))
            .ToArray();

    private LoadBalancerRouteResolution ResolveLoadBalancerRoute(
        IResourceManagerStore resourceManager,
        string loadBalancerResourceId,
        LoadBalancerRoute route)
    {
        var targetResource = resourceManager.GetResource(route.Target.ResourceId)
            ?? throw new InvalidOperationException(
                $"Load balancer resource '{loadBalancerResourceId}' route '{route.Id}' target resource '{route.Target.ResourceId}' could not be found.");
        var targetEndpoint = ResolveLoadBalancerTargetEndpoint(loadBalancerResourceId, route, targetResource);

        if (targetEndpoint is null && route.Target.Port is null)
        {
            throw new InvalidOperationException(
                $"Load balancer resource '{loadBalancerResourceId}' route '{route.Id}' must specify a target endpoint or port.");
        }

        return new LoadBalancerRouteResolution(
            route,
            targetResource,
            targetEndpoint,
            ResolveLoadBalancerBackends(resourceManager, loadBalancerResourceId, route, targetResource, targetEndpoint));
    }

    private IReadOnlyList<LoadBalancerBackendTarget> ResolveLoadBalancerBackends(
        IResourceManagerStore resourceManager,
        string loadBalancerResourceId,
        LoadBalancerRoute route,
        Resource targetResource,
        ResourceEndpoint? targetEndpoint)
    {
        if (IsServiceResource(targetResource) &&
            store.GetService(targetResource.Id) is { } service)
        {
            return ResolveServiceLoadBalancerBackends(
                resourceManager,
                loadBalancerResourceId,
                route,
                service,
                targetEndpoint);
        }

        if (!targetResource.ResourceAttributes.ContainsKey(ResourceAttributeNames.ContainerReplicas) ||
            route.Target.Port is not { } port)
        {
            return [];
        }

        var replicas = ResolveContainerReplicaCount(targetResource);
        var protocol = string.IsNullOrWhiteSpace(targetEndpoint?.Protocol)
            ? route.Kind == LoadBalancerRouteKind.Http ? "http" : "tcp"
            : targetEndpoint.Protocol;
        var serviceName = ResourceOrchestratorServiceInstances.CreateDefaultServiceName(targetResource.Id);
        return Enumerable
            .Range(1, replicas)
            .Select(replica => new LoadBalancerBackendTarget(
                ResourceOrchestratorServiceInstances.CreateDefaultInstanceName(
                    serviceName,
                    replica,
                    replicas),
                port,
                protocol))
            .ToArray();
    }

    private static IReadOnlyList<LoadBalancerBackendTarget> ResolveServiceLoadBalancerBackends(
        IResourceManagerStore resourceManager,
        string loadBalancerResourceId,
        LoadBalancerRoute route,
        ServiceResourceDefinition service,
        ResourceEndpoint? serviceEndpoint)
    {
        if (service.Targets.Count == 0)
        {
            return [];
        }

        var servicePort = ResolveServiceRoutePort(loadBalancerResourceId, route, service, serviceEndpoint);
        return service.Targets
            .Select(target => ResolveServiceLoadBalancerBackend(
                resourceManager,
                loadBalancerResourceId,
                route,
                service,
                servicePort,
                target))
            .ToArray();
    }

    private static LoadBalancerBackendTarget ResolveServiceLoadBalancerBackend(
        IResourceManagerStore resourceManager,
        string loadBalancerResourceId,
        LoadBalancerRoute route,
        ServiceResourceDefinition service,
        ServicePort servicePort,
        ServiceTarget target)
    {
        var targetResource = resourceManager.GetResource(target.ResourceId)
            ?? throw new InvalidOperationException(
                $"Load balancer resource '{loadBalancerResourceId}' route '{route.Id}' service '{service.Id}' target resource '{target.ResourceId}' could not be found.");
        var targetEndpoint = ResolveServiceTargetEndpoint(targetResource, servicePort);
        var protocol = string.IsNullOrWhiteSpace(targetEndpoint?.Protocol)
            ? servicePort.Protocol
            : targetEndpoint.Protocol;
        var host = ResolveBackendHost(targetResource, targetEndpoint);
        var port = ResolveBackendPort(targetEndpoint, servicePort.TargetPort);
        return new LoadBalancerBackendTarget(host, port, protocol, target.Weight);
    }

    private static ServicePort ResolveServiceRoutePort(
        string loadBalancerResourceId,
        LoadBalancerRoute route,
        ServiceResourceDefinition service,
        ResourceEndpoint? serviceEndpoint)
    {
        if (!string.IsNullOrWhiteSpace(route.Target.EndpointName))
        {
            return service.Ports.FirstOrDefault(port =>
                    string.Equals(port.Name, route.Target.EndpointName, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException(
                    $"Load balancer resource '{loadBalancerResourceId}' route '{route.Id}' service '{service.Id}' port '{route.Target.EndpointName}' could not be found.");
        }

        if (route.Target.Port is { } targetPort)
        {
            var matchingPort = service.Ports.FirstOrDefault(port =>
                port.Port == targetPort ||
                port.TargetPort == targetPort);
            if (matchingPort is not null)
            {
                return matchingPort;
            }
        }

        if (serviceEndpoint is not null)
        {
            var matchingPort = service.Ports.FirstOrDefault(port =>
                string.Equals(port.Name, serviceEndpoint.Name, StringComparison.OrdinalIgnoreCase));
            if (matchingPort is not null)
            {
                return matchingPort;
            }
        }

        if (service.Ports.Count == 1)
        {
            return service.Ports[0];
        }

        throw new InvalidOperationException(
            $"Load balancer resource '{loadBalancerResourceId}' route '{route.Id}' service '{service.Id}' must target a service port by endpoint name or port.");
    }

    private static ResourceEndpoint? ResolveServiceTargetEndpoint(
        Resource targetResource,
        ServicePort servicePort) =>
        targetResource.Endpoints.FirstOrDefault(endpoint =>
            string.Equals(endpoint.Name, servicePort.Name, StringComparison.OrdinalIgnoreCase)) ??
        targetResource.Endpoints.FirstOrDefault(endpoint =>
            TryGetEndpointPort(endpoint, out var port) && port == servicePort.TargetPort);

    private static string ResolveBackendHost(
        Resource targetResource,
        ResourceEndpoint? targetEndpoint)
    {
        if (targetEndpoint is not null &&
            Uri.TryCreate(targetEndpoint.Address, UriKind.Absolute, out var uri) &&
            !string.IsNullOrWhiteSpace(uri.Host))
        {
            return NormalizeEndpointHost(uri.Host);
        }

        return CreateBackendHost(targetResource.Id);
    }

    private static int ResolveBackendPort(ResourceEndpoint? targetEndpoint, int fallbackPort)
    {
        if (targetEndpoint is not null &&
            TryGetEndpointPort(targetEndpoint, out var port))
        {
            return port;
        }

        return fallbackPort;
    }

    private static string CreateBackendHost(string resourceId)
    {
        var host = new string(resourceId
            .Trim()
            .Select(character => char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : '-')
            .ToArray())
            .Trim('-');
        return string.IsNullOrWhiteSpace(host) ? "localhost" : host;
    }

    private static bool IsServiceResource(Resource resource) =>
        resource.ResourceClass == ResourceClass.Service ||
        string.Equals(resource.EffectiveTypeId, ServiceResourceType, StringComparison.OrdinalIgnoreCase);

    private static int ResolveContainerReplicaCount(Resource resource) =>
        resource.ResourceAttributes.TryGetValue(ResourceAttributeNames.ContainerReplicas, out var replicas) &&
        int.TryParse(replicas, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? Math.Max(1, parsed)
            : 1;

    private static ResourceEndpoint? ResolveLoadBalancerTargetEndpoint(
        string loadBalancerResourceId,
        LoadBalancerRoute route,
        Resource targetResource)
    {
        if (!string.IsNullOrWhiteSpace(route.Target.EndpointName))
        {
            return targetResource.Endpoints.FirstOrDefault(endpoint =>
                    string.Equals(endpoint.Name, route.Target.EndpointName, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException(
                    $"Load balancer resource '{loadBalancerResourceId}' route '{route.Id}' target endpoint '{route.Target.EndpointName}' could not be found on resource '{targetResource.Id}'.");
        }

        if (route.Target.Port is null)
        {
            return null;
        }

        return targetResource.Endpoints.FirstOrDefault(endpoint =>
            TryGetEndpointPort(endpoint, out var port) && port == route.Target.Port);
    }

    private static void ValidateLoadBalancerRoutes(LoadBalancerResourceDefinition definition)
    {
        foreach (var duplicate in definition.LoadBalancerEntrypoints
            .GroupBy(entrypoint => entrypoint.Name, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1))
        {
            throw new InvalidOperationException(
                $"Load balancer resource '{definition.Id}' has multiple entrypoints named '{duplicate.Key}'.");
        }

        foreach (var duplicate in definition.LoadBalancerRoutes
            .GroupBy(route => route.Id, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1))
        {
            throw new InvalidOperationException(
                $"Load balancer resource '{definition.Id}' has multiple routes with id '{duplicate.Key}'.");
        }

        var entrypoints = definition.LoadBalancerEntrypoints.ToDictionary(
            entrypoint => entrypoint.Name,
            StringComparer.OrdinalIgnoreCase);

        foreach (var route in definition.LoadBalancerRoutes)
        {
            if (!entrypoints.TryGetValue(route.EntrypointName, out var entrypoint))
            {
                throw new InvalidOperationException(
                    $"Load balancer resource '{definition.Id}' route '{route.Id}' references entrypoint '{route.EntrypointName}', but no matching entrypoint is declared.");
            }

            if (!IsLoadBalancerRouteCompatibleWithEntrypoint(route, entrypoint))
            {
                throw new InvalidOperationException(
                    $"Load balancer resource '{definition.Id}' route '{route.Id}' is a {route.Kind.ToString().ToLowerInvariant()} route but entrypoint '{entrypoint.Name}' uses protocol '{entrypoint.Protocol}'.");
            }
        }

        foreach (var duplicate in definition.LoadBalancerRoutes
            .GroupBy(CreateLoadBalancerRouteConflictKey, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1))
        {
            var routeIds = string.Join(", ", duplicate.Select(route => route.Id));
            throw new InvalidOperationException(
                $"Load balancer resource '{definition.Id}' has conflicting route match '{duplicate.Key}' on routes: {routeIds}.");
        }
    }

    private static bool IsLoadBalancerRouteCompatibleWithEntrypoint(
        LoadBalancerRoute route,
        LoadBalancerEntrypoint entrypoint) =>
        route.Kind switch
        {
            LoadBalancerRouteKind.Http => entrypoint.Protocol is ResourceEndpointProtocol.Http or ResourceEndpointProtocol.Https,
            LoadBalancerRouteKind.Tcp => entrypoint.Protocol == ResourceEndpointProtocol.Tcp,
            _ => false
        };

    private static string CreateLoadBalancerRouteConflictKey(LoadBalancerRoute route)
    {
        var host = NormalizeNullable(route.Match.Host)?.ToLowerInvariant() ?? "*";
        var pathPrefix = NormalizeNullable(route.Match.PathPrefix) ?? "/";
        var port = route.Match.Port?.ToString(CultureInfo.InvariantCulture) ?? "*";
        return route.Kind == LoadBalancerRouteKind.Tcp
            ? $"{route.Kind}:{route.EntrypointName}:{port}"
            : $"{route.Kind}:{route.EntrypointName}:{host}:{pathPrefix}";
    }

    private async Task<bool> TryProvisionEndpointMappingAsync(
        Resource networkResource,
        NetworkResourceDefinition network,
        ResourceEndpointMappingDefinition mapping,
        ResolvedEndpoint source,
        ResolvedEndpoint target,
        Resource provider,
        IResourceManagerStore resourceManager,
        CancellationToken cancellationToken)
    {
        if (string.Equals(provider.Id, networkResource.Id, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (network.Kind != NetworkResourceKind.Virtual)
        {
            return false;
        }

        var provisioningContext = new ResourceEndpointMappingProvisioningContext(
            networkResource,
            network,
            mapping,
            source.Endpoint,
            target.Resource,
            target.Endpoint,
            provider,
            resourceManager);
        var provisioner = endpointMappingProvisioners.FirstOrDefault(candidate =>
            candidate.CanProvisionEndpointMapping(provisioningContext));
        if (provisioner is null)
        {
            throw new InvalidOperationException(
                $"Endpoint mapping '{mapping.Id}' requires provider resource '{provider.Id}', but no activated host networking service can materialize it.");
        }

        await provisioner.ProvisionEndpointMappingAsync(provisioningContext, cancellationToken);
        return true;
    }

    private static ResolvedEndpoint ResolveEndpointReference(
        IResourceManagerStore resourceManager,
        string mappingId,
        ResourceEndpointReference endpoint,
        string role)
    {
        var resource = resourceManager.GetResource(endpoint.ResourceId)
            ?? throw new InvalidOperationException(
                $"Endpoint mapping '{mappingId}' {role} resource '{endpoint.ResourceId}' could not be found.");
        var resolvedEndpoint = resource.Endpoints.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, endpoint.EndpointName, StringComparison.OrdinalIgnoreCase));
        if (resolvedEndpoint is null)
        {
            throw new InvalidOperationException(
                $"Endpoint mapping '{mappingId}' {role} endpoint '{endpoint.EndpointName}' could not be found on resource '{endpoint.ResourceId}'.");
        }

        return new ResolvedEndpoint(resource, resolvedEndpoint);
    }

    private static Resource ValidateMappingProvider(
        IResourceManagerStore resourceManager,
        ResourceEndpointMappingDefinition mapping)
    {
        var providerResourceId = FirstNonEmpty(
                mapping.ProviderResourceId,
                mapping.NetworkResourceId,
                mapping.Source.ResourceId)
            ?? throw new InvalidOperationException(
                $"Endpoint mapping '{mapping.Id}' does not specify a provider resource.");
        var provider = resourceManager.GetResource(providerResourceId)
            ?? throw new InvalidOperationException(
                $"Endpoint mapping '{mapping.Id}' provider resource '{providerResourceId}' could not be found.");
        if (!provider.HasCapability(ResourceCapabilityIds.NetworkingEndpointMapper))
        {
            throw new InvalidOperationException(
                $"Endpoint mapping '{mapping.Id}' provider resource '{providerResourceId}' does not advertise '{ResourceCapabilityIds.NetworkingEndpointMapper}'.");
        }

        return provider;
    }

    private Resource CreateServiceResource(ServiceResourceDefinition definition) =>
        new(
            definition.Id,
            definition.Name,
            "Service",
            "CloudShell",
            "logical",
            ResourceState.Running,
            CreateEndpoints(definition),
            definition.Ports.Count == 0 ? "host local" : $"host local {definition.Ports.Count} port(s)",
            DateTimeOffset.UtcNow,
            CreateServiceDependencies(definition),
            TypeId: ServiceResourceType,
            HealthChecks: definition.ResourceHealthChecks,
            ResourceClass: ResourceClass.Service,
            Attributes: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [ResourceAttributeNames.ServiceTargetCount] =
                    definition.Targets.Count.ToString(CultureInfo.InvariantCulture),
                [ResourceAttributeNames.ServicePortCount] =
                    definition.Ports.Count.ToString(CultureInfo.InvariantCulture),
                [ResourceAttributeNames.EndpointCount] =
                    definition.Ports.Count.ToString(CultureInfo.InvariantCulture)
            },
            Capabilities: [new(ResourceCapabilityIds.EndpointSource)]);

    private Resource CreateStorageResource(StorageResourceDefinition definition)
    {
        var volumeCount = store.GetVolumes().Count(volume =>
            string.Equals(volume.StorageResourceId, definition.Id, StringComparison.OrdinalIgnoreCase));
        return new(
            definition.Id,
            definition.Name,
            definition.Provider,
            definition.Provider,
            "local",
            ResourceState.Running,
            [],
            definition.Medium,
            DateTimeOffset.UtcNow,
            [],
            TypeId: StorageResourceType,
            ResourceClass: ResourceClass.Storage,
            Attributes: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [ResourceAttributeNames.StorageProvider] = definition.Provider,
                [ResourceAttributeNames.StorageMedium] = definition.Medium,
                [ResourceAttributeNames.StorageLocation] =
                    string.IsNullOrWhiteSpace(definition.Location) ? "provider default" : definition.Location,
                [ResourceAttributeNames.StorageVolumeCount] =
                    volumeCount.ToString(CultureInfo.InvariantCulture)
            },
            Capabilities:
            [
                new(
                    ResourceCapabilityIds.StorageProvider,
                    new Dictionary<string, string>
                    {
                        [ResourceAttributeNames.StorageMedium] = definition.Medium
                    }),
                new(
                    ResourceCapabilityIds.StorageMountProvider,
                    new Dictionary<string, string>
                    {
                        [ResourceAttributeNames.StorageMedium] = definition.Medium
                    })
            ]);
    }

    private Resource CreateVolumeResource(VolumeResourceDefinition definition)
    {
        var providerName = GetVolumeProviderName(definition);
        var storageMedium = GetVolumeStorageMedium(definition);
        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [ResourceAttributeNames.VolumeProvider] = providerName,
            [ResourceAttributeNames.VolumeAccessMode] =
                definition.AccessMode.ToString(),
            [ResourceAttributeNames.VolumePersistent] =
                definition.Persistent.ToString().ToLowerInvariant()
        };

        if (!string.IsNullOrWhiteSpace(storageMedium))
        {
            attributes[ResourceAttributeNames.VolumeStorageMedium] = storageMedium;
        }

        if (!string.IsNullOrWhiteSpace(definition.Location))
        {
            attributes[ResourceAttributeNames.VolumeLocation] = definition.Location;
        }

        if (!string.IsNullOrWhiteSpace(definition.StorageResourceId))
        {
            attributes[ResourceAttributeNames.VolumeStorageResourceId] = definition.StorageResourceId;
        }

        if (!string.IsNullOrWhiteSpace(definition.SubPath))
        {
            attributes[ResourceAttributeNames.VolumeSubPath] = definition.SubPath;
        }

        return new(
            definition.Id,
            definition.Name,
            "Volume",
            "CloudShell",
            "logical",
            ResourceState.Running,
            [],
            providerName,
            DateTimeOffset.UtcNow,
            CreateVolumeDependencies(definition),
            TypeId: VolumeResourceType,
            ResourceClass: ResourceClass.Storage,
            Attributes: attributes,
            Capabilities: [new(ResourceCapabilityIds.StorageVolume)]);
    }

    private Resource CreateDnsZoneResource(DnsZoneResourceDefinition definition)
    {
        var conflictCount = GetNameMappingConflictGroups(definition)
            .Sum(group => group.Count());
        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [ResourceAttributeNames.DnsZoneName] = definition.ZoneName,
            [ResourceAttributeNames.DnsProvider] =
                string.IsNullOrWhiteSpace(definition.Provider) ? "logical" : definition.Provider,
            [ResourceAttributeNames.DnsRecordCount] =
                definition.DnsNameMappings.Count.ToString(CultureInfo.InvariantCulture)
        };

        if (conflictCount > 0)
        {
            attributes[ResourceAttributeNames.DnsConflictCount] =
                conflictCount.ToString(CultureInfo.InvariantCulture);
        }

        return new(
            definition.Id,
            definition.Name,
            "DNS Zone",
            "CloudShell",
            "logical",
            ResourceState.Running,
            [],
            definition.ZoneName,
            DateTimeOffset.UtcNow,
            CreateDnsZoneDependencies(definition),
            TypeId: DnsZoneResourceType,
            ResourceClass: ResourceClass.Network,
            Attributes: attributes,
            Capabilities: [new(ResourceCapabilityIds.NetworkingDnsZone)]);
    }

    private IReadOnlyList<Resource> CreateNameMappingResources(DnsZoneResourceDefinition zone) =>
        CreateNameMappingResources(
            zone,
            GetConflictingNameMappingIds(zone));

    private IReadOnlyList<Resource> CreateNameMappingResources(
        DnsZoneResourceDefinition zone,
        HashSet<string> conflictingMappingIds) =>
        zone.DnsNameMappings
            .Select(mapping => CreateNameMappingResource(
                zone,
                mapping,
                conflictingMappingIds.Contains(mapping.Id)))
            .ToArray();

    private Resource CreateNameMappingResource(
        DnsZoneResourceDefinition zone,
        DnsNameMappingDefinition mapping,
        bool hasConflict)
    {
        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [ResourceAttributeNames.NameMappingHostName] = mapping.HostName,
            [ResourceAttributeNames.NameMappingTargetResourceId] = mapping.TargetResourceId,
            [ResourceAttributeNames.NameMappingExposure] = mapping.Exposure.ToString(),
            [ResourceAttributeNames.NameMappingStatus] = hasConflict ? "Conflict" : "Ready",
            [ResourceAttributeNames.NameMappingMaterializationStatus] =
                GetNameMappingMaterializationStatus(zone, mapping),
            [ResourceAttributeNames.NameMappingMaterializationStatusReason] =
                GetNameMappingMaterializationStatusReason(zone, mapping),
            [ResourceAttributeNames.DnsZoneName] = zone.ZoneName,
            [ResourceAttributeNames.DnsProvider] =
                string.IsNullOrWhiteSpace(zone.Provider) ? "logical" : zone.Provider
        };

        if (hasConflict)
        {
            attributes[ResourceAttributeNames.NameMappingStatusReason] =
                $"Host name '{mapping.HostName}' is used by multiple {mapping.Exposure} mappings in DNS zone '{zone.ZoneName}'.";
        }

        if (!string.IsNullOrWhiteSpace(mapping.TargetEndpointName))
        {
            attributes[ResourceAttributeNames.NameMappingTargetEndpointName] = mapping.TargetEndpointName;
        }

        if (!string.IsNullOrWhiteSpace(mapping.ProviderResourceId))
        {
            attributes[ResourceAttributeNames.NameMappingProviderResourceId] = mapping.ProviderResourceId;
        }

        return new(
            mapping.Id,
            mapping.Name,
            "Name Mapping",
            "CloudShell",
            "logical",
            ResourceState.Running,
            [],
            mapping.HostName,
            DateTimeOffset.UtcNow,
            CreateNameMappingDependencies(mapping),
            ParentResourceId: zone.Id,
            TypeId: NameMappingResourceType,
            ResourceClass: ResourceClass.Network,
            Attributes: attributes,
            Capabilities: [new(ResourceCapabilityIds.NetworkingNameMapping)]);
    }

    private static HashSet<string> GetConflictingNameMappingIds(DnsZoneResourceDefinition zone) =>
        GetNameMappingConflictGroups(zone)
            .SelectMany(group => group)
            .Select(mapping => mapping.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static IEnumerable<IGrouping<string, DnsNameMappingDefinition>> GetNameMappingConflictGroups(
        DnsZoneResourceDefinition zone) =>
        zone.DnsNameMappings
            .Where(mapping => !string.IsNullOrWhiteSpace(mapping.HostName))
            .GroupBy(
                mapping => CreateNameMappingConflictKey(mapping),
                StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Skip(1).Any());

    private static string CreateNameMappingConflictKey(DnsNameMappingDefinition mapping) =>
        $"{mapping.HostName.Trim().ToLowerInvariant()}|{mapping.Exposure}";

    private static string GetNameMappingMaterializationStatus(
        DnsZoneResourceDefinition zone,
        DnsNameMappingDefinition mapping) =>
        HasNameMappingProvider(zone, mapping) ? "ProviderSelected" : "LogicalOnly";

    private static string GetNameMappingMaterializationStatusReason(
        DnsZoneResourceDefinition zone,
        DnsNameMappingDefinition mapping)
    {
        if (!string.IsNullOrWhiteSpace(mapping.ProviderResourceId))
        {
            return $"Provider resource '{mapping.ProviderResourceId}' is responsible for publishing this name.";
        }

        if (!IsLogicalDnsProvider(zone.Provider))
        {
            return $"DNS provider '{zone.Provider}' is responsible for publishing this name.";
        }

        return "No DNS publishing provider is selected. CloudShell models the name mapping, but it will not publish DNS records for this host.";
    }

    private static bool HasNameMappingProvider(
        DnsZoneResourceDefinition zone,
        DnsNameMappingDefinition mapping) =>
        !string.IsNullOrWhiteSpace(mapping.ProviderResourceId) ||
        !IsLogicalDnsProvider(zone.Provider);

    private static bool IsLogicalDnsProvider(string? provider) =>
        string.IsNullOrWhiteSpace(provider) ||
        string.Equals(provider, "logical", StringComparison.OrdinalIgnoreCase);

    private Resource CreateLoadBalancerResource(LoadBalancerResourceDefinition definition)
    {
        var endpoints = CreateLoadBalancerEndpoints(definition);
        var hasRuntimeProvider = HasRuntimeProvider(definition);
        var state = hasRuntimeProvider
            ? definition.RuntimeState ?? ResourceState.Stopped
            : ResourceState.Running;
        return new(
            definition.Id,
            definition.Name,
            "Load Balancer",
            "CloudShell",
            "logical",
            state,
            endpoints,
            $"{definition.Provider} {definition.LoadBalancerRoutes.Count} route(s)",
            DateTimeOffset.UtcNow,
            CreateLoadBalancerDependencies(definition),
            TypeId: LoadBalancerResourceType,
            Actions: CreateLoadBalancerActions(definition, state, hasRuntimeProvider),
            ResourceClass: ResourceClass.Network,
            Attributes: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [ResourceAttributeNames.LoadBalancerProvider] = definition.Provider,
                [ResourceAttributeNames.LoadBalancerHostResourceId] =
                    string.IsNullOrWhiteSpace(definition.HostResourceId) ? "default" : definition.HostResourceId,
                [ResourceAttributeNames.LoadBalancerEntrypointCount] =
                    definition.LoadBalancerEntrypoints.Count.ToString(CultureInfo.InvariantCulture),
                [ResourceAttributeNames.LoadBalancerRouteCount] =
                    definition.LoadBalancerRoutes.Count.ToString(CultureInfo.InvariantCulture),
                [ResourceAttributeNames.LoadBalancerHttpRouteCount] =
                    definition.LoadBalancerRoutes.Count(route => route.Kind == LoadBalancerRouteKind.Http)
                        .ToString(CultureInfo.InvariantCulture),
                [ResourceAttributeNames.LoadBalancerTcpRouteCount] =
                    definition.LoadBalancerRoutes.Count(route => route.Kind == LoadBalancerRouteKind.Tcp)
                        .ToString(CultureInfo.InvariantCulture),
                [ResourceAttributeNames.EndpointCount] =
                    endpoints.Count.ToString(CultureInfo.InvariantCulture)
            },
            Capabilities: CreateLoadBalancerCapabilities(definition),
            LoadBalancerRoutes: definition.LoadBalancerRoutes);
    }

    private IReadOnlyList<ResourceAction> CreateLoadBalancerActions(
        LoadBalancerResourceDefinition definition,
        ResourceState state,
        bool hasRuntimeProvider)
    {
        var actions = new List<ResourceAction>();
        if (hasRuntimeProvider)
        {
            actions.Add(state is ResourceState.Running or ResourceState.Starting
                ? ResourceAction.Stop
                : ResourceAction.Start);
        }

        if (definition.LoadBalancerRoutes.Count > 0)
        {
            actions.Add(ApplyLoadBalancerConfigurationAction);
        }

        return actions;
    }

    private bool HasRuntimeProvider(LoadBalancerResourceDefinition definition) =>
        loadBalancerProviders
            .OfType<ILoadBalancerRuntimeProvider>()
            .Any(provider =>
                string.Equals(provider.ProviderName, definition.Provider, StringComparison.OrdinalIgnoreCase) &&
                provider.CanManageRuntime(definition));

    private IReadOnlyList<ResourceEndpoint> CreateNetworkEndpoints(NetworkResourceDefinition definition)
    {
        var endpoints = definition.NetworkEndpoints
            .Select(endpoint => ResolveNetworkEndpoint(definition.Id, endpoint))
            .ToArray();

        return endpoints.Length == 0
            ? [ResourceEndpoint.Logical("network", $"network://{definition.Id}", "network")]
            : endpoints;
    }

    private ResourceEndpoint ResolveNetworkEndpoint(
        string networkId,
        ResourceEndpointRequest request) =>
        hostLocalNetwork.ResolveNetworkEndpoint(
            networkId,
            request,
            options.AutoLocalPortStart,
            options.AutoLocalPortEnd);

    private IReadOnlyList<ResourceEndpoint> CreateEndpoints(ServiceResourceDefinition definition) =>
        definition.Ports
            .Select(port => hostLocalNetwork.ResolveServiceEndpoint(
                definition.Id,
                port,
                options.AutoLocalPortStart,
                options.AutoLocalPortEnd))
            .ToArray();

    private static IReadOnlyList<ResourceEndpoint> CreateLoadBalancerEndpoints(
        LoadBalancerResourceDefinition definition) =>
        definition.LoadBalancerEntrypoints
            .Select(entrypoint =>
            {
                var scheme = entrypoint.Protocol.ToString().ToLowerInvariant();
                var address = entrypoint.Protocol switch
                {
                    ResourceEndpointProtocol.Http => $"http://localhost:{entrypoint.Port}",
                    ResourceEndpointProtocol.Https => $"https://localhost:{entrypoint.Port}",
                    _ => $"{scheme}://localhost:{entrypoint.Port}"
                };
                return ResourceEndpoint.FromAddress(
                    entrypoint.Name,
                    address,
                    scheme,
                    entrypoint.Exposure);
            })
            .ToArray();

    private void ValidatePlatformEndpointAssignments(
        string ownerResourceId,
        IReadOnlyList<ResourceEndpoint> endpoints)
    {
        var occupied = GetPlatformEndpointAssignments(ownerResourceId).ToList();
        var candidates = endpoints
            .Select(endpoint => CreateEndpointAssignment(ownerResourceId, endpoint))
            .Where(assignment => assignment is not null)
            .Select(assignment => assignment!)
            .ToArray();

        foreach (var duplicate in candidates
            .GroupBy(assignment => assignment.Identity, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1))
        {
            var endpointNames = string.Join(", ", duplicate.Select(assignment => assignment.EndpointName));
            throw new InvalidOperationException(
                $"Resource '{ownerResourceId}' has conflicting endpoint assignment '{duplicate.Key}' on endpoints: {endpointNames}.");
        }

        foreach (var candidate in candidates)
        {
            var conflict = occupied.FirstOrDefault(assignment =>
                string.Equals(assignment.Identity, candidate.Identity, StringComparison.OrdinalIgnoreCase));
            if (conflict is null)
            {
                continue;
            }

            throw new InvalidOperationException(
                $"Resource '{ownerResourceId}' endpoint '{candidate.EndpointName}' uses endpoint assignment '{candidate.Identity}', which is already assigned to resource '{conflict.ResourceId}' endpoint '{conflict.EndpointName}'.");
        }
    }

    private IEnumerable<EndpointAssignment> GetPlatformEndpointAssignments(string excludeResourceId)
    {
        foreach (var network in store.GetNetworks())
        {
            if (string.Equals(network.Id, excludeResourceId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var endpoint in CreateNetworkEndpoints(network))
            {
                var assignment = CreateEndpointAssignment(network.Id, endpoint);
                if (assignment is not null)
                {
                    yield return assignment;
                }
            }
        }

        foreach (var service in store.GetServices())
        {
            if (string.Equals(service.Id, excludeResourceId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var endpoint in CreateEndpoints(service))
            {
                var assignment = CreateEndpointAssignment(service.Id, endpoint);
                if (assignment is not null)
                {
                    yield return assignment;
                }
            }
        }

        foreach (var loadBalancer in store.GetLoadBalancers())
        {
            if (string.Equals(loadBalancer.Id, excludeResourceId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var endpoint in CreateLoadBalancerEndpoints(loadBalancer))
            {
                var assignment = CreateEndpointAssignment(loadBalancer.Id, endpoint);
                if (assignment is not null)
                {
                    yield return assignment;
                }
            }
        }
    }

    private static EndpointAssignment? CreateEndpointAssignment(
        string resourceId,
        ResourceEndpoint endpoint)
    {
        if (!Uri.TryCreate(endpoint.Address, UriKind.Absolute, out var uri) ||
            string.IsNullOrWhiteSpace(uri.Host))
        {
            return null;
        }

        var port = GetEndpointPort(uri);
        if (port is null)
        {
            return null;
        }

        var protocol = string.IsNullOrWhiteSpace(endpoint.Protocol)
            ? uri.Scheme
            : endpoint.Protocol;
        var host = NormalizeEndpointHost(uri.Host);
        return new EndpointAssignment(
            resourceId,
            endpoint.Name,
            $"{protocol.ToLowerInvariant()}://{host}:{port.Value.ToString(CultureInfo.InvariantCulture)}");
    }

    private static int? GetEndpointPort(Uri uri)
    {
        if (uri.Port > 0)
        {
            return uri.Port;
        }

        if (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
        {
            return 80;
        }

        if (uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return 443;
        }

        return null;
    }

    private static string NormalizeEndpointHost(string host) =>
        host switch
        {
            "0.0.0.0" or "::" => "localhost",
            _ => host.Trim('[', ']').ToLowerInvariant()
        };

    private static IReadOnlyList<string> CreateServiceDependencies(ServiceResourceDefinition definition) =>
        definition.Targets
            .Select(target => target.ResourceId)
            .Concat(definition.NetworkIds)
            .Where(dependency => !string.IsNullOrWhiteSpace(dependency))
            .Select(dependency => dependency.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static IReadOnlyList<string> CreateLoadBalancerDependencies(LoadBalancerResourceDefinition definition) =>
        definition.LoadBalancerRoutes
            .Select(route => route.Target.ResourceId)
            .Concat([definition.HostResourceId])
            .Where(dependency => !string.IsNullOrWhiteSpace(dependency))
            .Select(dependency => dependency!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static IReadOnlyList<string> CreateDnsZoneDependencies(DnsZoneResourceDefinition definition) =>
        definition.DnsNameMappings
            .SelectMany(CreateNameMappingDependencies)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static IReadOnlyList<string> CreateNameMappingDependencies(DnsNameMappingDefinition mapping) =>
        new[] { mapping.TargetResourceId, mapping.ProviderResourceId }
            .Where(dependency => !string.IsNullOrWhiteSpace(dependency))
            .Select(dependency => dependency!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static IReadOnlyList<string> CreateVolumeDependencies(VolumeResourceDefinition definition) =>
        string.IsNullOrWhiteSpace(definition.StorageResourceId)
            ? []
            : [definition.StorageResourceId.Trim()];

    private static string GetStorageProviderName(string? provider) =>
        IsLocalStorageProvider(provider) ? StorageProviderNames.LocalStorage : provider!.Trim();

    private string GetVolumeProviderName(VolumeResourceDefinition definition)
    {
        if (!string.IsNullOrWhiteSpace(definition.StorageResourceId))
        {
            var storage = store.GetStorage(definition.StorageResourceId);
            if (storage is not null)
            {
                return storage.Provider;
            }
        }

        return GetStorageProviderName(definition.Provider);
    }

    private string? GetVolumeStorageMedium(VolumeResourceDefinition definition)
    {
        if (!string.IsNullOrWhiteSpace(definition.StorageResourceId))
        {
            return store.GetStorage(definition.StorageResourceId)?.Medium;
        }

        return IsLocalStorageProvider(definition.Provider) ? StorageMedia.FileSystem : null;
    }

    private static bool IsLocalStorageProvider(string? provider) =>
        string.IsNullOrWhiteSpace(provider) ||
        string.Equals(provider, "local", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(provider, StorageProviderNames.LocalStorage, StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<ResourceCapability> CreateLoadBalancerCapabilities(
        LoadBalancerResourceDefinition definition)
    {
        var capabilities = new List<ResourceCapability>
        {
            new(ResourceCapabilityIds.NetworkingProvider),
            new(ResourceCapabilityIds.NetworkingEndpointProvider),
            new(ResourceCapabilityIds.NetworkingEndpointMapper),
            new(ResourceCapabilityIds.NetworkingGateway),
            new(ResourceCapabilityIds.NetworkingLoadBalancer)
        };
        if (definition.LoadBalancerEntrypoints.Any(entrypoint => entrypoint.Protocol == ResourceEndpointProtocol.Https))
        {
            capabilities.Add(new ResourceCapability(ResourceCapabilityIds.NetworkingTls));
        }

        return capabilities;
    }

    private static NetworkResourceDefinition NormalizeNetwork(NetworkResourceDefinition definition) =>
        definition with
        {
            Id = NormalizeResourceId(definition.Id, "network", definition.Name),
            Name = definition.Name.Trim(),
            Endpoints = NormalizeEndpointRequests(definition.NetworkEndpoints),
            EndpointMappings = NormalizeEndpointMappings(definition.NetworkEndpointMappings)
        };

    private static bool IsNetworkResourceType(string resourceType) =>
        string.Equals(resourceType, NetworkResourceType, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(resourceType, VirtualNetworkResourceType, StringComparison.OrdinalIgnoreCase);

    private static LoadBalancerResourceDefinition NormalizeLoadBalancer(LoadBalancerResourceDefinition definition) =>
        definition with
        {
            Id = NormalizeResourceId(definition.Id, "load-balancer", definition.Name),
            Name = definition.Name.Trim(),
            Provider = string.IsNullOrWhiteSpace(definition.Provider)
                ? "traefik"
                : definition.Provider.Trim().ToLowerInvariant(),
            HostResourceId = NormalizeNullable(definition.HostResourceId),
            RuntimeState = NormalizeLoadBalancerRuntimeState(definition.RuntimeState),
            Entrypoints = definition.LoadBalancerEntrypoints
                .Where(entrypoint => !string.IsNullOrWhiteSpace(entrypoint.Name))
                .Select(entrypoint => entrypoint with
                {
                    Name = entrypoint.Name.Trim(),
                    Port = Math.Max(1, entrypoint.Port)
                })
                .ToArray(),
            Routes = definition.LoadBalancerRoutes
                .Where(route =>
                    !string.IsNullOrWhiteSpace(route.Id) &&
                    !string.IsNullOrWhiteSpace(route.EntrypointName) &&
                    !string.IsNullOrWhiteSpace(route.Target.ResourceId) &&
                    (!string.IsNullOrWhiteSpace(route.Target.EndpointName) ||
                        route.Target.Port is not null))
                .Select(route => route with
                {
                    Id = route.Id.Trim(),
                    Name = string.IsNullOrWhiteSpace(route.Name) ? route.Id.Trim() : route.Name.Trim(),
                    EntrypointName = route.EntrypointName.Trim(),
                    Match = route.Match with
                    {
                        Host = NormalizeNullable(route.Match.Host),
                        PathPrefix = NormalizeNullable(route.Match.PathPrefix),
                        Port = route.Match.Port is null ? null : Math.Max(1, route.Match.Port.Value)
                    },
                    Target = route.Target with
                    {
                        ResourceId = route.Target.ResourceId.Trim(),
                        EndpointName = NormalizeNullable(route.Target.EndpointName),
                        Port = route.Target.Port is null ? null : Math.Max(1, route.Target.Port.Value)
                    }
                })
                .ToArray()
        };

    private static VolumeResourceDefinition NormalizeVolume(VolumeResourceDefinition definition) =>
        definition with
        {
            Id = NormalizeResourceId(definition.Id, "volume", definition.Name),
            Name = definition.Name.Trim(),
            Provider = NormalizeNullable(definition.Provider),
            Location = NormalizeNullable(definition.Location),
            StorageResourceId = NormalizeNullable(definition.StorageResourceId),
            SubPath = NormalizeNullable(definition.SubPath)
        };

    private static StorageResourceDefinition NormalizeStorage(StorageResourceDefinition definition) =>
        definition with
        {
            Id = NormalizeResourceId(definition.Id, "storage", definition.Name),
            Name = definition.Name.Trim(),
            Provider = NormalizeNullable(definition.Provider) ?? StorageProviderNames.LocalStorage,
            Medium = NormalizeNullable(definition.Medium) ?? StorageMedia.FileSystem,
            Location = NormalizeNullable(definition.Location)
        };

    private static DnsZoneResourceDefinition NormalizeDnsZone(DnsZoneResourceDefinition definition) =>
        definition with
        {
            Id = NormalizeResourceId(definition.Id, "dns", definition.Name),
            Name = definition.Name.Trim(),
            ZoneName = NormalizeNullable(definition.ZoneName) ?? definition.Name.Trim().ToLowerInvariant(),
            Provider = NormalizeNullable(definition.Provider),
            Mappings = definition.DnsNameMappings
                .Where(mapping =>
                    !string.IsNullOrWhiteSpace(mapping.Id) &&
                    !string.IsNullOrWhiteSpace(mapping.HostName) &&
                    !string.IsNullOrWhiteSpace(mapping.TargetResourceId))
                .Select(mapping => mapping with
                {
                    Id = mapping.Id.Trim(),
                    Name = string.IsNullOrWhiteSpace(mapping.Name) ? mapping.Id.Trim() : mapping.Name.Trim(),
                    HostName = mapping.HostName.Trim().ToLowerInvariant(),
                    TargetResourceId = mapping.TargetResourceId.Trim(),
                    TargetEndpointName = NormalizeNullable(mapping.TargetEndpointName),
                    ProviderResourceId = NormalizeNullable(mapping.ProviderResourceId)
                })
                .DistinctBy(mapping => mapping.Id, StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };

    private static ResourceState? NormalizeLoadBalancerRuntimeState(ResourceState? state) =>
        state is ResourceState.Running or ResourceState.Starting
            ? ResourceState.Running
            : state is ResourceState.Stopped
                ? ResourceState.Stopped
                : state;

    private static string GetNetworkResourceType(NetworkResourceDefinition definition) =>
        definition.Kind == NetworkResourceKind.Virtual
            ? VirtualNetworkResourceType
            : NetworkResourceType;

    private static string GetNetworkKindAttribute(NetworkResourceDefinition definition) =>
        definition.Kind switch
        {
            NetworkResourceKind.Host => "Host",
            NetworkResourceKind.Virtual when definition.IsDefault => "Default virtual",
            NetworkResourceKind.Virtual => "Virtual",
            _ when definition.IsDefault => "Default",
            _ => "Local"
        };

    private static IReadOnlyList<ResourceCapability> CreateNetworkCapabilities(NetworkResourceDefinition definition)
    {
        var capabilities = new List<ResourceCapability>
        {
            new(ResourceCapabilityIds.NetworkingProvider),
            new(ResourceCapabilityIds.NetworkingEndpointProvider),
            new(ResourceCapabilityIds.NetworkingEndpointMapper)
        };

        if (definition.Kind == NetworkResourceKind.Host)
        {
            capabilities.Add(new(ResourceCapabilityIds.NetworkingHostNetwork));
        }

        if (definition.Kind == NetworkResourceKind.Virtual)
        {
            capabilities.Add(new(ResourceCapabilityIds.NetworkingVirtualNetwork));
            capabilities.Add(new(ResourceCapabilityIds.NetworkingIngress));
        }

        return capabilities;
    }

    private static IReadOnlyList<string> GetExternalMappingProviderIds(NetworkResourceDefinition definition) =>
        definition.NetworkEndpointMappings
            .Select(mapping => FirstNonEmpty(
                mapping.ProviderResourceId,
                mapping.NetworkResourceId,
                mapping.Source.ResourceId))
            .Where(providerId =>
                !string.IsNullOrWhiteSpace(providerId) &&
                !string.Equals(providerId, definition.Id, StringComparison.OrdinalIgnoreCase))
            .Select(providerId => providerId!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static NetworkResourceDefinition CreateHostNetworkDefinition() =>
        new(
            HostNetworkResourceId,
            "Host Network",
            IsDefault: true,
            Kind: NetworkResourceKind.Host);

    private NetworkResourceDefinition? GetNetworkDefinition(string resourceId) =>
        store.GetNetwork(resourceId) ??
        (string.Equals(resourceId, HostNetworkResourceId, StringComparison.OrdinalIgnoreCase)
            ? CreateHostNetworkDefinition()
            : null);

    private static ServiceResourceDefinition NormalizeService(ServiceResourceDefinition definition) =>
        definition with
        {
            Id = NormalizeResourceId(definition.Id, "service", definition.Name),
            Name = definition.Name.Trim(),
            Targets = definition.Targets
                .Where(target => !string.IsNullOrWhiteSpace(target.ResourceId))
                .Select(target => target with
                {
                    ResourceId = target.ResourceId.Trim(),
                    Weight = Math.Max(0, target.Weight)
                })
                .DistinctBy(target => target.ResourceId, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            Ports = definition.Ports
                .Where(port => !string.IsNullOrWhiteSpace(port.Name))
                .Select(port => port with
                {
                    Name = port.Name.Trim(),
                    Protocol = string.IsNullOrWhiteSpace(port.Protocol) ? "tcp" : port.Protocol.Trim().ToLowerInvariant(),
                    TargetPort = Math.Max(1, port.TargetPort),
                    Port = port.Port is null ? null : Math.Max(1, port.Port.Value)
                })
                .DistinctBy(port => port.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            NetworkIds = definition.NetworkIds
                .Where(networkId => !string.IsNullOrWhiteSpace(networkId))
                .Select(networkId => networkId.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            HealthChecks = NormalizeHealthChecks(definition.ResourceHealthChecks)
        };

    private static IReadOnlyList<ResourceEndpointRequest> NormalizeEndpointRequests(
        IReadOnlyList<ResourceEndpointRequest> endpoints) =>
        endpoints
            .Where(endpoint => !string.IsNullOrWhiteSpace(endpoint.Name))
            .Select(endpoint => endpoint with
            {
                Name = endpoint.Name.Trim(),
                Host = NormalizeNullable(endpoint.Host),
                IPAddress = NormalizeNullable(endpoint.IPAddress),
                Port = endpoint.Port is null ? null : Math.Max(1, endpoint.Port.Value),
                TargetPort = endpoint.TargetPort is null ? null : Math.Max(1, endpoint.TargetPort.Value),
                NetworkResourceId = NormalizeNullable(endpoint.NetworkResourceId),
                ProviderEndpointId = NormalizeNullable(endpoint.ProviderEndpointId)
            })
            .DistinctBy(endpoint => endpoint.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static IReadOnlyList<ResourceEndpointMappingDefinition> NormalizeEndpointMappings(
        IReadOnlyList<ResourceEndpointMappingDefinition> mappings) =>
        mappings
            .Where(mapping =>
                !string.IsNullOrWhiteSpace(mapping.Id) &&
                !string.IsNullOrWhiteSpace(mapping.Source.ResourceId) &&
                !string.IsNullOrWhiteSpace(mapping.Source.EndpointName) &&
                !string.IsNullOrWhiteSpace(mapping.Target.ResourceId) &&
                !string.IsNullOrWhiteSpace(mapping.Target.EndpointName))
            .Select(mapping => mapping with
            {
                Id = mapping.Id.Trim(),
                Name = string.IsNullOrWhiteSpace(mapping.Name) ? mapping.Id.Trim() : mapping.Name.Trim(),
                Source = new ResourceEndpointReference(
                    mapping.Source.ResourceId.Trim(),
                    mapping.Source.EndpointName.Trim()),
                Target = new ResourceEndpointReference(
                    mapping.Target.ResourceId.Trim(),
                    mapping.Target.EndpointName.Trim()),
                NetworkResourceId = NormalizeNullable(mapping.NetworkResourceId),
                ProviderResourceId = NormalizeNullable(mapping.ProviderResourceId)
            })
            .DistinctBy(mapping => mapping.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static bool TryGetEndpointPort(ResourceEndpoint endpoint, out int port)
    {
        if (Uri.TryCreate(endpoint.Address, UriKind.Absolute, out var uri) && !uri.IsDefaultPort)
        {
            port = uri.Port;
            return true;
        }

        var separatorIndex = endpoint.Address.LastIndexOf(':');
        if (separatorIndex >= 0 &&
            int.TryParse(
                endpoint.Address.AsSpan(separatorIndex + 1),
                CultureInfo.InvariantCulture,
                out port))
        {
            return true;
        }

        port = 0;
        return false;
    }

    private static string? NormalizeNullable(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private static string NormalizeResourceId(
        string resourceId,
        string prefix,
        string name)
    {
        if (!string.IsNullOrWhiteSpace(resourceId))
        {
            return resourceId.Trim();
        }

        var slug = string.Join(
                "-",
                name.Trim().ToLowerInvariant().Split(
                    [' ', '.', '_', ':', '/', '\\'],
                    StringSplitOptions.RemoveEmptyEntries))
            .Trim('-');

        return string.IsNullOrWhiteSpace(slug)
            ? $"{prefix}:{Guid.NewGuid():N}"
            : $"{prefix}:{slug}";
    }

    private static string? NormalizeGroupId(string? resourceGroupId) =>
        string.IsNullOrWhiteSpace(resourceGroupId) ? null : resourceGroupId.Trim();

    private static IReadOnlyList<ResourceHealthCheck> NormalizeHealthChecks(
        IReadOnlyList<ResourceHealthCheck> healthChecks) =>
        healthChecks
            .Where(check => !string.IsNullOrWhiteSpace(check.Path))
            .Select(check => check with
            {
                Path = check.Path.Trim(),
                EndpointName = string.IsNullOrWhiteSpace(check.EndpointName) ? null : check.EndpointName.Trim(),
                Name = string.IsNullOrWhiteSpace(check.Name) ? check.Type.ToString().ToLowerInvariant() : check.Name.Trim()
            })
            .ToArray();

    private sealed record ResolvedEndpoint(
        Resource Resource,
        ResourceEndpoint Endpoint);

    private sealed record EndpointAssignment(
        string ResourceId,
        string EndpointName,
        string Identity);
}
