using CloudShell.Abstractions.ResourceManager;
using System.Collections.Concurrent;

namespace CloudShell.ControlPlane.ResourceManager.Deployment;

public interface IResourceOrchestratorDeploymentStore
{
    ResourceOrchestratorRevision CreateRevision(
        ResourceOrchestratorDeployment deployment,
        DateTimeOffset createdAt,
        ResourceOrchestratorRevisionStatus status,
        ResourceOrchestratorReplicaGroup? replicaGroup = null);

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
    private readonly ConcurrentDictionary<string, int> revisionNumbers =
        new(StringComparer.OrdinalIgnoreCase);

    public ResourceOrchestratorRevision CreateRevision(
        ResourceOrchestratorDeployment deployment,
        DateTimeOffset createdAt,
        ResourceOrchestratorRevisionStatus status,
        ResourceOrchestratorReplicaGroup? replicaGroup = null)
    {
        ArgumentNullException.ThrowIfNull(deployment);

        var revisionNumber = revisionNumbers.AddOrUpdate(
            CreateRevisionNumberKey(deployment.SourceResourceId, deployment.Id),
            1,
            (_, current) => current + 1);
        return new ResourceOrchestratorRevision(
            deployment.RevisionId,
            deployment.Id,
            deployment.SourceResourceId,
            deployment.ServiceId,
            revisionNumber,
            createdAt,
            status,
            replicaGroup);
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

        records[CreateRecordKey(deployment.SourceResourceId, deployment.Id, deployment.RevisionId)] =
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

        records.AddOrUpdate(
            CreateRecordKey(deployment.SourceResourceId, deployment.Id, deployment.RevisionId),
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
        records.AddOrUpdate(
            CreateRecordKey(deployment.SourceResourceId, deployment.Id, deployment.RevisionId),
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
    }

    private static string CreateRecordKey(
        string sourceResourceId,
        string deploymentId,
        string revisionId) =>
        string.Join('\u001f', sourceResourceId, deploymentId, revisionId);

    private static string CreateRevisionNumberKey(
        string sourceResourceId,
        string deploymentId) =>
        string.Join('\u001f', sourceResourceId, deploymentId);

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
