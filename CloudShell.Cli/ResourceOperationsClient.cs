using CloudShell.Abstractions.ControlPlane;
using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Cli;

internal static class ResourceOperationsClient
{
    public static async Task<IReadOnlyList<Resource>> ListAsync(
        Uri controlPlaneUrl,
        string? bearerToken,
        string? resourceType,
        ResourceClass? resourceClass,
        bool? isRegistered,
        CancellationToken cancellationToken)
    {
        using var client = ControlPlaneClientFactory.Create(controlPlaneUrl, bearerToken);
        return await client.ControlPlane.ListResourcesAsync(
            new ResourceQuery(
                ResourceType: resourceType,
                IsRegistered: isRegistered,
                ResourceClass: resourceClass),
            cancellationToken);
    }

    public static async Task<ResourceProcedureResult> ExecuteActionAsync(
        Uri controlPlaneUrl,
        string? bearerToken,
        ResourceActionExecuteCommand command,
        CancellationToken cancellationToken)
    {
        using var client = ControlPlaneClientFactory.Create(controlPlaneUrl, bearerToken);
        return await client.ControlPlane.ExecuteResourceActionAsync(
            new ExecuteResourceActionCommand(
                command.ResourceId,
                command.ActionId,
                command.StartDependencies,
                command.IgnoreDependentWarning,
                TriggeredBy: "CloudShell CLI"),
            cancellationToken);
    }
}
