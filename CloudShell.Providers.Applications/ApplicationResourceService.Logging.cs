using CloudShell.Abstractions.Logging;
using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.ResourceManager;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace CloudShell.Providers.Applications;

public sealed partial class ApplicationResourceService
{
    public IReadOnlyList<LogDescriptor> GetLogs() => store
        .GetApplications()
        .SelectMany(CreateLogDescriptors)
        .ToArray();

    public async Task<IReadOnlyList<LogEntry>> ReadLogAsync(
        string logId,
        int maxEntries = 200,
        DateTimeOffset? before = null,
        CancellationToken cancellationToken = default)
    {
        if (TryGetRuntimeContainerLogTarget(logId, out var target))
        {
            return await ReadRuntimeContainerLogAsync(target, maxEntries, before, cancellationToken);
        }

        if (TryGetApplicationLogId(logId, out var applicationId))
        {
            var entries = await localProcesses.ReadLogAsync(applicationId, maxEntries, before, cancellationToken);
            return entries
                .Where(IsConsoleLogEntry)
                .ToArray();
        }

        return [];
    }

    private async Task<IReadOnlyList<LogEntry>> ReadRuntimeContainerLogAsync(
        RuntimeContainerLogTarget target,
        int maxEntries,
        DateTimeOffset? before,
        CancellationToken cancellationToken)
    {
        var engine = await ResolveStaticContainerHostAsync(target.Application, cancellationToken);
        if (engine is null)
        {
            return
            [
                new LogEntry(
                    DateTimeOffset.UtcNow,
                    "Runtime replica logs require a configured container host.",
                    "Error",
                    target.Instance.Name)
            ];
        }

        var arguments = new List<string>
        {
            "logs",
            "--timestamps",
            "--tail",
            Math.Max(1, maxEntries).ToString(CultureInfo.InvariantCulture)
        };
        if (before is not null)
        {
            arguments.Add("--until");
            arguments.Add(before.Value.AddTicks(-1).UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
        }

        arguments.Add(target.Instance.Name);
        var result = await ApplicationContainerHostCommands.CaptureAsync(
            engine,
            arguments,
            cancellationToken,
            dockerHostLogger);

        var sourceFormat = ApplicationLogSources.GetPrimaryApplicationLogSource(target.Application).Format;
        var entries = ApplicationRuntimeLogParser.ParseContainerLogOutput(
                result.Output,
                target.Instance.Name,
                null,
                sourceFormat)
            .Concat(ApplicationRuntimeLogParser.ParseContainerLogOutput(
                result.Error,
                target.Instance.Name,
                result.ExitCode == 0 ? "Error" : null,
                sourceFormat))
            .ToArray();
        if (result.ExitCode != 0 && entries.Length == 0)
        {
            return
            [
                new LogEntry(
                    DateTimeOffset.UtcNow,
                    "Container runtime did not return logs for the replica.",
                    "Error",
                    target.Instance.Name)
            ];
        }

        return entries
            .OrderBy(entry => entry.Timestamp)
            .TakeLast(Math.Max(1, maxEntries))
            .ToArray();
    }

    public async IAsyncEnumerable<LogEntry> StreamLogAsync(
        string logId,
        int initialEntries = 50,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!TryGetApplicationLogId(logId, out var applicationId))
        {
            yield break;
        }

        await foreach (var entry in localProcesses.StreamLogAsync(
                           applicationId,
                           initialEntries,
                           cancellationToken))
        {
            if (IsConsoleLogEntry(entry))
            {
                yield return entry;
            }
        }
    }

    private IReadOnlyList<LogDescriptor> CreateLogDescriptors(ApplicationResourceDefinition application) =>
    [
        .. CreateApplicationLogDescriptors(application),
        .. CreateRuntimeContainerLogDescriptors(application)
    ];

