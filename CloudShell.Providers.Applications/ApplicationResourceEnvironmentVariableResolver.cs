using CloudShell.Abstractions.Authorization;
using CloudShell.Abstractions.Observability;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.Client.Authentication;

namespace CloudShell.Providers.Applications;

public sealed class ApplicationResourceEnvironmentVariableResolver(
    ApplicationProviderOptions options,
    ResourceDeclarationStore declarations,
    ApplicationResourceSettingResolver settingResolver,
    IEnumerable<IResourceIdentityCredentialEnvironmentProvider> identityCredentialEnvironmentProviders,
    IEnumerable<IResourceEnvironmentVariableProvider> environmentVariableProviders)
{
    private readonly IReadOnlyList<IResourceIdentityCredentialEnvironmentProvider> identityCredentialEnvironmentProviders =
        identityCredentialEnvironmentProviders.ToArray();
    private readonly IReadOnlyList<IResourceEnvironmentVariableProvider> environmentVariableProviders =
        environmentVariableProviders.ToArray();

    public ResourceObservability GetEffectiveObservability(ApplicationResourceDefinition definition) =>
        definition.Observability ??
        (options.EnableObservabilityByDefault
            ? ResourceObservability.Default
            : ResourceObservability.None);

    public IReadOnlyList<EnvironmentVariableAssignment> ResolveWorkloadEnvironmentVariables(
        ApplicationResourceDefinition definition,
        string? resourceGroupId = null,
        IResourceManagerStore? resourceManager = null) =>
        (definition.UseServiceDiscovery
                ? ResolveServiceDiscoveryEnvironmentVariables(definition, resourceGroupId, resourceManager)
                : [])
            .Concat(ResolveObservabilityEnvironmentVariables(definition))
            .Concat(ResolveResourceIdentityEnvironmentVariables(definition))
            .Concat(definition.EnvironmentVariables)
            .Where(variable => !string.IsNullOrWhiteSpace(variable.Name))
            .GroupBy(variable => variable.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .ToArray();

    public async Task<IReadOnlyList<EnvironmentVariableAssignment>> ResolveRuntimeEnvironmentVariablesAsync(
        ApplicationResourceDefinition definition,
        IReadOnlyList<string> dependsOn,
        string? resourceGroupId,
        IResourceManagerStore? resourceManager,
        IReadOnlyList<EnvironmentVariableAssignment> providerRuntimeVariables,
        CancellationToken cancellationToken = default)
    {
        var configuredVariables = await settingResolver.ResolveConfiguredEnvironmentVariablesAsync(
            definition,
            resourceGroupId,
            cancellationToken);

        return ResolveDependencyEnvironmentVariables(definition, dependsOn)
            .Concat(definition.UseServiceDiscovery
                ? ResolveServiceDiscoveryEnvironmentVariables(definition, resourceGroupId, resourceManager)
                : [])
            .Concat(ResolveObservabilityEnvironmentVariables(definition))
            .Concat(providerRuntimeVariables)
            .Concat(ResolveResourceIdentityEnvironmentVariables(definition))
            .Concat(configuredVariables)
            .Where(variable => !string.IsNullOrWhiteSpace(variable.Name))
            .GroupBy(variable => variable.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .ToArray();
    }

    private IReadOnlyList<EnvironmentVariableAssignment> ResolveDependencyEnvironmentVariables(
        ApplicationResourceDefinition definition,
        IReadOnlyList<string> dependsOn) =>
        definition.DependsOn
            .Concat(dependsOn)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .SelectMany(dependency => environmentVariableProviders
                .SelectMany(provider => provider.GetEnvironmentVariables(dependency)))
            .Where(variable => !string.IsNullOrWhiteSpace(variable.Name))
            .GroupBy(variable => variable.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .ToArray();

    private IReadOnlyList<EnvironmentVariableAssignment> ResolveServiceDiscoveryEnvironmentVariables(
        ApplicationResourceDefinition definition,
        string? resourceGroupId,
        IResourceManagerStore? resourceManager)
    {
        if (resourceManager is null)
        {
            return [];
        }

        var references = definition.References
            .Where(reference => !string.IsNullOrWhiteSpace(reference))
            .Where(reference => IsSameResourceGroup(resourceManager.GetGroupForResource(reference)?.Id, resourceGroupId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (references.Count == 0)
        {
            return [];
        }

        return references
            .Select(reference => resourceManager.GetResource(reference))
            .Where(resource => resource is not null)
            .Cast<Resource>()
            .SelectMany(CreateServiceDiscoveryEndpointEnvironmentVariables)
            .GroupBy(variable => variable.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .ToArray();
    }

    private IReadOnlyList<EnvironmentVariableAssignment> ResolveObservabilityEnvironmentVariables(
        ApplicationResourceDefinition definition)
    {
        var observability = GetEffectiveObservability(definition);
        if (!observability.HasAnySignal)
        {
            return [];
        }

        var variables = new List<EnvironmentVariableAssignment>
        {
            new("OTEL_SERVICE_NAME", ApplicationResourceProjectionSupport.FirstNonEmpty(
                observability.ServiceName,
                ApplicationServiceDiscoveryDisplay.CreateConfigurationSegment(definition.Name),
                ApplicationServiceDiscoveryDisplay.CreateConfigurationSegment(definition.Id)) ?? definition.Id),
            new("OTEL_RESOURCE_ATTRIBUTES", CreateOtelResourceAttributes(definition, observability))
        };

        var endpoint = ApplicationResourceProjectionSupport.FirstNonEmpty(
            observability.OtlpEndpoint,
            options.OtlpEndpoint,
            Environment.GetEnvironmentVariable("ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL"));
        var protocol = ApplicationResourceProjectionSupport.FirstNonEmpty(
            observability.OtlpProtocol,
            options.OtlpProtocol,
            endpoint is null
                ? null
                : "grpc");

        if (endpoint is null)
        {
            endpoint = ApplicationResourceProjectionSupport.FirstNonEmpty(
                Environment.GetEnvironmentVariable("ASPIRE_DASHBOARD_OTLP_HTTP_ENDPOINT_URL"),
                Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT"));
            protocol = ApplicationResourceProjectionSupport.FirstNonEmpty(
                observability.OtlpProtocol,
                options.OtlpProtocol,
                Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_PROTOCOL"),
                Environment.GetEnvironmentVariable("ASPIRE_DASHBOARD_OTLP_HTTP_ENDPOINT_URL") is null
                    ? null
                    : "http/protobuf");
        }

        if (!string.IsNullOrWhiteSpace(endpoint))
        {
            variables.Add(new("OTEL_EXPORTER_OTLP_ENDPOINT", endpoint));
        }

        if (!string.IsNullOrWhiteSpace(protocol))
        {
            variables.Add(new("OTEL_EXPORTER_OTLP_PROTOCOL", protocol));
        }

        var headers = ApplicationResourceProjectionSupport.FirstNonEmpty(
            observability.OtlpHeaders,
            options.OtlpHeaders,
            Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_HEADERS"));
        if (!string.IsNullOrWhiteSpace(headers))
        {
            variables.Add(new("OTEL_EXPORTER_OTLP_HEADERS", headers));
        }

        return variables;
    }

    private IReadOnlyList<EnvironmentVariableAssignment> ResolveResourceIdentityEnvironmentVariables(
        ApplicationResourceDefinition definition)
    {
        var declaration = declarations.GetDeclaration(definition.Id);
        if (declaration?.IdentityBinding is null)
        {
            return [];
        }

        var providerCatalog = declarations.CreateIdentityProviderCatalog(
            new ResourceIdentityProviderCatalog());
        var resolution = providerCatalog.Resolve(declaration.IdentityBinding);
        if (resolution.Provider is null)
        {
            return [];
        }

        var identity = ResourceIdentityReference.ForResource(
            definition.Id,
            declaration.IdentityBinding.Name);
        var scope = declaration.IdentityBinding.IdentityScopes.Count == 0
            ? string.IsNullOrWhiteSpace(options.ResourceIdentityDefaultScope)
                ? "ControlPlane.Access"
                : options.ResourceIdentityDefaultScope
            : declaration.IdentityBinding.IdentityScopes[0];
        var credentialEnvironmentProvider = identityCredentialEnvironmentProviders.FirstOrDefault(provider =>
            provider.CanCreateEnvironment(resolution.Provider));
        if (credentialEnvironmentProvider is not null)
        {
            return credentialEnvironmentProvider
                .CreateEnvironment(new ResourceIdentityCredentialEnvironmentRequest(
                    resolution.Provider,
                    identity,
                    declaration.IdentityBinding,
                    scope))
                .Where(variable => !string.IsNullOrWhiteSpace(variable.Name))
                .ToArray();
        }

        var tokenEndpoint = options.ResourceIdentityTokenEndpoint;
        if (resolution.Provider.Kind != ResourceIdentityProviderKind.BuiltIn ||
            string.IsNullOrWhiteSpace(tokenEndpoint))
        {
            return [];
        }

        var clientId = CreateResourceIdentityClientId(identity);

        return
        [
            new(
                EnvironmentCloudShellResourceCredential.TokenEndpointEnvironmentVariable,
                tokenEndpoint),
            new(
                EnvironmentCloudShellResourceCredential.ClientIdEnvironmentVariable,
                clientId),
            new(
                EnvironmentCloudShellResourceCredential.ClientSecretEnvironmentVariable,
                ResolveBuiltInResourceIdentityClientSecret(resolution.Provider, clientId)),
            new(
                EnvironmentCloudShellResourceCredential.ScopeEnvironmentVariable,
                scope)
        ];
    }

    private string ResolveBuiltInResourceIdentityClientSecret(
        ResourceIdentityProviderDefinition provider,
        string clientId) =>
        provider.ProviderSettings.TryGetValue("clientSecret", out var configuredSecret) &&
        !string.IsNullOrWhiteSpace(configuredSecret)
            ? configuredSecret
            : $"local-development-{SanitizeResourceIdentityClientId(clientId)}-secret";

    private static IEnumerable<EnvironmentVariableAssignment> CreateServiceDiscoveryEndpointEnvironmentVariables(
        Resource resource)
    {
        foreach (var binding in ApplicationServiceDiscoveryDisplay.GetEndpointBindings(resource))
        {
            yield return new EnvironmentVariableAssignment(
                binding.EnvironmentVariableName,
                binding.Address);
        }
    }

    private static string CreateOtelResourceAttributes(
        ApplicationResourceDefinition definition,
        ResourceObservability observability)
    {
        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["service.instance.id"] = definition.Id,
            ["cloudshell.resource.id"] = definition.Id,
            ["cloudshell.resource.type"] = definition.ResourceType
        };

        foreach (var attribute in observability.Attributes)
        {
            if (!string.IsNullOrWhiteSpace(attribute.Key))
            {
                attributes[attribute.Key.Trim()] = attribute.Value;
            }
        }

        return string.Join(
            ',',
            attributes
                .Where(attribute => !string.IsNullOrWhiteSpace(attribute.Key))
                .Select(attribute => $"{attribute.Key}={EscapeOtelAttributeValue(attribute.Value)}"));
    }

    private static string EscapeOtelAttributeValue(string? value) =>
        (value ?? string.Empty)
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace(",", "\\,", StringComparison.Ordinal)
            .Replace("=", "\\=", StringComparison.Ordinal);

    private static bool IsSameResourceGroup(
        string? candidateResourceGroupId,
        string? resourceGroupId) =>
        string.Equals(
            NormalizeGroupId(candidateResourceGroupId),
            NormalizeGroupId(resourceGroupId),
            StringComparison.OrdinalIgnoreCase);

    private static string? NormalizeGroupId(string? resourceGroupId) =>
        string.IsNullOrWhiteSpace(resourceGroupId) ? null : resourceGroupId;

    private static string CreateResourceIdentityClientId(ResourceIdentityReference identity) =>
        string.IsNullOrWhiteSpace(identity.Name)
            ? identity.ResourceId
            : $"{identity.ResourceId}/{identity.Name}";

    private static string SanitizeResourceIdentityClientId(string value)
    {
        var characters = value
            .Select(character => char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : '-')
            .ToArray();
        return new string(characters).Trim('-');
    }
}
