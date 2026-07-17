using CloudShell.Abstractions.ControlPlane;
using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Hosting.ResourceManager;

public static class EnvironmentRevisionProjection
{
    public const string BaselineRevisionId = "baseline-current";
    private const string MissingValue = "not available";

    public static IReadOnlyList<EnvironmentRevisionProjectionRow> CreateRows(
        IReadOnlyList<Resource> resources,
        IReadOnlyList<ResourceDeploymentRecord>? deployments = null)
    {
        ArgumentNullException.ThrowIfNull(resources);

        var deploymentRows = (deployments ?? [])
            .Where(deployment => IsProjected(deployment.EnvironmentRevisionId))
            .GroupBy(deployment => deployment.EnvironmentRevisionId!, StringComparer.OrdinalIgnoreCase)
            .Select(CreateDeploymentRecordRevisionRow)
            .ToArray();
        var deploymentRevisionIds = deploymentRows
            .Select(row => row.EnvironmentRevisionId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var resourceAttributeRows = resources
            .Where(resource => resource.ResourceAttributes.ContainsKey(ResourceAttributeNames.DeploymentId))
            .Select(EnvironmentDeploymentProjectionRow.Create)
            .Where(row => !deploymentRevisionIds.Contains(row.EnvironmentRevisionId))
            .Where(row => IsProjected(row.EnvironmentRevisionId))
            .GroupBy(row => row.EnvironmentRevisionId, StringComparer.OrdinalIgnoreCase)
            .Select(CreateProjectedRevisionRow)
            .ToArray();
        var projectedRevisionRows = deploymentRows
            .Concat(resourceAttributeRows)
            .OrderBy(row => row.RevisionNumber is null)
            .ThenBy(row => row.RevisionNumber)
            .ThenBy(row => row.CreatedAt ?? DateTimeOffset.MaxValue)
            .ThenBy(row => row.EnvironmentRevisionId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (resources.Count == 0)
        {
            return projectedRevisionRows;
        }

        var baselineRow = new EnvironmentRevisionProjectionRow(
            BaselineRevisionId,
            resources.Select(resource => resource.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            DeploymentCount: 0,
            ServiceCount: 0,
            ReplicaGroupCount: 0,
            LatestStatus: "Declared",
            Description: "Declared resource graph");

        return
        [
            baselineRow,
            ..projectedRevisionRows
        ];
    }

    private static EnvironmentRevisionProjectionRow CreateProjectedRevisionRow(
        IGrouping<string, EnvironmentDeploymentProjectionRow> group)
    {
        var rows = group.ToArray();
        return new EnvironmentRevisionProjectionRow(
            group.Key,
            rows.Select(row => row.Resource.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            rows.Select(row => row.DeploymentId).Where(IsProjected).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            rows.Select(row => row.ServiceId).Where(IsProjected).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            rows.Select(row => row.ReplicaGroupId).Where(IsProjected).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            rows.Select(row => row.Status).FirstOrDefault(IsProjected) ?? MissingValue,
            "Deployment-produced environment revision");
    }

    private static EnvironmentRevisionProjectionRow CreateDeploymentRecordRevisionRow(
        IGrouping<string, ResourceDeploymentRecord> group)
    {
        var records = group.ToArray();
        var latest = records
            .OrderByDescending(record => record.CompletedAt ?? record.StartedAt)
            .First();
        return new EnvironmentRevisionProjectionRow(
            group.Key,
            records.Select(record => record.SourceResourceId).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            records.Select(record => record.DeploymentId).Where(IsProjected).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            records.Select(record => record.ServiceId).Where(IsProjected).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            records.Select(record => record.ReplicaGroup?.Id).Where(IsProjected).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            latest.EnvironmentRevisionStatus?.ToString() ?? latest.Status.ToString(),
            "Deployment-produced environment revision",
            latest.EnvironmentRevisionNumber,
            latest.EnvironmentRevisionCreatedAt ?? latest.CompletedAt ?? latest.StartedAt,
            latest.BasedOnEnvironmentRevisionId,
            latest.ProvisionedBy ?? latest.TriggeredBy);
    }

    private static bool IsProjected(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        !string.Equals(value, "not projected", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(value, MissingValue, StringComparison.OrdinalIgnoreCase);

    private sealed record EnvironmentDeploymentProjectionRow(
        Resource Resource,
        string DeploymentId,
        string Status,
        string EnvironmentRevisionId,
        string ServiceId,
        string ReplicaGroupId)
    {
        public static EnvironmentDeploymentProjectionRow Create(Resource resource) =>
            new(
                resource,
                GetAttribute(resource, ResourceAttributeNames.DeploymentId),
                GetAttribute(resource, ResourceAttributeNames.DeploymentStatus),
                GetAttribute(resource, ResourceAttributeNames.DeploymentEnvironmentRevisionId),
                GetAttribute(resource, ResourceAttributeNames.DeploymentServiceId),
                GetAttribute(resource, ResourceAttributeNames.DeploymentReplicaGroupId));

        private static string GetAttribute(Resource resource, string name) =>
            resource.ResourceAttributes.GetValueOrDefault(name) ?? MissingValue;
    }
}

public sealed record EnvironmentRevisionProjectionRow(
    string EnvironmentRevisionId,
    int ResourceCount,
    int DeploymentCount,
    int ServiceCount,
    int ReplicaGroupCount,
    string LatestStatus,
    string Description,
    int? RevisionNumber = null,
    DateTimeOffset? CreatedAt = null,
    string? BasedOnEnvironmentRevisionId = null,
    string? ProvisionedBy = null);
