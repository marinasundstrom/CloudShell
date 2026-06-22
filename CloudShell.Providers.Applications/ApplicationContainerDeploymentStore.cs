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
    private List<ApplicationContainerDeployment> _deployments;

    public ApplicationContainerDeploymentStore(
        ApplicationProviderOptions options,
        IHostEnvironment environment)
    {
        _historyPath = ResolvePath(options.ContainerDeploymentHistoryPath, environment.ContentRootPath);
        _deployments = LoadDeployments();
    }

    public IReadOnlyList<ApplicationContainerDeployment> List(string applicationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationId);

        lock (_gate)
        {
            return _deployments
                .Where(deployment => string.Equals(
                    deployment.ApplicationId,
                    applicationId,
                    StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(deployment => deployment.CreatedAt)
                .ToArray();
        }
    }

    public void Save(ApplicationContainerDeployment deployment)
    {
        ArgumentNullException.ThrowIfNull(deployment);

        lock (_gate)
        {
            var index = _deployments.FindIndex(item =>
                string.Equals(item.Id, deployment.Id, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                _deployments[index] = deployment;
            }
            else
            {
                _deployments.Add(deployment);
            }

            Persist();
        }
    }

    public void RemoveApplication(string applicationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationId);

        lock (_gate)
        {
            _deployments.RemoveAll(deployment => string.Equals(
                deployment.ApplicationId,
                applicationId,
                StringComparison.OrdinalIgnoreCase));
            Persist();
        }
    }

    private List<ApplicationContainerDeployment> LoadDeployments()
    {
        if (!File.Exists(_historyPath))
        {
            return [];
        }

        var json = File.ReadAllText(_historyPath);
        return JsonSerializer.Deserialize<List<ApplicationContainerDeployment>>(json, SerializerOptions) ?? [];
    }

    private void Persist()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_historyPath)!);
        File.WriteAllText(_historyPath, JsonSerializer.Serialize(_deployments, SerializerOptions));
    }

    private static string ResolvePath(string path, string contentRootPath) =>
        Path.IsPathRooted(path)
            ? path
            : Path.GetFullPath(path, contentRootPath);
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
