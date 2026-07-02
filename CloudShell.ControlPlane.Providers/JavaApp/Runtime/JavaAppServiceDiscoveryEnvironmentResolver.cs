namespace CloudShell.ControlPlane.Providers;

public sealed class JavaAppServiceDiscoveryEnvironmentResolver(
    ResourceGraphModel? graphModel = null) : IJavaAppRuntimeEnvironmentProvider
{
    private readonly ResourceGraphModel? _graphModel = graphModel;

    public async ValueTask<IReadOnlyDictionary<string, string>> ResolveAsync(
        Resource resource,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resource);

        var references = resource.Attributes.GetObject<ResourceReference[]>(
            JavaAppResourceTypeProvider.Attributes.References) ?? [];
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
        foreach (var endpoint in GetEndpointRequests(resource))
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

        AddEndpointAttribute(
            resource,
            variables,
            ConfigurationStoreResourceTypeProvider.ResourceTypeId,
            ConfigurationStoreResourceTypeProvider.Attributes.Endpoint,
            "entries");
        AddConfigurationStoreClientVariables(resource, variables);
        AddEndpointAttribute(
            resource,
            variables,
            SecretsVaultResourceTypeProvider.ResourceTypeId,
            SecretsVaultResourceTypeProvider.Attributes.Endpoint,
            "secrets");
        AddSecretsVaultClientVariables(resource, variables);
    }

    private static IReadOnlyList<NetworkingEndpointRequestValue> GetEndpointRequests(
        ResourceState resource)
    {
        if (resource.TypeId == JavaAppResourceTypeProvider.ResourceTypeId)
        {
            return resource.ResourceAttributeValues.GetObject<NetworkingEndpointRequestValue[]>(
                JavaAppResourceTypeProvider.Attributes.EndpointRequests) ?? [];
        }

        if (resource.TypeId == JavaScriptAppResourceTypeProvider.ResourceTypeId)
        {
            return resource.ResourceAttributeValues.GetObject<NetworkingEndpointRequestValue[]>(
                JavaScriptAppResourceTypeProvider.Attributes.EndpointRequests) ?? [];
        }

        if (resource.TypeId == ContainerApplicationResourceTypeProvider.ResourceTypeId)
        {
            return resource.ResourceAttributeValues.GetObject<NetworkingEndpointRequestValue[]>(
                ContainerApplicationResourceTypeProvider.Attributes.EndpointRequests) ?? [];
        }

        if (resource.TypeId == SqlServerResourceTypeProvider.ResourceTypeId)
        {
            return resource.ResourceAttributeValues.GetObject<NetworkingEndpointRequestValue[]>(
                SqlServerResourceTypeProvider.Attributes.EndpointRequests) ?? [];
        }

        return resource.ResourceAttributeValues.GetObject<NetworkingEndpointRequestValue[]>(
            AspNetCoreProjectResourceTypeProvider.Attributes.EndpointRequests) ?? [];
    }

    private static void AddEndpointAttribute(
        ResourceState resource,
        Dictionary<string, string> variables,
        ResourceTypeId typeId,
        ResourceAttributeId attributeId,
        string endpointName)
    {
        if (resource.TypeId != typeId ||
            !resource.ResourceAttributeValues.TryGetValue(attributeId, out var value) ||
            !value.TryGetScalarString(out var address) ||
            !Uri.TryCreate(address, UriKind.Absolute, out var endpointUri))
        {
            return;
        }

        foreach (var endpointKey in GetEndpointKeys(endpointName, endpointUri.Scheme))
        {
            foreach (var serviceName in ResolveServiceDiscoveryNames(resource))
            {
                variables[$"services__{serviceName}__{endpointKey}__0"] = address;
            }
        }
    }

    private static void AddConfigurationStoreClientVariables(
        ResourceState resource,
        Dictionary<string, string> variables)
    {
        if (resource.TypeId != ConfigurationStoreResourceTypeProvider.ResourceTypeId ||
            !TryGetAbsoluteEndpoint(
                resource,
                ConfigurationStoreResourceTypeProvider.Attributes.Endpoint,
                out var baseEndpoint))
        {
            return;
        }

        var entriesEndpoint =
            $"{baseEndpoint.ToString().TrimEnd('/')}/api/configuration/stores/{Uri.EscapeDataString(resource.EffectiveResourceId)}/entries";
        variables.TryAdd("CLOUDSHELL_CONFIGURATION_SERVICE_NAME", resource.Name);
        foreach (var segment in ResolveCloudShellClientEnvironmentSegments(resource))
        {
            variables[$"CLOUDSHELL_CONFIGURATION_{segment}_STORE_ID"] =
                resource.EffectiveResourceId;
            variables[$"CLOUDSHELL_CONFIGURATION_{segment}_ENDPOINT"] =
                entriesEndpoint;
        }
    }

    private static void AddSecretsVaultClientVariables(
        ResourceState resource,
        Dictionary<string, string> variables)
    {
        if (resource.TypeId != SecretsVaultResourceTypeProvider.ResourceTypeId ||
            !TryGetAbsoluteEndpoint(
                resource,
                SecretsVaultResourceTypeProvider.Attributes.Endpoint,
                out var baseEndpoint))
        {
            return;
        }

        var secretsEndpoint =
            $"{baseEndpoint.ToString().TrimEnd('/')}/api/secrets/vaults/{Uri.EscapeDataString(resource.EffectiveResourceId)}/secrets";
        variables.TryAdd("CLOUDSHELL_SECRETS_VAULT_NAME", resource.Name);
        foreach (var segment in ResolveCloudShellClientEnvironmentSegments(resource))
        {
            variables[$"CLOUDSHELL_SECRETS_{segment}_VAULT_ID"] =
                resource.EffectiveResourceId;
            variables[$"CLOUDSHELL_SECRETS_{segment}_ENDPOINT"] =
                secretsEndpoint;
        }
    }

    private static bool TryGetAbsoluteEndpoint(
        ResourceState resource,
        ResourceAttributeId attributeId,
        out Uri endpoint)
    {
        endpoint = null!;
        if (resource.ResourceAttributeValues.TryGetValue(attributeId, out var value) &&
            value.TryGetScalarString(out var address) &&
            Uri.TryCreate(address, UriKind.Absolute, out var parsedEndpoint))
        {
            endpoint = parsedEndpoint;
            return true;
        }

        return false;
    }

    private static IReadOnlyList<string> ResolveServiceDiscoveryNames(ResourceState resource)
    {
        var configured = resource.ResourceAttributeValues.TryGetValue(
            JavaAppResourceTypeProvider.Attributes.ServiceDiscoveryName,
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

    private static IReadOnlyList<string> ResolveCloudShellClientEnvironmentSegments(
        ResourceState resource) =>
        new[]
            {
                resource.Name,
                resource.EffectiveResourceId
            }
            .Select(CreateEnvironmentSegment)
            .Where(segment => !string.IsNullOrWhiteSpace(segment))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Cast<string>()
            .ToArray();

    private static IReadOnlyList<string> GetEndpointKeys(NetworkingEndpointRequestValue endpoint)
    {
        var keys = new List<string>();
        AddIfValid(keys, endpoint.Name);
        AddIfValid(keys, endpoint.Protocol);
        return keys.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static IReadOnlyList<string> GetEndpointKeys(
        string endpointName,
        string protocol)
    {
        var keys = new List<string>();
        AddIfValid(keys, endpointName);
        AddIfValid(keys, protocol);
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

    private static string? CreateEnvironmentSegment(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var characters = value
            .Trim()
            .Select(character => char.IsLetterOrDigit(character) ? char.ToUpperInvariant(character) : '_')
            .ToArray();

        return new string(characters).Trim('_');
    }
}
