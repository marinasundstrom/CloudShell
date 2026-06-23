namespace CloudShell.Providers.Applications;

public sealed class AspNetCoreProjectEndpointNormalizationRule :
    IApplicationResourceDefinitionNormalizationRule
{
    public bool AppliesTo(ApplicationResourceDefinition definition) =>
        ApplicationResourceTypes.IsAspNetCoreProject(definition.ResourceType);

    public ApplicationResourceDefinition Normalize(
        ApplicationResourceDefinition definition,
        ApplicationResourceDefinitionNormalizationContext context) =>
        Resolve(definition, context);

    public ApplicationResourceDefinition Resolve(
        ApplicationResourceDefinition definition,
        ApplicationResourceDefinitionNormalizationContext context)
    {
        if (definition.EndpointPorts.Count > 0)
        {
            return definition;
        }

        var endpointPorts = definition.UseLaunchSettingsEndpoints
            ? context.TryReadLaunchSettingsEndpointPorts(definition.ProjectPath)
            : [];
        return endpointPorts.Count == 0
            ? definition with
            {
                EndpointPorts = ApplicationResourceDefinitionNormalizationContext
                    .CreateAspNetCoreProjectEndpointPorts(definition.Endpoint)
            }
            : definition with { EndpointPorts = endpointPorts };
    }
}
