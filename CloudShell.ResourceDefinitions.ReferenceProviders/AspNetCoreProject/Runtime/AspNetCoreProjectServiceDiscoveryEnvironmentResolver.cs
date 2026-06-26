namespace CloudShell.ResourceDefinitions.ReferenceProviders;

public sealed class AspNetCoreProjectServiceDiscoveryEnvironmentResolver(
    ResourceGraphModel? graphModel = null)
{
    private readonly ResourceGraphModel? _graphModel = graphModel;

    public async ValueTask<IReadOnlyDictionary<string, string>> ResolveAsync(
        Resource resource,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resource);

        var references = resource.Attributes.GetObject<ResourceReference[]>(
            AspNetCoreProjectResourceTypeProvider.Attributes.References) ?? [];
        if (_graphModel is null || references.Length == 0)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var snapshot = await _graphModel.GetSnapshotAsync(cancellationToken);
        var resources = snapshot.Resources.ToDictionary(
            state => state.EffectiveResourceId,
            StringComparer.OrdinalIgnoreCase);
        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var reference in references)
        {
            if (!reference.TryGetResourceId(out var resourceId) ||
                !resources.TryGetValue(resourceId, out var dependency))
            {
                continue;
            }

            AddServiceDiscoveryVariables(dependency, variables);
        }

        return variables;
    }

    private static void AddServiceDiscoveryVariables(
        ResourceState resource,
        Dictionary<string, string> variables)
    {
        var endpoints = resource.ResourceAttributeValues
            .GetObject<NetworkingEndpointRequestValue[]>(
                AspNetCoreProjectResourceTypeProvider.Attributes.EndpointRequests) ?? [];

        foreach (var endpoint in endpoints)
        {
            var address = CreateEndpointAddress(endpoint);
            if (address is null)
            {
                continue;
            }

            foreach (var endpointKey in GetEndpointKeys(endpoint))
            {
                foreach (var serviceName in ResolveServiceDiscoveryNames(resource))
                {
                    variables[$"services__{serviceName}__{endpointKey}__0"] = address;
                }
            }
        }
    }

    private static IReadOnlyList<string> ResolveServiceDiscoveryNames(ResourceState resource)
    {
        var configured = resource.ResourceAttributeValues.TryGetValue(
            AspNetCoreProjectResourceTypeProvider.Attributes.ServiceDiscoveryName,
            out var value) &&
            value.TryGetScalarString(out var serviceDiscoveryName)
                ? serviceDiscoveryName
                : null;

        return new[]
            {
                configured,
                resource.Name,
                resource.EffectiveResourceId
            }
            .Select(CreateConfigurationSegment)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Cast<string>()
            .ToArray();
    }

    private static IReadOnlyList<string> GetEndpointKeys(NetworkingEndpointRequestValue endpoint)
    {
        var keys = new List<string>();
        AddIfValid(keys, endpoint.Name);
        AddIfValid(keys, endpoint.Protocol);
        return keys.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string? CreateEndpointAddress(NetworkingEndpointRequestValue endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint.Protocol) ||
            endpoint.Port is not > 0)
        {
            return null;
        }

        var host = FirstNonEmpty(endpoint.Host, endpoint.IpAddress);
        if (host is null)
        {
            return null;
        }

        var protocol = endpoint.Protocol.Trim().ToLowerInvariant();
        return protocol is "http" or "https"
            ? $"{protocol}://{host}:{endpoint.Port.Value}"
            : $"{host}:{endpoint.Port.Value}";
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    private static void AddIfValid(List<string> values, string? value)
    {
        var segment = CreateConfigurationSegment(value);
        if (!string.IsNullOrWhiteSpace(segment))
        {
            values.Add(segment);
        }
    }

    private static string? CreateConfigurationSegment(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var characters = value
            .Trim()
            .ToLowerInvariant()
            .Select(character =>
                char.IsAsciiLetterOrDigit(character) ||
                character is '_' or '.' or '-'
                    ? character
                    : '-')
            .ToArray();

        return new string(characters).Trim('-');
    }
}
