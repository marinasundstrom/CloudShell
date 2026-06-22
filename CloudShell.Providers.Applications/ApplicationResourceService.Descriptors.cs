using CloudShell.Abstractions.ResourceManager;
using System.Text.Json;

namespace CloudShell.Providers.Applications;

public sealed partial class ApplicationResourceService
{
    public bool CanDescribe(Resource resource) =>
        ApplicationResourceTypes.IsApplication(resource.EffectiveTypeId) &&
        store.GetApplication(resource.Id) is not null;

    public async Task CleanupHostScopedResourcesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var application in GetApplications())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (application.Lifetime != ApplicationLifetime.ControlPlaneScoped ||
                IsContainerBacked(application))
            {
                continue;
            }

            await localProcesses.CleanupHostScopedProcessAsync(
                ApplicationProcessDefinitions.Create(application),
                cancellationToken);
        }
    }

    public Task<ResourceOrchestrationDescriptor> DescribeAsync(
        Resource resource,
        ResourceOrchestrationDescriptorContext context,
        CancellationToken cancellationToken = default)
    {
        var application = GetApplication(resource.Id)
            ?? throw new InvalidOperationException($"Application resource '{resource.Id}' is not configured.");

        var workload = CreateWorkloadConfiguration(
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
            JsonSerializer.SerializeToElement(workload, TemplateSerializerOptions)));
    }
}
