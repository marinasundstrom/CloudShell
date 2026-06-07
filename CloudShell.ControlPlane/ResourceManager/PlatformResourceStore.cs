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
                    .ToArray()
            };
            Persist();
        }
    }

    private PlatformResourceDefinitions LoadDefinitions()
    {
        if (!File.Exists(_definitionsPath))
        {
            return new PlatformResourceDefinitions([], []);
        }

        var json = File.ReadAllText(_definitionsPath);
        var definitions = JsonSerializer.Deserialize<PlatformResourceDefinitions>(json, SerializerOptions)
            ?? new PlatformResourceDefinitions([], []);

        return definitions with
        {
            Networks = definitions.Networks.Select(NormalizeNetwork).ToArray(),
            Services = definitions.Services.Select(NormalizeService).ToArray()
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

    private void Persist()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_definitionsPath)!);
        File.WriteAllText(_definitionsPath, JsonSerializer.Serialize(_definitions, SerializerOptions));
    }

    private static NetworkResourceDefinition NormalizeNetwork(NetworkResourceDefinition definition) =>
        definition with
        {
            Id = NormalizeId(definition.Id, "network", definition.Name),
            Name = definition.Name.Trim()
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
                .ToArray()
        };
    }

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
        IReadOnlyList<ServiceResourceDefinition> Services);
}
