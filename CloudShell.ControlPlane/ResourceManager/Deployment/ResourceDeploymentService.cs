using CloudShell.Abstractions.ControlPlane;
using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.ResourceManager.Orchestration;
using System.Collections.Concurrent;

namespace CloudShell.ControlPlane.ResourceManager.Deployment;

public sealed class ResourceDeploymentService(
    IEnumerable<IResourceOrchestrator> orchestrators,
    IEnumerable<IResourceOrchestratorDeploymentApplier> deploymentAppliers,
    IResourceManagerStore resourceManager,
    IResourceRegistrationStore registrations,
    ResourceOrchestratorSelectionStore selectionStore,
    IResourceEventSink? resourceEvents = null,
    IResourceOrchestratorDeploymentStore? deploymentStore = null)
{
    private readonly IReadOnlyList<IResourceOrchestrator> orchestrators = orchestrators.ToArray();
    private readonly IReadOnlyList<IResourceOrchestratorDeploymentApplier> deploymentAppliers =
        deploymentAppliers.ToArray();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> sourceResourceDeploymentLocks =
        new(StringComparer.OrdinalIgnoreCase);

    public Task<IReadOnlyList<ResourceDeploymentRecord>> ListResourceDeploymentsAsync(
        ResourceDeploymentQuery? query = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (deploymentStore is null)
        {
            return Task.FromResult<IReadOnlyList<ResourceDeploymentRecord>>([]);
        }

        var records = deploymentStore
            .List(new ResourceOrchestratorDeploymentQuery(
                SourceResourceId: Normalize(query?.SourceResourceId),
                DeploymentId: Normalize(query?.DeploymentId),
                OrchestratorId: Normalize(query?.OrchestratorId),
                MaxRecords: Math.Clamp(query?.MaxRecords ?? 200, 1, 1000)))
            .Select(ToResourceDeploymentRecord)
            .ToArray();
        return Task.FromResult<IReadOnlyList<ResourceDeploymentRecord>>(records);
    }

    public async Task<ResourceOrchestratorDeploymentApplyResult> ApplyDeploymentAsync(
        Resource resource,
        ResourceOrchestratorDeployment deployment,
        CancellationToken cancellationToken = default,
        string? triggeredBy = null,
        string? cause = null)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(deployment);

        if (!string.Equals(resource.Id, deployment.SourceResourceId, StringComparison.OrdinalIgnoreCase))
        {
            throw new ControlPlaneException(
                ControlPlaneError.InvalidRequest(
                    $"Deployment '{deployment.Id}' belongs to resource '{deployment.SourceResourceId}', not '{resource.Id}'."));
        }

        var sourceResourceLock = sourceResourceDeploymentLocks.GetOrAdd(
            resource.Id,
            _ => new SemaphoreSlim(1, 1));
        await sourceResourceLock.WaitAsync(cancellationToken);
        try
        {
            return await ApplyDeploymentCoreAsync(
                resource,
                deployment,
                cancellationToken,
                triggeredBy,
                cause);
        }
        finally
        {
            sourceResourceLock.Release();
        }
    }

    private async Task<ResourceOrchestratorDeploymentApplyResult> ApplyDeploymentCoreAsync(
        Resource resource,
        ResourceOrchestratorDeployment deployment,
        CancellationToken cancellationToken,
        string? triggeredBy,
        string? cause)
    {
        var context = CreateContext(resource, triggeredBy, cause);
        var preparedDeployment = PrepareDeploymentBase(deployment);
        var applier = SelectDeploymentApplier(context, preparedDeployment);
        var applyingDeployment = preparedDeployment with { Status = ResourceOrchestratorDeploymentStatus.Applying };
        deploymentStore?.RecordApplying(
            applyingDeployment,
            DateTimeOffset.UtcNow,
            triggeredBy,
            cause);
        AppendResourceActionEvent(
            resource,
            ResourceEventTypes.Events.Deployment.Applying,
            $"Applying deployment '{deployment.Id}' for revision '{deployment.RevisionId}'.{FormatCause(cause)}",
            triggeredBy);

        try
        {
            var result = await applier.ApplyDeploymentAsync(
                context,
                applyingDeployment,
                cancellationToken);
            result = NormalizeApplyResult(
                result,
                applyingDeployment,
                triggeredBy);
            deploymentStore?.RecordApplied(
                result.Deployment,
                result.Revision,
                DateTimeOffset.UtcNow,
                result.ProcedureResult.Message,
                triggeredBy,
                cause);
            AppendResourceActionEvent(
                resource,
                ResourceEventTypes.Events.Deployment.Applied,
                $"Applied deployment '{deployment.Id}' for revision '{result.Deployment.RevisionId}'. Result: {result.ProcedureResult.Message}",
                triggeredBy);
            return result;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            deploymentStore?.RecordFailed(
                applyingDeployment,
                DateTimeOffset.UtcNow,
                exception.Message,
                triggeredBy,
                cause);
            AppendResourceActionEvent(
                resource,
                ResourceEventTypes.Events.Deployment.Failed,
                $"Failed deployment '{deployment.Id}' for revision '{deployment.RevisionId}'. Reason: {exception.Message}",
                triggeredBy,
                ResourceSignalSeverity.Error);
            throw;
        }
    }

    private ResourceOrchestrationContext CreateContext(
        Resource resource,
        string? triggeredBy = null,
        string? cause = null)
    {
        var registration = GetRegistrationForResourceOrAncestor(resource);
        return new ResourceOrchestrationContext(
            resource,
            registration,
            resourceManager.GetGroupForResource(resource.Id),
            resourceManager,
            registrations,
            selectionStore.Get().PreferredContainerHostId,
            triggeredBy,
            cause,
            resourceEvents);
    }

    private ResourceOrchestratorDeployment PrepareDeploymentBase(
        ResourceOrchestratorDeployment deployment)
    {
        var explicitBasedOnRevisionId = Normalize(deployment.BasedOnRevisionId);
        if (explicitBasedOnRevisionId is not null)
        {
            return deployment with { BasedOnRevisionId = explicitBasedOnRevisionId };
        }

        var latestRevisionId = GetLatestSuccessfulRevisionId(deployment);
        return latestRevisionId is null
            ? deployment with { BasedOnRevisionId = null }
            : deployment with { BasedOnRevisionId = latestRevisionId };
    }

    private ResourceOrchestratorEnvironmentRevisionId? GetLatestSuccessfulRevisionId(
        ResourceOrchestratorDeployment deployment)
    {
        if (deploymentStore is null)
        {
            return null;
        }

        var records = deploymentStore.List(new ResourceOrchestratorDeploymentQuery(
            SourceResourceId: deployment.SourceResourceId,
            OrchestratorId: Normalize(deployment.OrchestratorId),
            MaxRecords: 1_000));
        var revision = records
            .Where(record =>
                string.Equals(record.ServiceId, deployment.ServiceId, StringComparison.OrdinalIgnoreCase) &&
                record.Status == ResourceOrchestratorDeploymentStatus.Active &&
                record.Revision is not null &&
                !record.Revision.Id.IsEmpty)
            .OrderByDescending(record => record.CompletedAt ?? record.StartedAt)
            .Select(record => record.Revision!.Id)
            .FirstOrDefault();
        return revision.IsEmpty ? null : revision;
    }

    private static ResourceOrchestratorDeploymentApplyResult NormalizeApplyResult(
        ResourceOrchestratorDeploymentApplyResult result,
        ResourceOrchestratorDeployment requestedDeployment,
        string? triggeredBy)
    {
        var basedOnRevisionId =
            Normalize(result.Deployment.BasedOnRevisionId) ??
            Normalize(requestedDeployment.BasedOnRevisionId);
        var provisionedBy =
            Normalize(result.Revision.ProvisionedBy) ??
            Normalize(triggeredBy);
        var deployment = result.Deployment with { BasedOnRevisionId = basedOnRevisionId };
        var revision = result.Revision with
        {
            BasedOnRevisionId = Normalize(result.Revision.BasedOnRevisionId) ?? basedOnRevisionId,
            ProvisionedBy = provisionedBy
        };
        return result with
        {
            Deployment = deployment,
            Revision = revision
        };
    }

    private IResourceOrchestratorDeploymentApplier SelectDeploymentApplier(
        ResourceOrchestrationContext context,
        ResourceOrchestratorDeployment deployment)
    {
        if (!string.IsNullOrWhiteSpace(deployment.OrchestratorId))
        {
            var explicitOrchestrator = orchestrators.FirstOrDefault(orchestrator =>
                string.Equals(orchestrator.Id, deployment.OrchestratorId, StringComparison.OrdinalIgnoreCase));
            if (explicitOrchestrator is null)
            {
                throw new ControlPlaneException(
                    ControlPlaneError.InvalidRequest(
                        $"Orchestrator '{deployment.OrchestratorId}' is not registered for deployment '{deployment.Id}'."));
            }

            if (explicitOrchestrator is IResourceOrchestratorDeploymentApplier explicitApplier &&
                explicitApplier.CanApplyDeployment(context, deployment))
            {
                return explicitApplier;
            }

            var defaultApplier = SelectStandaloneDeploymentApplier(context, deployment);
            if (defaultApplier is not null)
            {
                return defaultApplier;
            }

            throw new ControlPlaneException(
                ControlPlaneError.InvalidRequest(
                    $"Orchestrator '{deployment.OrchestratorId}' cannot apply deployment '{deployment.Id}' for resource '{context.Resource.Name}', and no default deployment service can apply it."));
        }

        return SelectPreferredDeploymentApplier(context, deployment)
            ?? throw new ControlPlaneException(
                ControlPlaneError.InvalidRequest(
                    $"No deployment service can apply deployment '{deployment.Id}' for resource '{context.Resource.Name}'."));
    }

    private IResourceOrchestratorDeploymentApplier? SelectPreferredDeploymentApplier(
        ResourceOrchestrationContext context,
        ResourceOrchestratorDeployment deployment)
    {
        var selectedId = selectionStore.Get().OrchestratorId;
        if (!string.Equals(selectedId, "default", StringComparison.OrdinalIgnoreCase))
        {
            var selected = orchestrators.FirstOrDefault(orchestrator =>
                string.Equals(orchestrator.Id, selectedId, StringComparison.OrdinalIgnoreCase) &&
                orchestrator is IResourceOrchestratorDeploymentApplier applier &&
                applier.CanApplyDeployment(context, deployment));
            if (selected is IResourceOrchestratorDeploymentApplier selectedApplier)
            {
                return selectedApplier;
            }
        }

        var defaultOrchestrator = orchestrators.FirstOrDefault(orchestrator =>
            string.Equals(orchestrator.Id, "default", StringComparison.OrdinalIgnoreCase) &&
            orchestrator is IResourceOrchestratorDeploymentApplier applier &&
            applier.CanApplyDeployment(context, deployment));
        if (defaultOrchestrator is IResourceOrchestratorDeploymentApplier defaultNativeApplier)
        {
            return defaultNativeApplier;
        }

        return SelectStandaloneDeploymentApplier(context, deployment);
    }

    private IResourceOrchestratorDeploymentApplier? SelectStandaloneDeploymentApplier(
        ResourceOrchestrationContext context,
        ResourceOrchestratorDeployment deployment) =>
        deploymentAppliers.FirstOrDefault(applier => applier.CanApplyDeployment(context, deployment));

    private static ResourceDeploymentRecord ToResourceDeploymentRecord(
        ResourceOrchestratorDeploymentRecord record)
    {
        var revision = record.Revision;
        return new ResourceDeploymentRecord(
            record.DeploymentId,
            record.OrchestratorId,
            record.SourceResourceId,
            record.ServiceId,
            record.RevisionId,
            record.Status,
            record.StartedAt,
            record.CompletedAt,
            record.TriggeredBy,
            record.Cause,
            record.Message,
            record.Error,
            revision?.Id.ToString(),
            revision?.RevisionNumber,
            revision?.CreatedAt,
            revision?.Status,
            revision?.BasedOnRevisionId?.ToString(),
            revision?.ProvisionedBy,
            record.ReplicaGroup,
            revision?.Definition ?? record.Deployment.Spec.CreateDeploymentDefinition(record.RevisionId));
    }

    private ResourceRegistration? GetRegistrationForResourceOrAncestor(Resource resource)
    {
        var current = resource;
        while (true)
        {
            var registration = registrations.GetRegistration(current.Id);
            if (registration is not null)
            {
                return registration;
            }

            if (string.IsNullOrWhiteSpace(current.ParentResourceId))
            {
                return null;
            }

            var parent = resourceManager.GetResource(current.ParentResourceId);
            if (parent is null)
            {
                return null;
            }

            current = parent;
        }
    }

    private void AppendResourceActionEvent(
        Resource resource,
        string eventType,
        string message,
        string? triggeredBy,
        ResourceSignalSeverity severity = ResourceSignalSeverity.Info) =>
        resourceEvents?.Append(new ResourceEvent(
            resource.Id,
            eventType,
            message,
            DateTimeOffset.UtcNow,
            triggeredBy,
            severity));

    private static string FormatCause(string? cause) =>
        string.IsNullOrWhiteSpace(cause)
            ? string.Empty
            : $" Cause: {cause.Trim().TrimEnd('.')}.";

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static ResourceOrchestratorEnvironmentRevisionId? Normalize(
        ResourceOrchestratorEnvironmentRevisionId? value) =>
        value is { IsEmpty: false } ? value : null;
}
