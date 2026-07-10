using Microsoft.Extensions.Configuration;

namespace CloudShell.ControlPlane.Providers;

public sealed class AspNetCoreProjectSqlServerCredentialEnvironmentResolver(
    IConfiguration configuration,
    ResourceGraphModel? graphModel = null) : IAspNetCoreProjectRuntimeEnvironmentProvider
{
    private const string CredentialEndpointEnvironmentVariable = "CLOUDSHELL_SQL_CREDENTIAL_ENDPOINT";
    private const string DefaultControlPlaneEndpoint = "http://127.0.0.1:5112";
    private readonly IConfiguration configuration = configuration;
    private readonly ResourceGraphModel? graphModel = graphModel;

    public async ValueTask<IReadOnlyDictionary<string, string>> ResolveAsync(
        Resource resource,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resource);

        var references = resource.Attributes.GetObject<ResourceReference[]>(
            AspNetCoreProjectResourceTypeProvider.Attributes.References) ?? [];
        if (graphModel is null || references.Length == 0)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var snapshot = await graphModel.GetSnapshotAsync(cancellationToken);
        var resources = snapshot.Resources.ToDictionary(
            state => state.EffectiveResourceId,
            StringComparer.OrdinalIgnoreCase);
        var sqlServers = references
            .Select(reference =>
                reference.TryGetResourceId(out var resourceId) &&
                resources.TryGetValue(resourceId, out var dependency)
                    ? dependency
                    : null)
            .Where(resourceState =>
                resourceState?.TypeId == SqlServerResourceTypeProvider.ResourceTypeId)
            .Cast<ResourceState>()
            .ToArray();
        if (sqlServers.Length == 0)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var endpoint = ResolveCredentialEndpoint();
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [CredentialEndpointEnvironmentVariable] = endpoint
        };
        foreach (var sqlServer in sqlServers)
        {
            foreach (var segment in ResolveCloudShellClientEnvironmentSegments(sqlServer))
            {
                variables[$"CLOUDSHELL_SQL_{segment}_CREDENTIAL_ENDPOINT"] = endpoint;
            }
        }

        return variables;
    }

    private string ResolveCredentialEndpoint()
    {
        var configuredEndpoint =
            configuration["CloudShell:SqlServer:CredentialEndpoint"] ??
            configuration[CredentialEndpointEnvironmentVariable];
        if (!string.IsNullOrWhiteSpace(configuredEndpoint))
        {
            return configuredEndpoint.Trim();
        }

        return $"{ResolveControlPlaneEndpoint().TrimEnd('/')}/api/sql-server/v1/credentials";
    }

    private string ResolveControlPlaneEndpoint() =>
        configuration["CloudShell:ControlPlane:BaseAddress"] ??
        configuration["CloudShell:PublicEndpoint"] ??
        configuration["CLOUDSHELL_CONTROL_PLANE_ENDPOINT"] ??
        configuration["CLOUDSHELL_CONTROL_PLANE_URL"] ??
        DefaultControlPlaneEndpoint;

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
