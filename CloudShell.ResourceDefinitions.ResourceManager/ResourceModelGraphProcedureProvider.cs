using CloudShell.Abstractions.ResourceManager;
using ResourceManagerResource = CloudShell.Abstractions.ResourceManager.Resource;

namespace CloudShell.ResourceDefinitions.ResourceManager;

public sealed class ResourceModelGraphProcedureProvider :
    IResourceProvider,
    IResourceModelDiagnosticProvider,
    IResourceProcedureProvider,
    IResourceActionAvailabilityProvider,
    IResourceImageUpdateProvider,
    IResourceReplicaUpdateProvider
{
    private static readonly ResourceOperationId ContainerImageUpdateOperationId = "container.image.update";
    private static readonly ResourceOperationId ContainerReplicasUpdateOperationId = "container.replicas.update";
    private static readonly ResourceAttributeId ContainerImageAttributeId = "container.image";
    private static readonly ResourceAttributeId ContainerReplicasAttributeId = "container.replicas";

    private readonly ResourceModelGraphResourceProvider _resourceProvider;
    private readonly ResourceModelGraphResourceResolver _resourceResolver;
    private readonly ResourceModelGraphDefinitionApplyService _definitionApply;
    private readonly ResourceDefinitionResolutionContext _resolutionContext;

    public ResourceModelGraphProcedureProvider(
        ResourceModelGraphResourceProvider resourceProvider,
        ResourceModelGraphResourceResolver resourceResolver,
        ResourceModelGraphDefinitionApplyService definitionApply,
        ResourceDefinitionResolutionContext? resolutionContext = null)
    {
        _resourceProvider = resourceProvider ?? throw new ArgumentNullException(nameof(resourceProvider));
        _resourceResolver = resourceResolver ?? throw new ArgumentNullException(nameof(resourceResolver));
        _definitionApply = definitionApply ?? throw new ArgumentNullException(nameof(definitionApply));
        _resolutionContext = resolutionContext ?? ResourceDefinitionResolutionContext.Empty;
    }

    public string Id => _resourceProvider.Id;

    public string DisplayName => _resourceProvider.DisplayName;

    public IReadOnlyList<ResourceManagerResource> GetResources() =>
        _resourceProvider.GetResources();

    public IReadOnlyList<ResourceModelDiagnostic> GetResourceModelDiagnostics() =>
        _resourceProvider.GetResourceModelDiagnostics();

    public Task<ResourceProcedureResult> DeleteAsync(
        ResourceProcedureContext context,
        CancellationToken cancellationToken = default) =>
        Task.FromException<ResourceProcedureResult>(
            new NotSupportedException("Resource model graph resources are not deleted through this bridge provider."));

    public bool CanUpdateImage(ResourceManagerResource resource) =>
        IsBridgeResource(resource) &&
        resource.HasAction(ContainerImageUpdateOperationId.ToString());

    public async Task<ResourceProcedureResult> UpdateImageAsync(
        ResourceProcedureContext context,
        string image,
        bool restartIfRunning,
        string? triggeredBy = null,
        CancellationToken cancellationToken = default,
        int? requestedReplicas = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!CanUpdateImage(context.Resource))
        {
            throw new NotSupportedException(
                $"Resource model graph resource '{context.Resource.Id}' does not support image updates.");
        }

        var attributes = new Dictionary<ResourceAttributeId, ResourceAttributeValue>
        {
            [ContainerImageAttributeId] = image
        };
        if (requestedReplicas is not null)
        {
            attributes[ContainerReplicasAttributeId] = requestedReplicas.Value;
        }

        await ApplyContainerUpdateAttributesAsync(
            context,
            attributes,
            ContainerImageUpdateOperationId,
            triggeredBy,
            cancellationToken);

        return ResourceProcedureResult.Completed(
            $"Updated image for {context.Resource.Name} to '{image}'.");
    }

    public bool CanUpdateReplicas(ResourceManagerResource resource) =>
        IsBridgeResource(resource) &&
        resource.HasAction(ContainerReplicasUpdateOperationId.ToString());

    public async Task<ResourceProcedureResult> UpdateReplicasAsync(
        ResourceProcedureContext context,
        int replicas,
        bool restartIfRunning,
        string? triggeredBy = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (replicas < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(replicas),
                replicas,
                "Replicas must be greater than or equal to 1.");
        }

        if (!CanUpdateReplicas(context.Resource))
        {
            throw new NotSupportedException(
                $"Resource model graph resource '{context.Resource.Id}' does not support replica updates.");
        }

        await ApplyContainerUpdateAttributesAsync(
            context,
            new Dictionary<ResourceAttributeId, ResourceAttributeValue>
            {
                [ContainerReplicasAttributeId] = replicas
            },
            ContainerReplicasUpdateOperationId,
            triggeredBy,
            cancellationToken);

        return ResourceProcedureResult.Completed(
            $"Updated replicas for {context.Resource.Name} to '{replicas}'.");
    }

    public bool CanEvaluateAction(
        ResourceManagerResource resource,
        ResourceAction action) =>
        IsBridgeResource(resource) &&
        resource.HasAction(action.Id);

    public async Task<string?> GetActionUnavailableReasonAsync(
        ResourceProcedureContext context,
        ResourceAction action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(action);

        var resolution = await ResolveExecutableOperationAsync(
            context.Resource.Id,
            action,
            cancellationToken);
        var blockingGraphDiagnostics = await ResolveBlockingGraphDiagnosticsAsync(
            context.Resource.Id,
            cancellationToken);

        if (blockingGraphDiagnostics.Count > 0)
        {
            return FormatDiagnostics(blockingGraphDiagnostics);
        }

        if (resolution.Diagnostics.Count > 0)
        {
            return FormatDiagnostics(resolution.Diagnostics);
        }

        if (resolution.Operation is not IResourceOperationExecutorProjection executableOperation)
        {
            return $"Resource model operation '{resolution.OperationId}' does not support execution.";
        }

        return await executableOperation.CanExecuteAsync(cancellationToken)
            ? null
            : $"Resource model operation '{resolution.OperationId}' cannot execute.";
    }

    public async Task<ResourceProcedureResult> ExecuteActionAsync(
        ResourceProcedureContext context,
        ResourceAction action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(action);

        var resolution = await ResolveExecutableOperationAsync(
            context.Resource.Id,
            action,
            cancellationToken);
        var blockingGraphDiagnostics = await ResolveBlockingGraphDiagnosticsAsync(
            context.Resource.Id,
            cancellationToken);

        if (blockingGraphDiagnostics.Count > 0)
        {
            throw new InvalidOperationException(FormatDiagnostics(blockingGraphDiagnostics));
        }

        if (resolution.Diagnostics.Count > 0)
        {
            throw new InvalidOperationException(FormatDiagnostics(resolution.Diagnostics));
        }

        if (resolution.Operation is not IResourceOperationExecutorProjection executableOperation)
        {
            throw new NotSupportedException(
                $"Resource model operation '{resolution.OperationId}' does not support execution.");
        }

        if (!await executableOperation.CanExecuteAsync(cancellationToken))
        {
            throw new InvalidOperationException(
                $"Resource model operation '{resolution.OperationId}' cannot execute.");
        }

        var execution = await executableOperation.ExecuteAsync(cancellationToken);
        if (execution.HasErrors)
        {
            throw new InvalidOperationException(FormatDiagnostics(execution.Diagnostics));
        }

        return ResourceProcedureResult.Completed(
            CreateCompletedMessage(action, context.Resource, execution.Diagnostics));
    }

    private async Task ApplyContainerUpdateAttributesAsync(
        ResourceProcedureContext context,
        IReadOnlyDictionary<ResourceAttributeId, ResourceAttributeValue> attributes,
        ResourceOperationId operationId,
        string? triggeredBy,
        CancellationToken cancellationToken)
    {
        var current = await _resourceResolver.ResolveAsync(
            context.Resource.Id,
            _resolutionContext,
            cancellationToken);
        if (current.Target is null)
        {
            throw new InvalidOperationException(
                $"Resource model graph resource '{context.Resource.Id}' could not be resolved.");
        }

        if (current.HasErrors)
        {
            throw new InvalidOperationException(FormatDiagnostics(current.Diagnostics));
        }

        var definition = new ResourceDefinition(
            current.Target.Name,
            current.Target.Type.TypeId,
            ResourceId: current.Target.EffectiveResourceId,
            Attributes: new ResourceAttributeValueMap(attributes));
        var apply = await _definitionApply.ApplyDefinitionsAsync(
            [definition],
            new ResourceGraphCommitContext(
                EnvironmentId: _resolutionContext.EnvironmentId,
                PrincipalId: string.IsNullOrWhiteSpace(triggeredBy)
                    ? _resolutionContext.PrincipalId
                    : triggeredBy),
            ResourceModelGraphDefinitionApplyOptions.Default.WithoutRuntimeReconciliation(),
            cancellationToken);
        if (apply.HasErrors || !apply.IsCommitted)
        {
            throw new InvalidOperationException(FormatDiagnostics(apply.Diagnostics));
        }

        var operation = await ResolveExecutableOperationAsync(
            context.Resource.Id,
            operationId,
            cancellationToken);
        if (operation.Diagnostics.Count > 0)
        {
            throw new InvalidOperationException(FormatDiagnostics(operation.Diagnostics));
        }

        if (operation.Operation is not IResourceOperationExecutorProjection executableOperation)
        {
            throw new NotSupportedException(
                $"Resource model operation '{operation.OperationId}' does not support execution.");
        }

        var execution = await executableOperation.ExecuteAsync(cancellationToken);
        if (execution.HasErrors)
        {
            throw new InvalidOperationException(FormatDiagnostics(execution.Diagnostics));
        }
    }

    private async ValueTask<ResourceModelGraphOperationResolution> ResolveExecutableOperationAsync(
        string resourceId,
        ResourceAction action,
        CancellationToken cancellationToken) =>
        await _resourceResolver.ResolveOperationAsync(
            resourceId,
            action,
            _resolutionContext,
            cancellationToken);

    private async ValueTask<ResourceModelGraphOperationResolution> ResolveExecutableOperationAsync(
        string resourceId,
        ResourceOperationId operationId,
        CancellationToken cancellationToken) =>
        await _resourceResolver.ResolveOperationAsync(
            resourceId,
            operationId,
            _resolutionContext,
            cancellationToken);

    private async ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ResolveBlockingGraphDiagnosticsAsync(
        string resourceId,
        CancellationToken cancellationToken)
    {
        var resolution = await _resourceResolver.ResolveWithDependenciesAsync(
            resourceId,
            _resolutionContext,
            cancellationToken);

        return resolution.Diagnostics
            .Where(diagnostic =>
                diagnostic.Code == ResourceDefinitionDiagnosticCodes.ResourceReferenceTypeMismatch)
            .ToArray();
    }

    private static string FormatDiagnostics(
        IReadOnlyList<ResourceDefinitionDiagnostic> diagnostics) =>
        string.Join(
            " ",
            diagnostics.Select(diagnostic =>
                string.IsNullOrWhiteSpace(diagnostic.Target)
                    ? diagnostic.Message
                    : $"{diagnostic.Message} Target: {diagnostic.Target}."));

    private static string CreateCompletedMessage(
        ResourceAction action,
        ResourceManagerResource resource,
        IReadOnlyList<ResourceDefinitionDiagnostic> diagnostics)
    {
        var message = $"Executed {action.DisplayName} for {resource.Name}.";
        var diagnosticMessages = diagnostics
            .Where(diagnostic =>
                (diagnostic.Severity is
                    ResourceDefinitionDiagnosticSeverity.Information or
                    ResourceDefinitionDiagnosticSeverity.Warning) &&
                !string.IsNullOrWhiteSpace(diagnostic.Message))
            .Select(diagnostic => diagnostic.Message.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return diagnosticMessages.Length == 0
            ? message
            : $"{message} {string.Join(" ", diagnosticMessages)}";
    }

    private bool IsBridgeResource(ResourceManagerResource resource) =>
        resource.IsDeclaredResource &&
        resource.ResourceAttributes.TryGetValue(
            ResourceModelResourceManagerAttributeNames.BridgeProviderId,
            out var bridgeProviderId) &&
        string.Equals(bridgeProviderId, Id, StringComparison.OrdinalIgnoreCase);
}
