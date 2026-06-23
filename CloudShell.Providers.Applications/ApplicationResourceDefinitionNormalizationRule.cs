using CloudShell.Abstractions.ResourceManager;
using Microsoft.Extensions.Hosting;

namespace CloudShell.Providers.Applications;

public interface IApplicationResourceDefinitionNormalizationRule
{
    bool AppliesTo(ApplicationResourceDefinition definition);

    ApplicationResourceDefinition Normalize(
        ApplicationResourceDefinition definition,
        ApplicationResourceDefinitionNormalizationContext context);

    ApplicationResourceDefinition Resolve(
        ApplicationResourceDefinition definition,
        ApplicationResourceDefinitionNormalizationContext context) =>
        definition;
}

public sealed class ApplicationResourceDefinitionNormalizationContext(IHostEnvironment environment)
{
    private readonly AspNetCoreProjectEndpointDefinitionFactory aspNetCoreEndpoints = new(environment.ContentRootPath);

    public IReadOnlyList<ServicePort> TryReadLaunchSettingsEndpointPorts(string? projectPath) =>
        aspNetCoreEndpoints.TryReadLaunchSettingsEndpointPorts(projectPath);

    public static IReadOnlyList<ServicePort> CreateAspNetCoreProjectEndpointPorts(string? endpoint) =>
        AspNetCoreProjectEndpointDefinitionFactory.CreateEndpointPorts(endpoint);
}
