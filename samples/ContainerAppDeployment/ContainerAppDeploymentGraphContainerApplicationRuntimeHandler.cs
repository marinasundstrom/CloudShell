using CloudShell.Abstractions.ControlPlane;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.Providers.Applications;
using CloudShell.ResourceDefinitions;
using CloudShell.ResourceDefinitions.ReferenceProviders;
using Microsoft.Extensions.DependencyInjection;
using GraphResource = CloudShell.ResourceDefinitions.Resource;

internal sealed class ContainerAppDeploymentGraphContainerApplicationRuntimeHandler(
    IServiceScopeFactory scopeFactory) : IContainerApplicationRuntimeHandler
{
    private const string GraphAppResourceId = "application.container-app:graph-sample-api";
    private const string RuntimeAppResourceId = "application:sample-api";

    public ContainerApplicationRuntimeStatus GetStatus(GraphResource resource)
    {
        if (!IsGraphApp(resource))
        {
            return ContainerApplicationRuntimeStatus.Unknown;
        }

        using var scope = scopeFactory.CreateScope();
        var runningState = scope.ServiceProvider
            .GetRequiredService<IApplicationResourceRunningStateOperations>();
        return runningState.IsRunning(RuntimeAppResourceId)
            ? ContainerApplicationRuntimeStatus.Running
            : ContainerApplicationRuntimeStatus.Stopped;
    }

    public async ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ExecuteLifecycleAsync(
        GraphResource resource,
        ResourceOperationId operationId,
        CancellationToken cancellationToken = default)
    {
        if (!IsGraphApp(resource))
        {
            return [];
        }

        try
        {
            using var scope = scopeFactory.CreateScope();
            var resourceManager = scope.ServiceProvider.GetRequiredService<IResourceManager>();
            var result = operationId.ToString() switch
            {
                ResourceActionIds.Start => await resourceManager.StartResourceAsync(
                    RuntimeAppResourceId,
                    startDependencies: true,
                    cancellationToken),
                ResourceActionIds.Stop => await resourceManager.StopResourceAsync(
                    RuntimeAppResourceId,
                    ignoreDependentWarning: true,
                    cancellationToken),
                ResourceActionIds.Restart => await resourceManager.RestartResourceAsync(
                    RuntimeAppResourceId,
                    startDependencies: true,
                    ignoreDependentWarning: true,
                    cancellationToken),
                _ => throw new NotSupportedException(
                    $"The ContainerAppDeployment sample does not map graph operation '{operationId}' to the runtime app.")
            };

            return ToDiagnostics(result, resource.EffectiveResourceId);
        }
        catch (Exception exception)
        {
            return
            [
                ResourceDefinitionDiagnostic.Error(
                    "containerAppDeployment.containerApp.runtimeLifecycleFailed",
                    exception.Message,
                    resource.EffectiveResourceId)
            ];
        }
    }

    public async ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ApplyImageAsync(
        GraphResource resource,
        CancellationToken cancellationToken = default)
    {
        if (!IsGraphApp(resource))
        {
            return [];
        }

        var image = resource.Attributes.GetString(
            ContainerApplicationResourceTypeProvider.Attributes.ContainerImage);
        if (string.IsNullOrWhiteSpace(image))
        {
            return
            [
                ResourceDefinitionDiagnostic.Error(
                    "containerAppDeployment.containerApp.imageRequired",
                    "The graph container app image must be set before the sample runtime image can be updated.",
                    ContainerApplicationResourceTypeProvider.Attributes.ContainerImage)
            ];
        }

        var replicas = int.TryParse(
            resource.Attributes.GetString(
                ContainerApplicationResourceTypeProvider.Attributes.ContainerReplicas),
            out var parsedReplicas)
                ? parsedReplicas
                : (int?)null;

        try
        {
            using var scope = scopeFactory.CreateScope();
            var resourceManager = scope.ServiceProvider.GetRequiredService<IResourceManager>();
            var result = await resourceManager.UpdateResourceImageAsync(
                RuntimeAppResourceId,
                image,
                restartIfRunning: false,
                triggeredBy: "resource-graph",
                requestedReplicas: replicas,
                cancellationToken);

            return ToDiagnostics(result, resource.EffectiveResourceId);
        }
        catch (Exception exception)
        {
            return
            [
                ResourceDefinitionDiagnostic.Error(
                    "containerAppDeployment.containerApp.runtimeImageUpdateFailed",
                    exception.Message,
                    resource.EffectiveResourceId)
            ];
        }
    }

    private static bool IsGraphApp(GraphResource resource) =>
        string.Equals(resource.EffectiveResourceId, GraphAppResourceId, StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<ResourceDefinitionDiagnostic> ToDiagnostics(
        ResourceProcedureResult result,
        string target)
    {
        if (result.Signals.Count == 0 &&
            !result.RestartRequired &&
            !result.RuntimeReconciliationRequired)
        {
            return [];
        }

        var diagnostics = new List<ResourceDefinitionDiagnostic>();
        foreach (var signal in result.Signals)
        {
            diagnostics.Add(ToDiagnostic(signal, target));
        }

        if (result.RestartRequired && !string.IsNullOrWhiteSpace(result.RestartMessage))
        {
            diagnostics.Add(ResourceDefinitionDiagnostic.Warning(
                "containerAppDeployment.containerApp.restartRequired",
                result.RestartMessage,
                target));
        }

        if (result.RuntimeReconciliationRequired &&
            !string.IsNullOrWhiteSpace(result.RuntimeReconciliationMessage))
        {
            diagnostics.Add(ResourceDefinitionDiagnostic.Warning(
                "containerAppDeployment.containerApp.runtimeReconciliationRequired",
                result.RuntimeReconciliationMessage,
                target));
        }

        return diagnostics;
    }

    private static ResourceDefinitionDiagnostic ToDiagnostic(
        ResourceProcedureSignal signal,
        string target) =>
        signal.Severity == ResourceSignalSeverity.Error
            ? ResourceDefinitionDiagnostic.Error(
                "containerAppDeployment.containerApp.runtimeSignal",
                signal.Message,
                target)
            : ResourceDefinitionDiagnostic.Warning(
                "containerAppDeployment.containerApp.runtimeSignal",
                signal.Message,
                target);
}
