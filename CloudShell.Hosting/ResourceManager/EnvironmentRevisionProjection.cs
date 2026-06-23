using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Hosting.ResourceManager;

public static class EnvironmentRevisionProjection
{
    public const string BaselineRevisionId = "baseline-current";

    public static IReadOnlyList<EnvironmentRevisionProjectionRow> CreateRows(
        IReadOnlyList<Resource> resources)
    {
        ArgumentNullException.ThrowIfNull(resources);

        var deploymentRows = resources
            .Where(resource => resource.ResourceAttributes.ContainsKey(ResourceAttributeNames.DeploymentId))
            .Select(EnvironmentDeploymentProjectionRow.Create)
            .ToArray();
        var projectedRevisionRows = deploymentRows
            .Where(row => IsProjected(row.EnvironmentRevisionId))
            .GroupBy(row => row.EnvironmentRevisionId, StringComparer.OrdinalIgnoreCase)
            .Select(CreateProjectedRevisionRow)
            .OrderBy(row => row.EnvironmentRevisionId, StringComparer.OrdinalIgnoreCase)
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
            Description: "Programmatic declarations");

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
            rows.Select(row => row.Status).FirstOrDefault(IsProjected) ?? "not projected",
            "Deployment outcome");
    }

    private static bool IsProjected(string value) =>
        !string.IsNullOrWhiteSpace(value) &&
        !string.Equals(value, "not projected", StringComparison.OrdinalIgnoreCase);

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
            resource.ResourceAttributes.GetValueOrDefault(name) ?? "not projected";
    }
}

public sealed record EnvironmentRevisionProjectionRow(
    string EnvironmentRevisionId,
    int ResourceCount,
    int DeploymentCount,
    int ServiceCount,
    int ReplicaGroupCount,
    string LatestStatus,
    string Description);
