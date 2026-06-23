using CloudShell.Abstractions.ResourceManager;
using System.Collections.Concurrent;
using System.Globalization;

namespace CloudShell.ControlPlane.ResourceManager.Deployment;

public interface IResourceOrchestratorDeploymentStore
{
    ResourceOrchestratorRevision CreateRevision(
        ResourceOrchestratorDeployment deployment,
        DateTimeOffset createdAt,
        ResourceOrchestratorRevisionStatus status,
        ResourceOrchestratorReplicaGroup? replicaGroup = null,
        string? provisionedBy = null);

    IReadOnlyList<ResourceOrchestratorDeploymentRecord> List(ResourceOrchestratorDeploymentQuery? query = null);

    void RecordApplying(
        ResourceOrchestratorDeployment deployment,
        DateTimeOffset startedAt,
        string? triggeredBy = null,
        string? cause = null);

    void RecordApplied(
        ResourceOrchestratorDeployment deployment,
        ResourceOrchestratorRevision revision,
        DateTimeOffset completedAt,
        string message,
        string? triggeredBy = null,
        string? cause = null);

    void RecordFailed(
        ResourceOrchestratorDeployment deployment,
        DateTimeOffset completedAt,
        string error,
        string? triggeredBy = null,
        string? cause = null);
}

public sealed record ResourceOrchestratorDeploymentQuery(
    string? SourceResourceId = null,
    string? DeploymentId = null,
    string? OrchestratorId = null,
    int MaxRecords = 200);

public sealed record ResourceOrchestratorDeploymentRecord(
    string DeploymentId,
    string OrchestratorId,
    string SourceResourceId,
    string ServiceId,
    string RevisionId,
    ResourceOrchestratorDeployment Deployment,
    ResourceOrchestratorRevision? Revision,
    ResourceOrchestratorDeploymentStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt = null,
    string? TriggeredBy = null,
    string? Cause = null,
    string? Message = null,
    string? Error = null,
    ResourceOrchestratorReplicaGroup? ReplicaGroup = null);

public sealed class InMemoryResourceOrchestratorDeploymentStore : IResourceOrchestratorDeploymentStore
{
    private readonly ConcurrentDictionary<string, ResourceOrchestratorDeploymentRecord> records =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> pendingRecordKeys =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, int> revisionNumbers =
        new(StringComparer.OrdinalIgnoreCase);

    public ResourceOrchestratorRevision CreateRevision(
        ResourceOrchestratorDeployment deployment,
        DateTimeOffset createdAt,
        ResourceOrchestratorRevisionStatus status,
        ResourceOrchestratorReplicaGroup? replicaGroup = null,
        string? provisionedBy = null)
    {
        ArgumentNullException.ThrowIfNull(deployment);

        var revisionNumber = revisionNumbers.AddOrUpdate(
            CreateRevisionNumberKey(deployment.SourceResourceId, deployment.ServiceId),
            1,
            (_, current) => current + 1);
        return new ResourceOrchestratorRevision(
            ResourceOrchestratorEnvironmentRevisionId.FromScope(
                deployment.SourceResourceId,
                deployment.ServiceId,
                revisionNumber),
            deployment.Id,
            deployment.SourceResourceId,
            deployment.ServiceId,
            revisionNumber,
            createdAt,
            status,
            replicaGroup,
            Normalize(deployment.BasedOnRevisionId),
            Normalize(provisionedBy),
            deployment.Spec.CreateDeploymentDefinition(deployment.RevisionId));
    }

