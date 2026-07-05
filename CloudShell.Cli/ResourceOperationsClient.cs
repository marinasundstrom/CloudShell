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
        using var client = await ControlPlaneClientFactory.CreateAsync(
            controlPlaneUrl,
            bearerToken,
            cancellationToken);
        return await client.ControlPlane.ListResourcesAsync(
            new ResourceQuery(
                ResourceType: resourceType,
                IsRegistered: isRegistered,
                ResourceClass: resourceClass),
            cancellationToken);
    }

    public static async Task<Resource?> GetAsync(
        Uri controlPlaneUrl,
        string? bearerToken,
        string resourceId,
        CancellationToken cancellationToken)
    {
        using var client = await ControlPlaneClientFactory.CreateAsync(
            controlPlaneUrl,
            bearerToken,
            cancellationToken);
        return await client.ControlPlane.GetResourceAsync(resourceId, cancellationToken);
    }

    public static async Task<ResourceProcedureResult> ExecuteActionAsync(
        Uri controlPlaneUrl,
        string? bearerToken,
        ResourceActionExecuteCommand command,
        CancellationToken cancellationToken)
    {
        using var client = await ControlPlaneClientFactory.CreateAsync(
            controlPlaneUrl,
            bearerToken,
            cancellationToken);
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
