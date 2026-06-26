using CloudShell.Abstractions.ControlPlane;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ResourceDefinitions;
using CloudShell.ResourceDefinitions.ReferenceProviders;
using GraphResource = CloudShell.ResourceDefinitions.Resource;

namespace CloudShell.ApplicationTopologyHost;

internal sealed class ApplicationTopologyGraphSqlServerRuntimeHandler(
    IServiceScopeFactory scopeFactory) : ISqlServerRuntimeHandler
{
    private const string GraphSqlServerResourceId =
        "application.sql-server:graph-application-topology-sql-server";

    private const string RuntimeSqlServerResourceId =
        "application:application-topology-sql-server";

    // Status projection runs while Resource Manager is composing resources, so
    // this adapter cannot query Resource Manager without recursing.
    public SqlServerRuntimeStatus GetStatus(GraphResource resource)
        => SqlServerRuntimeStatus.Unknown;

    public async ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ExecuteLifecycleAsync(
        GraphResource resource,
        ResourceOperationId operationId,
        CancellationToken cancellationToken = default)
    {
        if (!IsGraphSqlServer(resource))
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
                    RuntimeSqlServerResourceId,
                    startDependencies: true,
                    cancellationToken),
                ResourceActionIds.Stop => await resourceManager.StopResourceAsync(
                    RuntimeSqlServerResourceId,
                    ignoreDependentWarning: true,
                    cancellationToken),
                ResourceActionIds.Restart => await resourceManager.RestartResourceAsync(
                    RuntimeSqlServerResourceId,
                    startDependencies: true,
                    ignoreDependentWarning: true,
                    cancellationToken),
                _ => throw new NotSupportedException(
                    $"The ApplicationTopology sample does not map graph SQL operation '{operationId}' to runtime SQL.")
            };

            return ToDiagnostics(result, resource.EffectiveResourceId);
        }
        catch (Exception exception)
        {
            return
            [
                ResourceDefinitionDiagnostic.Error(
                    "applicationTopology.sqlServer.runtimeLifecycleFailed",
                    exception.Message,
                    resource.EffectiveResourceId)
            ];
        }
    }

    private static bool IsGraphSqlServer(GraphResource resource) =>
        string.Equals(
            resource.EffectiveResourceId,
            GraphSqlServerResourceId,
            StringComparison.OrdinalIgnoreCase);

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
                "applicationTopology.sqlServer.restartRequired",
                result.RestartMessage,
                target));
        }

        if (result.RuntimeReconciliationRequired &&
            !string.IsNullOrWhiteSpace(result.RuntimeReconciliationMessage))
        {
            diagnostics.Add(ResourceDefinitionDiagnostic.Warning(
                "applicationTopology.sqlServer.runtimeReconciliationRequired",
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
                "applicationTopology.sqlServer.runtimeSignal",
                signal.Message,
                target)
            : ResourceDefinitionDiagnostic.Warning(
                "applicationTopology.sqlServer.runtimeSignal",
                signal.Message,
                target);
}
