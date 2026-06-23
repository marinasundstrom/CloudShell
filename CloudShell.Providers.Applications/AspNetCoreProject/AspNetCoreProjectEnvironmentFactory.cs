using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Providers.Applications;

internal sealed class AspNetCoreProjectEnvironmentFactory(
    Func<ApplicationResourceDefinition, IReadOnlyList<ResourceEndpointNetworkMapping>> createEndpointNetworkMappings)
{
    public const string AspNetCoreUrlsEnvironmentVariable = "ASPNETCORE_URLS";
    public const string DotNetWatchRestartOnRudeEditEnvironmentVariable = "DOTNET_WATCH_RESTART_ON_RUDE_EDIT";

    public IReadOnlyList<EnvironmentVariableAssignment> Create(
        ApplicationResourceDefinition definition,
        IResourceManagerStore? resourceManager = null)
    {
        var urls = ResolveEndpointUrls(definition, resourceManager);
        List<EnvironmentVariableAssignment> variables = [];

        if (urls.Count > 0)
        {
            variables.Add(new EnvironmentVariableAssignment(
                AspNetCoreUrlsEnvironmentVariable,
                string.Join(';', urls)));
        }

        if (definition.AspNetCoreHotReload)
        {
            variables.Add(new EnvironmentVariableAssignment(DotNetWatchRestartOnRudeEditEnvironmentVariable, "true"));
        }

        return variables;
    }

    public IReadOnlyList<string> ResolveEndpointUrls(
        ApplicationResourceDefinition definition,
        IResourceManagerStore? resourceManager = null)
    {
        if (!ApplicationResourceTypes.IsAspNetCoreProject(definition.ResourceType))
        {
            return [];
        }

        var projectedUrls = resourceManager?
            .GetResource(definition.Id)?
            .ResourceEndpointNetworkMappings
            .Where(mapping => string.Equals(
                mapping.Target.ResourceId,
                definition.Id,
                StringComparison.OrdinalIgnoreCase))
            .Select(mapping => mapping.Address)
            .Where(address => !string.IsNullOrWhiteSpace(address))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (projectedUrls is { Length: > 0 })
        {
            return projectedUrls;
        }

        return createEndpointNetworkMappings(definition)
            .Select(mapping => mapping.Address)
            .Where(address => !string.IsNullOrWhiteSpace(address))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
