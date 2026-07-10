using Microsoft.Extensions.Options;

namespace CloudShell.ControlPlane.Providers;

public sealed class DeferredContainerApplicationRuntimeOptions
{
    private readonly HashSet<string> resourceIds = new(StringComparer.OrdinalIgnoreCase);

    public ISet<string> ResourceIds => resourceIds;

    public DeferredContainerApplicationRuntimeOptions AddResource(string resourceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);

        resourceIds.Add(resourceId);
        return this;
    }
}

public interface IDeferredContainerApplicationRuntimeSelector
{
    bool IsDeferredRuntimeResource(Resource resource);

    bool IsDeferredRuntimeResourceId(string resourceId);
}

public sealed class DeferredContainerApplicationRuntimeHandler(
    IOptions<DeferredContainerApplicationRuntimeOptions> options) : IContainerApplicationRuntimeHandler
{
    private const string RuntimeDeferredDiagnosticCode =
        "application.container.deferredRuntime";
    private const string ImageAcceptedDiagnosticCode =
        "application.container.deferredRuntimeImageAccepted";
    private const string ReplicasAcceptedDiagnosticCode =
        "application.container.deferredRuntimeReplicasAccepted";
    private readonly DeferredContainerApplicationRuntimeOptions options = options.Value;

    public ContainerApplicationRuntimeStatus GetStatus(Resource resource) =>
        ContainerApplicationRuntimeStatus.Unknown;

    public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ExecuteLifecycleAsync(
        Resource resource,
        ResourceOperationId operationId,
        CancellationToken cancellationToken = default) =>
        CanHandle(resource)
            ? WarningAsync(
                RuntimeDeferredDiagnosticCode,
                $"Container app lifecycle operation '{operationId}' was not materialized because this host uses the deferred container app runtime bridge.",
                resource)
            : EmptyAsync();

    public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ApplyImageAsync(
        Resource resource,
        CancellationToken cancellationToken = default) =>
        CanHandle(resource)
            ? AcceptedAsync(
                ImageAcceptedDiagnosticCode,
                "Container app image state was accepted without runtime materialization.",
                resource)
            : EmptyAsync();

    public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ApplyReplicasAsync(
        Resource resource,
        CancellationToken cancellationToken = default) =>
        CanHandle(resource)
            ? AcceptedAsync(
                ReplicasAcceptedDiagnosticCode,
                "Container app replica state was accepted without runtime materialization.",
                resource)
            : EmptyAsync();

    private bool CanHandle(Resource resource) =>
        options.ResourceIds.Contains(resource.EffectiveResourceId);

    private static ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> AcceptedAsync(
        string code,
        string message,
        Resource resource) =>
        ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>(
        [
            new(
                ResourceDefinitionDiagnosticSeverity.Information,
                code,
                message,
                resource.EffectiveResourceId)
        ]);

    private static ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> WarningAsync(
        string code,
        string message,
        Resource resource) =>
        ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>(
        [
            new(
                ResourceDefinitionDiagnosticSeverity.Warning,
                code,
                message,
                resource.EffectiveResourceId)
        ]);

    private static ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> EmptyAsync() =>
        ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>([]);
}

public sealed class DeferredContainerApplicationRuntimeSelector(
    IOptions<DeferredContainerApplicationRuntimeOptions> options) :
    IDeferredContainerApplicationRuntimeSelector
{
    private readonly DeferredContainerApplicationRuntimeOptions options = options.Value;

    public bool IsDeferredRuntimeResource(Resource resource) =>
        resource.Type.TypeId == ContainerApplicationResourceTypeProvider.ResourceTypeId &&
        options.ResourceIds.Contains(resource.EffectiveResourceId);

    public bool IsDeferredRuntimeResourceId(string resourceId) =>
        options.ResourceIds.Contains(resourceId);
}
