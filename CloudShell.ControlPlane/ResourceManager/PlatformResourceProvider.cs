using System.Globalization;
using System.Text.Json;
using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.ControlPlane.ResourceManager;

public sealed class PlatformResourceProvider(
    PlatformResourceStore store,
    PlatformResourceOptions options,
    IHostLocalNetworkEnvironment? hostLocalNetworkEnvironment = null) :
    IResourceProvider,
    IResourceCreationProvider,
    IResourceProcedureProvider,
    IProgrammaticResourceDeclarationProvider,
    IResourceAutoStartPolicyProvider,
    IResourceOrchestrationDescriptorProvider
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public const string ProviderId = "cloudshell.platform";
    public const string HostNetworkResourceId = "network:host";
    public const string NetworkResourceType = "cloudshell.network";
    public const string VirtualNetworkResourceType = "cloudshell.virtualNetwork";
    public const string ServiceResourceType = "cloudshell.service";
    public const string ReconcileEndpointMappingsActionId = "reconcileEndpointMappings";
    private readonly IHostLocalNetworkEnvironment hostLocalNetwork =
        hostLocalNetworkEnvironment ?? new HostLocalNetworkEnvironment();

    private static readonly ResourceAction ReconcileEndpointMappingsAction = new(
        ReconcileEndpointMappingsActionId,
        "Reconcile endpoint mappings",
        Description: "Validate and apply endpoint mappings for the network resource.");

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
            .. store.GetServices().Select(CreateServiceResource)
        ];
    }

    public bool CanCreate(ResourceCreationRequest request) =>
        IsNetworkResourceType(request.ResourceType) ||
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
        store.SaveService(normalized);
        await registrations.RegisterAsync(
            Id,
            normalized.Id,
            NormalizeGroupId(resourceGroupId),
            CreateServiceDependencies(normalized),
            cancellationToken);
    }

    public async Task<ResourceProcedureResult> DeleteAsync(
        ResourceProcedureContext context,
        CancellationToken cancellationToken = default)
    {
        store.Remove(context.Resource.Id);
        await context.Registrations.RemoveAsync(context.Resource.Id, cancellationToken);
        return ResourceProcedureResult.Completed("Platform resource registration removed.");
    }

    public Task<ResourceProcedureResult> ExecuteActionAsync(
        ResourceProcedureContext context,
        ResourceAction action,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(action.Id, ReconcileEndpointMappingsActionId, StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException(
                $"CloudShell platform resources do not support action '{action.DisplayName}'.");
        }

        if (!IsNetworkResourceType(context.Resource.EffectiveTypeId))
        {
            throw new InvalidOperationException(
                $"Endpoint mappings can only be reconciled for network resources.");
        }

        return Task.FromResult(ReconcileEndpointMappings(context));
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
            if (declaration.Persistence == ResourceDeclarationPersistence.Persisted)
            {
                store.SaveNetwork(
                    declaredNetwork.Definition,
                    persist: true);
            }

            return registrations.RegisterAsync(
                Id,
                declaredNetwork.Definition.Id,
                NormalizeGroupId(declaration.ResourceGroupId),
                declaration.DependsOn,
                cancellationToken);
        }

        var declaredService = options.DeclaredServices.FirstOrDefault(service =>
            string.Equals(service.Definition.Id, declaration.ResourceId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                $"Platform resource declaration '{declaration.ResourceId}' was not found.");

        if (declaration.Persistence == ResourceDeclarationPersistence.Persisted)
        {
            store.SaveService(
                declaredService.Definition,
                persist: true);
        }

        return registrations.RegisterAsync(
            Id,
            declaredService.Definition.Id,
            NormalizeGroupId(declaration.ResourceGroupId),
            CreateServiceDependencies(declaredService.Definition)
                .Concat(declaration.DependsOn)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            cancellationToken);
    }

    public bool CanDescribe(Resource resource) =>
        IsNetworkResourceType(resource.EffectiveTypeId) ||
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
            Attributes: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [ResourceAttributeNames.NetworkKind] = GetNetworkKindAttribute(definition),
                [ResourceAttributeNames.EndpointCount] = endpoints.Count.ToString(CultureInfo.InvariantCulture)
            },
            Capabilities: CreateNetworkCapabilities(definition));
    }

    private ResourceProcedureResult ReconcileEndpointMappings(ResourceProcedureContext context)
    {
        var resourceManager = context.ResourceManager
            ?? throw new InvalidOperationException("Resource Manager is required to reconcile endpoint mappings.");
        var network = GetNetworkDefinition(context.Resource.Id)
            ?? throw new InvalidOperationException($"Network resource '{context.Resource.Id}' is not configured.");
        if (network.NetworkEndpointMappings.Count == 0)
        {
            return ResourceProcedureResult.Completed("No endpoint mappings to reconcile.");
        }

        foreach (var mapping in network.NetworkEndpointMappings)
        {
            ValidateEndpointReference(resourceManager, mapping.Id, mapping.Source, "source");
            ValidateEndpointReference(resourceManager, mapping.Id, mapping.Target, "target");
            ValidateMappingProvider(resourceManager, mapping);
        }

        return ResourceProcedureResult.Completed(
            $"Reconciled {network.NetworkEndpointMappings.Count} endpoint mapping(s).");
    }

    private static void ValidateEndpointReference(
        IResourceManagerStore resourceManager,
        string mappingId,
        ResourceEndpointReference endpoint,
        string role)
    {
        var resource = resourceManager.GetResource(endpoint.ResourceId)
            ?? throw new InvalidOperationException(
                $"Endpoint mapping '{mappingId}' {role} resource '{endpoint.ResourceId}' could not be found.");
        if (!resource.Endpoints.Any(candidate =>
                string.Equals(candidate.Name, endpoint.EndpointName, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                $"Endpoint mapping '{mappingId}' {role} endpoint '{endpoint.EndpointName}' could not be found on resource '{endpoint.ResourceId}'.");
        }
    }

    private static void ValidateMappingProvider(
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

    private static IReadOnlyList<string> CreateServiceDependencies(ServiceResourceDefinition definition) =>
        definition.Targets
            .Select(target => target.ResourceId)
            .Concat(definition.NetworkIds)
            .Where(dependency => !string.IsNullOrWhiteSpace(dependency))
            .Select(dependency => dependency.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

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
}
