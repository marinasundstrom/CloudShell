using System.Text.Json;
using CloudShell.Abstractions.ResourceManager;
using Microsoft.Extensions.Hosting;

namespace CloudShell.ControlPlane.ResourceManager;

public sealed class PlatformResourceStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly object _gate = new();
    private readonly string _definitionsPath;
    private PlatformResourceDefinitions _definitions;

    public PlatformResourceStore(
        PlatformResourceOptions options,
        IHostEnvironment environment)
    {
        _definitionsPath = ResolvePath(options.DefinitionsPath, environment.ContentRootPath);
        _definitions = LoadDefinitions();

        foreach (var network in options.DeclaredNetworks)
        {
            UpsertNetwork(
                network.Definition,
                persist: false,
                replaceExisting: !network.Persist || network.OverwritePersistedState);
        }

        foreach (var service in options.DeclaredServices)
        {
            UpsertService(
                service.Definition,
                persist: false,
                replaceExisting: !service.Persist || service.OverwritePersistedState);
        }

        foreach (var loadBalancer in options.DeclaredLoadBalancers)
        {
            UpsertLoadBalancer(
                loadBalancer.Definition,
                persist: false,
                replaceExisting: !loadBalancer.Persist || loadBalancer.OverwritePersistedState);
        }
    }

    public IReadOnlyList<NetworkResourceDefinition> GetNetworks()
    {
        lock (_gate)
        {
            return _definitions.Networks
                .OrderBy(network => network.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    public NetworkResourceDefinition? GetNetwork(string id)
    {
        lock (_gate)
        {
            return _definitions.Networks.FirstOrDefault(network =>
                string.Equals(network.Id, id, StringComparison.OrdinalIgnoreCase));
        }
    }

    public IReadOnlyList<ServiceResourceDefinition> GetServices()
    {
        lock (_gate)
        {
            return _definitions.Services
                .OrderBy(service => service.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    public ServiceResourceDefinition? GetService(string id)
    {
        lock (_gate)
        {
            return _definitions.Services.FirstOrDefault(service =>
                string.Equals(service.Id, id, StringComparison.OrdinalIgnoreCase));
        }
    }

    public IReadOnlyList<LoadBalancerResourceDefinition> GetLoadBalancers()
    {
        lock (_gate)
        {
            return _definitions.LoadBalancers
                .OrderBy(loadBalancer => loadBalancer.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    public LoadBalancerResourceDefinition? GetLoadBalancer(string id)
    {
        lock (_gate)
        {
            return _definitions.LoadBalancers.FirstOrDefault(loadBalancer =>
                string.Equals(loadBalancer.Id, id, StringComparison.OrdinalIgnoreCase));
        }
    }

    public void SaveNetwork(NetworkResourceDefinition definition, bool persist = true)
    {
        lock (_gate)
        {
            UpsertNetwork(definition, persist, replaceExisting: true);
        }
    }

    public void SaveService(ServiceResourceDefinition definition, bool persist = true)
    {
        lock (_gate)
        {
            UpsertService(definition, persist, replaceExisting: true);
        }
    }

    public void SaveLoadBalancer(LoadBalancerResourceDefinition definition, bool persist = true)
    {
        lock (_gate)
        {
            UpsertLoadBalancer(definition, persist, replaceExisting: true);
        }
    }

    public void Remove(string id)
    {
        lock (_gate)
        {
            _definitions = _definitions with
            {
                Networks = _definitions.Networks
                    .Where(network => !string.Equals(network.Id, id, StringComparison.OrdinalIgnoreCase))
                    .ToArray(),
                Services = _definitions.Services
                    .Where(service => !string.Equals(service.Id, id, StringComparison.OrdinalIgnoreCase))
                    .ToArray(),
                LoadBalancers = _definitions.LoadBalancers
                    .Where(loadBalancer => !string.Equals(loadBalancer.Id, id, StringComparison.OrdinalIgnoreCase))
                    .ToArray()
            };
            Persist();
        }
    }

    private PlatformResourceDefinitions LoadDefinitions()
    {
        if (!File.Exists(_definitionsPath))
        {
            return new PlatformResourceDefinitions([], [], []);
        }

        var json = File.ReadAllText(_definitionsPath);
        var definitions = JsonSerializer.Deserialize<PlatformResourceDefinitions>(json, SerializerOptions)
            ?? new PlatformResourceDefinitions([], [], []);

        return definitions with
        {
            Networks = (definitions.Networks ?? []).Select(NormalizeNetwork).ToArray(),
            Services = (definitions.Services ?? []).Select(NormalizeService).ToArray(),
            LoadBalancers = (definitions.LoadBalancers ?? []).Select(NormalizeLoadBalancer).ToArray()
        };
    }

    private void UpsertNetwork(
        NetworkResourceDefinition definition,
        bool persist,
        bool replaceExisting)
    {
        var normalized = NormalizeNetwork(definition);
        var existing = _definitions.Networks.FirstOrDefault(network =>
            string.Equals(network.Id, normalized.Id, StringComparison.OrdinalIgnoreCase));
        if (existing is not null && !replaceExisting)
        {
            return;
        }

        _definitions = _definitions with
        {
            Networks = _definitions.Networks
                .Where(network => !string.Equals(network.Id, normalized.Id, StringComparison.OrdinalIgnoreCase))
                .Append(normalized)
                .ToArray()
        };

        if (persist)
        {
            Persist();
        }
    }

    private void UpsertService(
        ServiceResourceDefinition definition,
        bool persist,
        bool replaceExisting)
    {
        var normalized = NormalizeService(definition);
        var existing = _definitions.Services.FirstOrDefault(service =>
            string.Equals(service.Id, normalized.Id, StringComparison.OrdinalIgnoreCase));
        if (existing is not null && !replaceExisting)
        {
            return;
        }

        _definitions = _definitions with
        {
            Services = _definitions.Services
                .Where(service => !string.Equals(service.Id, normalized.Id, StringComparison.OrdinalIgnoreCase))
                .Append(normalized)
                .ToArray()
        };

        if (persist)
        {
            Persist();
        }
    }

    private void UpsertLoadBalancer(
        LoadBalancerResourceDefinition definition,
        bool persist,
        bool replaceExisting)
    {
        var normalized = NormalizeLoadBalancer(definition);
        var existing = _definitions.LoadBalancers.FirstOrDefault(loadBalancer =>
            string.Equals(loadBalancer.Id, normalized.Id, StringComparison.OrdinalIgnoreCase));
        if (existing is not null && !replaceExisting)
        {
            return;
        }

        _definitions = _definitions with
        {
            LoadBalancers = _definitions.LoadBalancers
                .Where(loadBalancer => !string.Equals(loadBalancer.Id, normalized.Id, StringComparison.OrdinalIgnoreCase))
                .Append(normalized)
                .ToArray()
        };

        if (persist)
        {
            Persist();
        }
    }

    private void Persist()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_definitionsPath)!);
        File.WriteAllText(_definitionsPath, JsonSerializer.Serialize(_definitions, SerializerOptions));
    }

    private static NetworkResourceDefinition NormalizeNetwork(NetworkResourceDefinition definition) =>
        definition with
        {
            Id = NormalizeId(definition.Id, "network", definition.Name),
            Name = definition.Name.Trim(),
            Endpoints = NormalizeEndpointRequests(definition.NetworkEndpoints),
            EndpointMappings = NormalizeEndpointMappings(definition.NetworkEndpointMappings),
            Kind = definition.Kind
        };

    private static ServiceResourceDefinition NormalizeService(ServiceResourceDefinition definition)
    {
        var id = NormalizeId(definition.Id, "service", definition.Name);
        return definition with
        {
            Id = id,
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
    }

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

    private static LoadBalancerResourceDefinition NormalizeLoadBalancer(
        LoadBalancerResourceDefinition definition) =>
        definition with
        {
            Id = NormalizeId(definition.Id, "load-balancer", definition.Name),
            Name = definition.Name.Trim(),
            Provider = string.IsNullOrWhiteSpace(definition.Provider)
                ? "traefik"
                : definition.Provider.Trim().ToLowerInvariant(),
            HostResourceId = NormalizeNullable(definition.HostResourceId),
            Entrypoints = definition.LoadBalancerEntrypoints
                .Where(entrypoint => !string.IsNullOrWhiteSpace(entrypoint.Name))
                .Select(entrypoint => entrypoint with
                {
                    Name = entrypoint.Name.Trim(),
                    Port = Math.Max(1, entrypoint.Port)
                })
                .DistinctBy(entrypoint => entrypoint.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            Routes = NormalizeLoadBalancerRoutes(definition.LoadBalancerRoutes)
        };

    private static IReadOnlyList<LoadBalancerRoute> NormalizeLoadBalancerRoutes(
        IReadOnlyList<LoadBalancerRoute> routes) =>
        routes
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
            .DistinctBy(route => route.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

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

    private static string NormalizeId(
        string id,
        string prefix,
        string name)
    {
        if (!string.IsNullOrWhiteSpace(id))
        {
            return id.Trim();
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

    private static string ResolvePath(string path, string contentRootPath) =>
        Path.IsPathRooted(path)
            ? path
            : Path.GetFullPath(path, contentRootPath);

    private sealed record PlatformResourceDefinitions(
        IReadOnlyList<NetworkResourceDefinition> Networks,
        IReadOnlyList<ServiceResourceDefinition> Services,
        IReadOnlyList<LoadBalancerResourceDefinition> LoadBalancers);
}