    private static IReadOnlyList<LogDescriptor> CreateApplicationLogDescriptors(ApplicationResourceDefinition application)
    {
        var source = ApplicationLogSources.GetPrimaryApplicationLogSource(application);
        return
        [
            new LogDescriptor(
                GetLogId(application.Id),
                source.Name,
                "Applications",
                application.Name,
                LogSourceKind.Resource,
                ResourceId: application.Id,
                SupportsStreaming: source.Capabilities.HasFlag(LogSourceCapabilities.Stream),
                Description: source.Description,
                Kind: source.Kind,
                Format: source.Format,
                Storage: source.Storage,
                Capabilities: source.Capabilities,
                Location: source.Location,
                ProducerResourceId: source.ProducerResourceId,
                Origin: source.Origin,
                Configuration: source.Configuration,
                Purpose: source.Purpose,
                Availability: source.Availability)
        ];
    }

    private IReadOnlyList<LogDescriptor> CreateRuntimeContainerLogDescriptors(
        ApplicationResourceDefinition application)
    {
        if (!IsReplicaModeEnabled(application))
        {
            return [];
        }

        var deployment = CreateDefaultContainerOrchestratorDeployment(application, ResourceState.Unknown);
        return CreateDefaultContainerServiceInstances(deployment.Spec.Service)
            .Select(instance =>
            {
                var resourceId = CreateRuntimeContainerResourceId(application.Id, instance.ReplicaOrdinal);
                var source = ApplicationLogSources.CreateRuntimeContainerLogSource(
                    application.Id,
                    instance,
                    ApplicationLogSources.GetPrimaryApplicationLogSource(application));
                return new LogDescriptor(
                    GetRuntimeContainerLogId(application.Id, instance.ReplicaOrdinal),
                    source.Name,
                    "Applications",
                    instance.Name,
                    LogSourceKind.Resource,
                    ResourceId: resourceId,
                    Description: source.Description,
                    Kind: source.Kind,
                    Format: source.Format,
                    Storage: source.Storage,
                    Capabilities: source.Capabilities,
                    Location: source.Location,
                    ProducerResourceId: source.ProducerResourceId,
                    Origin: source.Origin,
                    Configuration: source.Configuration,
                    Purpose: source.Purpose,
                    Availability: source.Availability);
            })
            .ToArray();
    }

    private bool TryGetRuntimeContainerLogTarget(
        string logId,
        out RuntimeContainerLogTarget target)
    {
        foreach (var application in store.GetApplications().Select(ResolveDefinition).Where(IsReplicaModeEnabled))
        {
            var service = CreateDefaultContainerOrchestratorService(application);
            foreach (var instance in CreateDefaultContainerServiceInstances(service))
            {
                if (string.Equals(
                        logId,
                        GetRuntimeContainerLogId(application.Id, instance.ReplicaOrdinal),
                        StringComparison.OrdinalIgnoreCase))
                {
                    target = new RuntimeContainerLogTarget(application, instance);
                    return true;
                }
            }
        }

        target = null!;
        return false;
    }

    private static string GetLogId(string applicationId) => $"{applicationId}:logs";

    private static string GetRuntimeContainerLogId(string applicationId, int replica) =>
        $"{CreateRuntimeContainerResourceId(applicationId, replica)}:logs";

    private static bool TryGetApplicationLogId(
        string logId,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? applicationId) =>
        TryGetApplicationIdFromLogId(logId, ":logs", out applicationId);

    private static bool TryGetApplicationIdFromLogId(
        string logId,
        string suffix,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? applicationId)
    {
        if (logId.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            applicationId = logId[..^suffix.Length];
            return true;
        }

        applicationId = null;
        return false;
    }

    private static bool IsConsoleLogEntry(LogEntry entry) =>
        string.Equals(entry.Source, "stdout", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(entry.Source, "stderr", StringComparison.OrdinalIgnoreCase);

    private sealed record RuntimeContainerLogTarget(
        ApplicationResourceDefinition Application,
        ResourceOrchestratorServiceInstance Instance);
}
