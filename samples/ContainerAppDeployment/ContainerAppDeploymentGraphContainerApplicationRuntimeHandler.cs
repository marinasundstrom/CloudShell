using CloudShell.Abstractions.ControlPlane;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.Providers.Applications;
using CloudShell.ResourceDefinitions;
using CloudShell.ResourceDefinitions.ReferenceProviders;
using GraphResource = CloudShell.ResourceDefinitions.Resource;

internal sealed class ContainerAppDeploymentGraphContainerApplicationRuntimeHandler(
    IContainerAppDeploymentGraphContainerApplicationRuntimeBridge bridge) : IContainerApplicationRuntimeHandler
{
    private const string GraphAppResourceId = "application.container-app:graph-sample-api";

    public ContainerApplicationRuntimeStatus GetStatus(GraphResource resource) =>
        IsGraphApp(resource)
            ? bridge.GetStatus(resource)
            : ContainerApplicationRuntimeStatus.Unknown;

    public async ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ExecuteLifecycleAsync(
        GraphResource resource,
        ResourceOperationId operationId,
        CancellationToken cancellationToken = default)
    {
        if (!IsGraphApp(resource))
        {
            return [];
        }

        return await bridge.ExecuteLifecycleAsync(resource, operationId, cancellationToken);
    }

    public async ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ApplyImageAsync(
        GraphResource resource,
        CancellationToken cancellationToken = default)
    {
        if (!IsGraphApp(resource))
        {
            return [];
        }

        return await bridge.ApplyImageAsync(resource, cancellationToken);
    }

    public async ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ApplyReplicasAsync(
        GraphResource resource,
        CancellationToken cancellationToken = default)
    {
        if (!IsGraphApp(resource))
        {
            return [];
        }

        return await bridge.ApplyReplicasAsync(resource, cancellationToken);
    }

    private static bool IsGraphApp(GraphResource resource) =>
        string.Equals(resource.EffectiveResourceId, GraphAppResourceId, StringComparison.OrdinalIgnoreCase);
}

internal sealed class ContainerAppDeploymentGraphResourceManagerContainerApplicationBridge(
    IServiceScopeFactory scopeFactory) : IContainerAppDeploymentGraphContainerApplicationRuntimeBridge
{
    private const string RuntimeAppResourceId = "application:sample-api";

    public ContainerApplicationRuntimeStatus GetStatus(GraphResource resource)
    {
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

    public async ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ApplyReplicasAsync(
        GraphResource resource,
        CancellationToken cancellationToken = default)
    {
        if (!int.TryParse(
                resource.Attributes.GetString(
                    ContainerApplicationResourceTypeProvider.Attributes.ContainerReplicas),
                out var replicas))
        {
            return
            [
                ResourceDefinitionDiagnostic.Error(
                    "containerAppDeployment.containerApp.replicasRequired",
                    "The graph container app replicas attribute must be set before the sample runtime replicas can be updated.",
                    ContainerApplicationResourceTypeProvider.Attributes.ContainerReplicas)
            ];
        }

        try
        {
            using var scope = scopeFactory.CreateScope();
            var resourceManager = scope.ServiceProvider.GetRequiredService<IResourceManager>();
            var result = await resourceManager.UpdateResourceReplicasAsync(
                RuntimeAppResourceId,
                replicas,
                restartIfRunning: false,
                triggeredBy: "resource-graph",
                cancellationToken);

            return ToDiagnostics(result, resource.EffectiveResourceId);
        }
        catch (Exception exception)
        {
            return
            [
                ResourceDefinitionDiagnostic.Error(
                    "containerAppDeployment.containerApp.runtimeReplicasUpdateFailed",
                    exception.Message,
                    resource.EffectiveResourceId)
            ];
        }
    }

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

internal sealed class ContainerAppDeploymentGraphOnlyContainerApplicationRuntimeBridge :
    IContainerAppDeploymentGraphContainerApplicationRuntimeBridge
{
    public ContainerApplicationRuntimeStatus GetStatus(GraphResource resource) =>
        ContainerApplicationRuntimeStatus.Stopped;

    public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ExecuteLifecycleAsync(
        GraphResource resource,
        ResourceOperationId operationId,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>(
        [
            new(
                ResourceDefinitionDiagnosticSeverity.Information,
                "containerAppDeployment.containerApp.graphOnlyRuntimeDeferred",
                $"Graph-only container app operation '{operationId}' was accepted without runtime materialization.",
                resource.EffectiveResourceId)
        ]);

    public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ApplyImageAsync(
        GraphResource resource,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>(
        [
            new(
                ResourceDefinitionDiagnosticSeverity.Information,
                "containerAppDeployment.containerApp.graphOnlyImageAccepted",
                "Graph-only container app image state was accepted without updating the old application provider runtime.",
                resource.EffectiveResourceId)
        ]);

    public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ApplyReplicasAsync(
        GraphResource resource,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>(
        [
            new(
                ResourceDefinitionDiagnosticSeverity.Information,
                "containerAppDeployment.containerApp.graphOnlyReplicasAccepted",
                "Graph-only container app replica state was accepted without updating the old application provider runtime.",
                resource.EffectiveResourceId)
        ]);
}
