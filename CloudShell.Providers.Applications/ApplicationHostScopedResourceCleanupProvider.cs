using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Providers.Applications;

internal sealed class ApplicationHostScopedResourceCleanupProvider(
    IApplicationResourceDefinitionSource definitions,
    LocalProcessRunner localProcesses) : IHostScopedResourceCleanupProvider
{
    public async Task CleanupHostScopedResourcesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var application in definitions.GetApplications())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (application.Lifetime != ApplicationLifetime.ControlPlaneScoped ||
                ApplicationResourceProjectionSupport.IsContainerBacked(application))
            {
                continue;
            }

            await localProcesses.CleanupHostScopedProcessAsync(
                ApplicationProcessDefinitions.Create(application),
                cancellationToken);
        }
    }
}