    public IReadOnlyList<ResourceOrchestratorDeploymentRecord> List(ResourceOrchestratorDeploymentQuery? query = null)
    {
        query ??= new ResourceOrchestratorDeploymentQuery();
        var maxRecords = Math.Clamp(query.MaxRecords, 1, 1_000);
        IEnumerable<ResourceOrchestratorDeploymentRecord> matches = records.Values;

        if (!string.IsNullOrWhiteSpace(query.SourceResourceId))
        {
            matches = matches.Where(record =>
                string.Equals(record.SourceResourceId, query.SourceResourceId, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(query.DeploymentId))
        {
            matches = matches.Where(record =>
                string.Equals(record.DeploymentId, query.DeploymentId, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(query.OrchestratorId))
        {
            matches = matches.Where(record =>
                string.Equals(record.OrchestratorId, query.OrchestratorId, StringComparison.OrdinalIgnoreCase));
        }

        return matches
            .OrderByDescending(record => record.StartedAt)
            .Take(maxRecords)
            .ToArray();
    }

    public void RecordApplying(
        ResourceOrchestratorDeployment deployment,
        DateTimeOffset startedAt,
        string? triggeredBy = null,
        string? cause = null)
    {
        ArgumentNullException.ThrowIfNull(deployment);

        var baseKey = CreateRecordBaseKey(deployment.SourceResourceId, deployment.Id, deployment.RevisionId);
        var recordKey = CreateRecordKey(baseKey, startedAt);
        pendingRecordKeys[baseKey] = recordKey;
        records[recordKey] =
            new ResourceOrchestratorDeploymentRecord(
                deployment.Id,
                deployment.OrchestratorId,
                deployment.SourceResourceId,
                deployment.ServiceId,
                deployment.RevisionId,
                deployment,
                Revision: null,
                ResourceOrchestratorDeploymentStatus.Applying,
                startedAt,
                TriggeredBy: Normalize(triggeredBy),
                Cause: Normalize(cause));
    }

    public void RecordApplied(
        ResourceOrchestratorDeployment deployment,
        ResourceOrchestratorRevision revision,
        DateTimeOffset completedAt,
        string message,
        string? triggeredBy = null,
        string? cause = null)
    {
        ArgumentNullException.ThrowIfNull(deployment);
        ArgumentNullException.ThrowIfNull(revision);

        var baseKey = CreateRecordBaseKey(deployment.SourceResourceId, deployment.Id, deployment.RevisionId);
        records.AddOrUpdate(
            GetPendingRecordKey(baseKey, completedAt),
            _ => new ResourceOrchestratorDeploymentRecord(
                deployment.Id,
                deployment.OrchestratorId,
                deployment.SourceResourceId,
                deployment.ServiceId,
                deployment.RevisionId,
                deployment,
                revision,
                deployment.Status,
                completedAt,
                completedAt,
                Normalize(triggeredBy),
                Normalize(cause),
                Normalize(message),
                ReplicaGroup: revision.ReplicaGroup),
            (_, existing) => existing with
            {
                Deployment = deployment,
                Revision = revision,
                Status = deployment.Status,
                CompletedAt = completedAt,
                TriggeredBy = Normalize(triggeredBy) ?? existing.TriggeredBy,
                Cause = Normalize(cause) ?? existing.Cause,
                Message = Normalize(message),
                Error = null,
                ReplicaGroup = revision.ReplicaGroup
            });
        pendingRecordKeys.TryRemove(baseKey, out _);
    }

    public void RecordFailed(
        ResourceOrchestratorDeployment deployment,
        DateTimeOffset completedAt,
        string error,
        string? triggeredBy = null,
        string? cause = null)
    {
        ArgumentNullException.ThrowIfNull(deployment);

        var failedDeployment = deployment with { Status = ResourceOrchestratorDeploymentStatus.Failed };
        var baseKey = CreateRecordBaseKey(deployment.SourceResourceId, deployment.Id, deployment.RevisionId);
        records.AddOrUpdate(
            GetPendingRecordKey(baseKey, completedAt),
            _ => new ResourceOrchestratorDeploymentRecord(
                failedDeployment.Id,
                failedDeployment.OrchestratorId,
                failedDeployment.SourceResourceId,
                failedDeployment.ServiceId,
                failedDeployment.RevisionId,
                failedDeployment,
                Revision: null,
                ResourceOrchestratorDeploymentStatus.Failed,
                completedAt,
                completedAt,
                Normalize(triggeredBy),
                Normalize(cause),
                Error: Normalize(error)),
            (_, existing) => existing with
            {
                Deployment = failedDeployment,
                Status = ResourceOrchestratorDeploymentStatus.Failed,
                CompletedAt = completedAt,
                TriggeredBy = Normalize(triggeredBy) ?? existing.TriggeredBy,
                Cause = Normalize(cause) ?? existing.Cause,
                Error = Normalize(error)
            });
        pendingRecordKeys.TryRemove(baseKey, out _);
    }

    private string GetPendingRecordKey(
        string baseKey,
        DateTimeOffset timestamp) =>
        pendingRecordKeys.TryGetValue(baseKey, out var recordKey)
            ? recordKey
            : CreateRecordKey(baseKey, timestamp);

    private static string CreateRecordBaseKey(
        string sourceResourceId,
        string deploymentId,
        string revisionId) =>
        string.Join('\u001f', sourceResourceId, deploymentId, revisionId);

    private static string CreateRecordKey(
        string baseKey,
        DateTimeOffset timestamp) =>
        string.Join('\u001f', baseKey, timestamp.UtcTicks.ToString(CultureInfo.InvariantCulture));

    private static string CreateRevisionNumberKey(
        string sourceResourceId,
        string serviceId) =>
        string.Join('\u001f', sourceResourceId, serviceId);

    private static ResourceOrchestratorEnvironmentRevisionId? Normalize(
        ResourceOrchestratorEnvironmentRevisionId? value) =>
        value is { IsEmpty: false } ? value : null;

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
