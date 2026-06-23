using CloudShell.Abstractions.Logging;
using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.ResourceManager;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace CloudShell.Providers.Applications;

public sealed class ApplicationLogProvider(
    ApplicationResourceStore store,
    ApplicationContainerDeploymentStore containerDeployments,
    LocalProcessRunner localProcesses,
    ApplicationContainerHostResolver containerHosts,
    IHostEnvironment environment,
    ILoggerFactory? loggerFactory = null,
    ApplicationResourceDefinitionNormalizer? definitionNormalizer = null) : ILogProvider
{
    private static readonly ApplicationWorkloadConfigurationFactory WorkloadConfigurationFactory = new();
    private static readonly ApplicationContainerOrchestratorDeploymentFactory ContainerOrchestratorDeploymentFactory = new();
    private static readonly ApplicationContainerRevisionService ContainerRevisionService = new();
    private static readonly ContainerApplicationRuntimeRevisionPolicy ContainerRuntimeRevisionPolicy = new();
    private readonly ILogger dockerHostLogger =
        loggerFactory?.CreateLogger(CloudShellLogCategories.DockerHostLifecycle) ??
        NullLogger.Instance;
    private readonly ApplicationResourceDefinitionNormalizer definitionNormalizer =
        definitionNormalizer ?? new ApplicationResourceDefinitionNormalizer(environment);

    public string Id => ApplicationResourceProviderIds.Applications;

    public string DisplayName => "Applications";

    public IReadOnlyList<LogSource> GetLogSources() => store
        .GetApplications()
        .Select(ResolveDefinition)
        .SelectMany(CreateLogSources)
        .ToArray();

    public async Task<IReadOnlyList<LogEntry>> ReadLogSourceAsync(
        string logSourceId,
        int maxEntries = 200,
        DateTimeOffset? before = null,
        CancellationToken cancellationToken = default)
    {
        if (TryGetRuntimeContainerLogTarget(logSourceId, out var target))
        {
            return await ReadRuntimeContainerLogAsync(target, maxEntries, before, cancellationToken);
        }

        if (TryGetApplicationLogId(logSourceId, out var applicationId))
        {
            var entries = await localProcesses.ReadLogAsync(applicationId, maxEntries, before, cancellationToken);
            return entries
                .Where(IsConsoleLogEntry)
                .ToArray();
        }

        return [];
    }

    public async IAsyncEnumerable<LogEntry> StreamLogSourceAsync(
        string logSourceId,
        int initialEntries = 50,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!TryGetApplicationLogId(logSourceId, out var applicationId))
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

    private async Task<IReadOnlyList<LogEntry>> ReadRuntimeContainerLogAsync(
        RuntimeContainerLogTarget target,
        int maxEntries,
        DateTimeOffset? before,
        CancellationToken cancellationToken)
    {
        var engine = await containerHosts.ResolveStaticAsync(target.Application, cancellationToken);
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

    private IReadOnlyList<LogSource> CreateLogSources(ApplicationResourceDefinition application) =>
    [
        .. CreateApplicationLogSources(application),
        .. CreateRuntimeContainerLogSources(application)
    ];

    private static IReadOnlyList<LogSource> CreateApplicationLogSources(ApplicationResourceDefinition application)
    {
        var source = ApplicationLogSources.GetPrimaryApplicationLogSource(application);
        return
        [
            new LogSource(
                GetLogId(application.Id),
                source.Name,
                "Applications",
                application.Name,
                LogSourceKind.Resource,
                Kind: source.Kind,
                Format: source.Format,
                Storage: source.Storage,
                Capabilities: source.Capabilities,
                ResourceId: application.Id,
                Location: source.Location,
                ProducerResourceId: source.ProducerResourceId,
                Description: source.Description,
                Origin: source.Origin,
                Configuration: source.Configuration,
                Purpose: source.Purpose,
                Availability: source.Availability)
        ];
    }

    private IReadOnlyList<LogSource> CreateRuntimeContainerLogSources(
        ApplicationResourceDefinition application)
    {
        if (!IsReplicaModeEnabled(application))
        {
            return [];
        }

        var deployment = CreateContainerOrchestratorDeployment(
            application,
            runtimeRevisionScoped: true);
        var replicaGroup = ResourceOrchestratorReplicaGroups.CreateDefaultReplicaGroup(deployment.Spec.Service);
        return replicaGroup.Instances
            .Select(instance =>
            {
                var resourceId = ApplicationResourceNames.CreateRuntimeContainerResourceId(
                    application.Id,
                    instance.ReplicaOrdinal);
                var source = ApplicationLogSources.CreateRuntimeContainerLogSource(
                    application.Id,
                    instance,
                    ApplicationLogSources.GetPrimaryApplicationLogSource(application));
                return new LogSource(
                    GetRuntimeContainerLogId(application.Id, instance.ReplicaOrdinal),
                    source.Name,
                    "Applications",
                    instance.Name,
                    LogSourceKind.Resource,
                    Kind: source.Kind,
                    Format: source.Format,
                    Storage: source.Storage,
                    Capabilities: source.Capabilities,
                    ResourceId: resourceId,
                    Location: source.Location,
                    ProducerResourceId: source.ProducerResourceId,
                    Description: source.Description,
                    Origin: source.Origin,
                    Configuration: source.Configuration,
                    Purpose: source.Purpose,
                    Availability: source.Availability);
            })
            .ToArray();
    }

    private bool TryGetRuntimeContainerLogTarget(
        string logSourceId,
        out RuntimeContainerLogTarget target)
    {
        foreach (var application in store.GetApplications().Select(ResolveDefinition).Where(IsReplicaModeEnabled))
        {
            var deployment = CreateContainerOrchestratorDeployment(
                application,
                runtimeRevisionScoped: true);
            var replicaGroup = ResourceOrchestratorReplicaGroups.CreateDefaultReplicaGroup(deployment.Spec.Service);
            foreach (var instance in replicaGroup.Instances)
            {
                if (string.Equals(
                        logSourceId,
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

    private ResourceOrchestratorDeployment CreateContainerOrchestratorDeployment(
        ApplicationResourceDefinition application,
        bool runtimeRevisionScoped)
    {
        var revision = ContainerRevisionService.GetEffectiveRevision(application);
        return ContainerOrchestratorDeploymentFactory.CreateDeployment(
            application,
            ResourceState.Unknown,
            CreateWorkloadConfiguration(application),
            runtimeRevisionScoped &&
                ContainerRuntimeRevisionPolicy.ShouldUseRevisionScopedRuntimeInstances(
                    application,
                    revision,
                    containerDeployments.ListRevisions(application.Id)));
    }

    private ResourceWorkloadConfiguration CreateWorkloadConfiguration(
        ApplicationResourceDefinition application) =>
        WorkloadConfigurationFactory.Create(
            application,
            application.EnvironmentVariables,
            application.Observability ?? ResourceObservability.Default);

    private ApplicationResourceDefinition ResolveDefinition(ApplicationResourceDefinition definition) =>
        definitionNormalizer.Resolve(definition);

    private static string GetLogId(string applicationId) => $"{applicationId}:logs";

    private static string GetRuntimeContainerLogId(string applicationId, int replica) =>
        $"{ApplicationResourceNames.CreateRuntimeContainerResourceId(applicationId, replica)}:logs";

    private static bool TryGetApplicationLogId(
        string logSourceId,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? applicationId) =>
        TryGetApplicationIdFromLogId(logSourceId, ":logs", out applicationId);

    private static bool TryGetApplicationIdFromLogId(
        string logSourceId,
        string suffix,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? applicationId)
    {
        if (logSourceId.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            applicationId = logSourceId[..^suffix.Length];
            return true;
        }

        applicationId = null;
        return false;
    }

    private static bool IsReplicaModeEnabled(ApplicationResourceDefinition application) =>
        ApplicationResourceTypes.IsContainerApp(application.ResourceType) &&
        application.ReplicasEnabled;

    private static bool IsConsoleLogEntry(LogEntry entry) =>
        string.Equals(entry.Source, "stdout", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(entry.Source, "stderr", StringComparison.OrdinalIgnoreCase);

    private sealed record RuntimeContainerLogTarget(
        ApplicationResourceDefinition Application,
        ResourceOrchestratorServiceInstance Instance);
}
