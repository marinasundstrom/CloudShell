using CloudShell.Abstractions.ControlPlane;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.Providers.Applications;
using CloudShell.ResourceDefinitions;
using CloudShell.ResourceDefinitions.ReferenceProviders;
using GraphResource = CloudShell.ResourceDefinitions.Resource;

internal sealed class ReplicatedContainerHealthGraphRuntimeHandler(
    IReplicatedContainerHealthGraphContainerAppRuntimeBridge bridge) : IContainerApplicationRuntimeHandler
{
    private const string GraphApiResourceId = "application.container-app:graph-api";

    public ContainerApplicationRuntimeStatus GetStatus(GraphResource resource) =>
        IsGraphApi(resource)
            ? bridge.GetStatus(resource)
            : ContainerApplicationRuntimeStatus.Unknown;

    public async ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ExecuteLifecycleAsync(
        GraphResource resource,
        ResourceOperationId operationId,
        CancellationToken cancellationToken = default)
    {
        if (!IsGraphApi(resource))
        {
            return [];
        }

        return await bridge.ExecuteLifecycleAsync(resource, operationId, cancellationToken);
    }

    public async ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ApplyImageAsync(
        GraphResource resource,
        CancellationToken cancellationToken = default)
    {
        if (!IsGraphApi(resource))
        {
            return [];
        }

        return await bridge.ApplyImageAsync(resource, cancellationToken);
    }

    public async ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ApplyReplicasAsync(
        GraphResource resource,
        CancellationToken cancellationToken = default)
    {
        if (!IsGraphApi(resource))
        {
            return [];
        }

        return await bridge.ApplyReplicasAsync(resource, cancellationToken);
    }

    private static bool IsGraphApi(GraphResource resource) =>
        string.Equals(resource.EffectiveResourceId, GraphApiResourceId, StringComparison.OrdinalIgnoreCase);
}

internal sealed class ReplicatedContainerHealthGraphResourceManagerBridge(
    IServiceScopeFactory scopeFactory) : IReplicatedContainerHealthGraphContainerAppRuntimeBridge
{
    private const string RuntimeApiResourceId = "application:api";

    public ContainerApplicationRuntimeStatus GetStatus(GraphResource resource)
    {
        using var scope = scopeFactory.CreateScope();
        var runtimeState = scope.ServiceProvider
            .GetRequiredService<IApplicationResourceRunningStateOperations>();
        return runtimeState.IsRunning(RuntimeApiResourceId)
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
                    RuntimeApiResourceId,
                    startDependencies: true,
                    cancellationToken),
                ResourceActionIds.Stop => await resourceManager.StopResourceAsync(
                    RuntimeApiResourceId,
                    ignoreDependentWarning: true,
                    cancellationToken),
                ResourceActionIds.Restart => await resourceManager.RestartResourceAsync(
                    RuntimeApiResourceId,
                    startDependencies: true,
                    ignoreDependentWarning: true,
                    cancellationToken),
                _ => throw new NotSupportedException(
                    $"The ReplicatedContainerHealth sample does not map graph operation '{operationId}' to the runtime app.")
            };

            return ToDiagnostics(result, resource.EffectiveResourceId);
        }
        catch (Exception exception)
        {
            return
            [
                ResourceDefinitionDiagnostic.Error(
                    "replicatedContainerHealth.runtimeLifecycleFailed",
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
                    "replicatedContainerHealth.containerImageRequired",
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
                RuntimeApiResourceId,
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
                    "replicatedContainerHealth.runtimeImageUpdateFailed",
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
                    "replicatedContainerHealth.containerReplicasRequired",
                    "The graph container app replicas attribute must be set before the sample runtime replicas can be updated.",
                    ContainerApplicationResourceTypeProvider.Attributes.ContainerReplicas)
            ];
        }

        try
        {
            using var scope = scopeFactory.CreateScope();
            var resourceManager = scope.ServiceProvider.GetRequiredService<IResourceManager>();
            var result = await resourceManager.UpdateResourceReplicasAsync(
                RuntimeApiResourceId,
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
                    "replicatedContainerHealth.runtimeReplicasUpdateFailed",
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
                "replicatedContainerHealth.restartRequired",
                result.RestartMessage,
                target));
        }

        if (result.RuntimeReconciliationRequired &&
            !string.IsNullOrWhiteSpace(result.RuntimeReconciliationMessage))
        {
            diagnostics.Add(ResourceDefinitionDiagnostic.Warning(
                "replicatedContainerHealth.runtimeReconciliationRequired",
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
                "replicatedContainerHealth.runtimeSignal",
                signal.Message,
                target)
            : ResourceDefinitionDiagnostic.Warning(
                "replicatedContainerHealth.runtimeSignal",
                signal.Message,
                target);
}

internal sealed class ReplicatedContainerHealthGraphOnlyContainerAppRuntimeBridge :
    IReplicatedContainerHealthGraphContainerAppRuntimeBridge
{
    public ContainerApplicationRuntimeStatus GetStatus(GraphResource resource) =>
        ContainerApplicationRuntimeStatus.Unknown;

    public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ExecuteLifecycleAsync(
        GraphResource resource,
        ResourceOperationId operationId,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>(
        [
            RuntimeNotPorted(resource)
        ]);

    public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ApplyImageAsync(
        GraphResource resource,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>(
        [
            RuntimeNotPorted(resource)
        ]);

    public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ApplyReplicasAsync(
        GraphResource resource,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>(
        [
            RuntimeNotPorted(resource)
        ]);

    private static ResourceDefinitionDiagnostic RuntimeNotPorted(GraphResource resource) =>
        ResourceDefinitionDiagnostic.Warning(
            "replicatedContainerHealth.graphOnlyRuntimeDeferred",
            "ReplicatedContainerHealth graph-only mode declares the container app through the Resource Graph, but graph-only container runtime materialization is not ported yet.",
            resource.EffectiveResourceId);
}
