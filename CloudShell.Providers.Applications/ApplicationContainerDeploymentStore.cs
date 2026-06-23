using System.Text.Json;
using Microsoft.Extensions.Hosting;

namespace CloudShell.Providers.Applications;

public sealed class ApplicationContainerDeploymentStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly object _gate = new();
    private readonly string _historyPath;
    private ApplicationContainerDeploymentHistory _history;

    public ApplicationContainerDeploymentStore(
        ApplicationProviderOptions options,
        IHostEnvironment environment)
    {
        _historyPath = ResolvePath(options.ContainerDeploymentHistoryPath, environment.ContentRootPath);
        _history = LoadHistory();
    }

    public IReadOnlyList<ApplicationContainerDeployment> List(string applicationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationId);

        lock (_gate)
        {
            return _history.Deployments
                .Where(deployment => string.Equals(
                    deployment.ApplicationId,
                    applicationId,
                    StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(deployment => deployment.CreatedAt)
                .ToArray();
        }
    }

    public IReadOnlyList<ApplicationContainerRevisionHistoryEntry> ListRevisions(string applicationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationId);

        lock (_gate)
        {
            return _history.Revisions
                .Where(revision => string.Equals(
                    revision.ApplicationId,
                    applicationId,
                    StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(revision => revision.CreatedAt)
                .ToArray();
        }
    }

    public void RecordDeployment(
        ApplicationContainerDeployment deployment,
        ApplicationContainerRevisionHistoryEntry revision,
        ApplicationContainerRevisionHistoryEntry? sourceRevision = null)
    {
        ArgumentNullException.ThrowIfNull(deployment);
        ArgumentNullException.ThrowIfNull(revision);

        lock (_gate)
        {
            var deployments = _history.Deployments.ToList();
            var deploymentIndex = deployments.FindIndex(item =>
                string.Equals(item.Id, deployment.Id, StringComparison.OrdinalIgnoreCase));
            if (deploymentIndex >= 0)
            {
                deployments[deploymentIndex] = deployment;
            }
            else
            {
                deployments.Add(deployment);
            }

            var revisions = _history.Revisions
                .Select(existing =>
                    string.Equals(existing.ApplicationId, revision.ApplicationId, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(existing.Status, ApplicationContainerRevisionStatuses.Active, StringComparison.OrdinalIgnoreCase)
                        ? existing with { Status = ApplicationContainerRevisionStatuses.Superseded }
                        : existing)
                .ToList();

            if (sourceRevision is not null &&
                revisions.All(existing => !IsSameApplicationRevision(existing, sourceRevision)))
            {
                revisions.Add(sourceRevision);
            }

            revisions.RemoveAll(existing => IsSameApplicationRevision(existing, revision));
            revisions.Add(revision);

            _history = new ApplicationContainerDeploymentHistory(deployments, revisions);

            Persist();
        }
    }

    public void RemoveApplication(string applicationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationId);

        lock (_gate)
        {
            _history = new ApplicationContainerDeploymentHistory(
                _history.Deployments
                    .Where(deployment => !string.Equals(
                        deployment.ApplicationId,
                        applicationId,
                        StringComparison.OrdinalIgnoreCase))
                    .ToArray(),
                _history.Revisions
                    .Where(revision => !string.Equals(
                        revision.ApplicationId,
                        applicationId,
                        StringComparison.OrdinalIgnoreCase))
                    .ToArray());
            Persist();
        }
    }

    private ApplicationContainerDeploymentHistory LoadHistory()
    {
        if (!File.Exists(_historyPath))
        {
            return ApplicationContainerDeploymentHistory.Empty;
        }

        var json = File.ReadAllText(_historyPath);
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind == JsonValueKind.Array)
        {
            var deployments = JsonSerializer.Deserialize<IReadOnlyList<ApplicationContainerDeployment>>(
                json,
                SerializerOptions);
            return new ApplicationContainerDeploymentHistory(deployments ?? [], []);
        }

        return JsonSerializer.Deserialize<ApplicationContainerDeploymentHistory>(json, SerializerOptions) ??
            ApplicationContainerDeploymentHistory.Empty;
    }

    private void Persist()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_historyPath)!);
        File.WriteAllText(_historyPath, JsonSerializer.Serialize(_history, SerializerOptions));
    }

    private static string ResolvePath(string path, string contentRootPath) =>
        Path.IsPathRooted(path)
            ? path
            : Path.GetFullPath(path, contentRootPath);

    private static bool IsSameApplicationRevision(
        ApplicationContainerRevisionHistoryEntry left,
        ApplicationContainerRevisionHistoryEntry right) =>
        string.Equals(left.ApplicationId, right.ApplicationId, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(left.Id, right.Id, StringComparison.OrdinalIgnoreCase);
}

public sealed record ApplicationContainerDeploymentHistory(
    IReadOnlyList<ApplicationContainerDeployment> Deployments,
    IReadOnlyList<ApplicationContainerRevisionHistoryEntry> Revisions)
{
    public static ApplicationContainerDeploymentHistory Empty { get; } = new([], []);
}

public sealed record ApplicationContainerDeployment(
    string Id,
    string ApplicationId,
    string RevisionId,
    string? SourceRevisionId,
    string Image,
    int RequestedReplicas,
    DateTimeOffset CreatedAt,
    string Status,
    string ChangeKind,
    string? TriggeredBy = null,
    string? OrchestratorDeploymentId = null);

public static class ApplicationContainerDeploymentStatuses
{
    public const string Completed = "completed";
    public const string Failed = "failed";
}

public sealed record ApplicationContainerRevisionHistoryEntry(
    string Id,
    string ApplicationId,
    string Image,
    int RequestedReplicas,
    DateTimeOffset CreatedAt,
    string Status,
    string ChangeKind,
    string? SourceRevisionId = null,
    string? TriggeredBy = null,
    string? DeploymentId = null);

public static class ApplicationContainerRevisionStatuses
{
    public const string Active = "active";
    public const string Superseded = "superseded";
    public const string Failed = "failed";
}
