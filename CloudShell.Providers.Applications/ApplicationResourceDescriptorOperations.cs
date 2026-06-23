using CloudShell.Abstractions.ResourceManager;
using System.Text.Json;

namespace CloudShell.Providers.Applications;

internal sealed class ApplicationResourceDescriptorOperations(
    IApplicationResourceDefinitionSource definitions,
    ApplicationWorkloadConfigurationProvider workloadConfigurations) : IApplicationResourceDescriptorOperations
{
    private static readonly JsonSerializerOptions DescriptorSerializerOptions = new(JsonSerializerDefaults.Web);

    public bool CanDescribe(Resource resource) =>
        ApplicationResourceTypes.IsApplication(resource.EffectiveTypeId) &&
        definitions.GetApplication(resource.Id) is not null;

    public Task<ResourceOrchestrationDescriptor> DescribeAsync(
        Resource resource,
        ResourceOrchestrationDescriptorContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var application = definitions.GetApplication(resource.Id)
            ?? throw new InvalidOperationException($"Application resource '{resource.Id}' is not configured.");

        var workload = workloadConfigurations.Create(
            application,
            context.ResourceGroup?.Id,
            context.ResourceManager);
        return Task.FromResult(new ResourceOrchestrationDescriptor(
            resource.Id,
            resource.EffectiveTypeId,
            resource.DependsOn,
            [],
            resource.Endpoints,
            "1.0",
            JsonSerializer.SerializeToElement(workload, DescriptorSerializerOptions)));
    }
}
