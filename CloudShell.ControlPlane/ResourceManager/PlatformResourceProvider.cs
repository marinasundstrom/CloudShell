using System.Globalization;
using System.Text.Json;
using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.ControlPlane.ResourceManager;

public sealed class PlatformResourceProvider(
    PlatformResourceStore store,
    PlatformResourceOptions options) :
    IResourceProvider,
    IResourceCreationProvider,
    IResourceProcedureProvider,
    IProgrammaticResourceDeclarationProvider,
    IResourceAutoStartPolicyProvider,
    IResourceOrchestrationDescriptorProvider
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public const string ProviderId = "cloudshell.platform";
    public const string NetworkResourceType = "cloudshell.network";
    public const string ServiceResourceType = "cloudshell.service";

    public string Id => ProviderId;

    public string DisplayName => "CloudShell";

    public IReadOnlyList<Resource> GetResources() =>
    [
        .. store.GetNetworks().Select(CreateNetworkResource),
        .. store.GetServices().Select(CreateServiceResource)
    ];

    public bool CanCreate(ResourceCreationRequest request) =>
        string.Equals(request.ResourceType, NetworkResourceType, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(request.ResourceType, ServiceResourceType, StringComparison.OrdinalIgnoreCase);

    public async Task CreateAsync(
        ResourceCreationRequest request,
        ResourceCreationContext context,
        CancellationToken cancellationToken = default)
    {
        if (string.Equals(request.ResourceType, NetworkResourceType, StringComparison.OrdinalIgnoreCase))
        {
            var definition = request.Configuration.Deserialize<NetworkResourceDefinition>(SerializerOptions)
                ?? throw new InvalidOperationException("Network resource configuration is required.");
            await SetupNetworkAsync(
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
        string.Equals(resource.EffectiveTypeId, NetworkResourceType, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(resource.EffectiveTypeId, ServiceResourceType, StringComparison.OrdinalIgnoreCase);

    public Task<ResourceOrchestrationDescriptor> DescribeAsync(
        Resource resource,
        ResourceOrchestrationDescriptorContext context,
        CancellationToken cancellationToken = default)
    {
        if (string.Equals(resource.EffectiveTypeId, NetworkResourceType, StringComparison.OrdinalIgnoreCase))
        {
            var network = store.GetNetwork(resource.Id)
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

    private static Resource CreateNetworkResource(NetworkResourceDefinition definition) =>
        new(
            definition.Id,
            definition.Name,
            "Network",
            "CloudShell",
            "logical",
            ResourceState.Running,
            [new ResourceEndpoint("network", $"network://{definition.Id}", "network", false)],
            definition.IsDefault ? "host default" : "host local",
            DateTimeOffset.UtcNow,
            [],
            TypeId: NetworkResourceType,
            ResourceClass: ResourceClass.Network,
            Attributes: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [ResourceAttributeNames.NetworkKind] = definition.IsDefault ? "Default" : "Local",
                [ResourceAttributeNames.EndpointCount] = "1"
            });

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
            });

    private IReadOnlyList<ResourceEndpoint> CreateEndpoints(ServiceResourceDefinition definition) =>
        definition.Ports
            .Select(port =>
            {
                var exposedPort = port.Port ?? AssignLocalPort(definition.Id, port.Name);
                return new ResourceEndpoint(
                    port.Name,
                    $"{port.Protocol}://localhost:{exposedPort}",
                    port.Protocol,
                    true);
            })
            .ToArray();

    private int AssignLocalPort(string serviceId, string portName)
    {
        var start = Math.Max(1, options.AutoLocalPortStart);
        var end = Math.Max(start, options.AutoLocalPortEnd);
        var range = end - start + 1;
        return start + (int)(StableHash($"{serviceId}:{portName}") % (uint)range);
    }

    private static uint StableHash(string value)
    {
        const uint offset = 2166136261;
        const uint prime = 16777619;

        var hash = offset;
        foreach (var character in value)
        {
            hash ^= character;
            hash *= prime;
        }

        return hash;
    }

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
            Name = definition.Name.Trim()
        };

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
