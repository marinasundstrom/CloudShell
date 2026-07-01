using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.ResourceManager;
using System.Globalization;
using ResourceManagerResource = CloudShell.Abstractions.ResourceManager.Resource;

namespace CloudShell.ControlPlane.Providers;

public sealed class LocalContainerApplicationRuntimeLogProvider(
    ILocalContainerApplicationCommandRunner commandRunner,
    IResourceManagerStore resourceManager) : ILogProvider
{
    public string Id => "resource-model.container-app.local-runtime.logs";

    public string DisplayName => "Container app local runtime";

    public IReadOnlyList<LogSource> GetLogSources() =>
        resourceManager
            .GetResources()
            .Where(IsRuntimeContainerReplica)
            .OrderBy(resource => resource.OwnerResourceId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(resource => ResolveReplicaOrdinal(resource))
            .Select(CreateLogSource)
            .ToArray();

    public bool CanOpenLogSource(LogSource source) =>
        ResolveRuntimeContainer(source.Id) is not null;

    public Task<IReadOnlyList<LogEntry>> ReadLogSourceAsync(
        string logSourceId,
        int maxEntries = 200,
        DateTimeOffset? before = null,
        CancellationToken cancellationToken = default)
    {
        var resource = ResolveRuntimeContainer(logSourceId);
        if (resource is null)
        {
            return Task.FromResult<IReadOnlyList<LogEntry>>([]);
        }

        return ReadReplicaLogsAsync(resource, maxEntries, before, cancellationToken);
    }

    public static string CreateLogSourceId(ResourceManagerResource resource)
    {
        var ownerResourceId = resource.OwnerResourceId ?? resource.ParentResourceId ?? resource.Id;
        var replicaOrdinal = ResolveReplicaOrdinal(resource);
        return $"{ownerResourceId}:replica-{replicaOrdinal}:logs";
    }

    private LogSource CreateLogSource(ResourceManagerResource resource)
    {
        var parent = resource.OwnerResourceId is null
            ? null
            : resourceManager.GetResource(resource.OwnerResourceId);
        var replicaOrdinal = ResolveReplicaOrdinal(resource);
        var sourceName = parent?.Name ?? resource.Name;
        var format = ResolveRuntimeLogFormat(parent);

        return new LogSource(
            CreateLogSourceId(resource),
            $"Replica {replicaOrdinal} logs",
            DisplayName,
            sourceName,
            LogSourceKind.Resource,
            Kind: ResourceLogSourceKind.Container,
            Format: format,
            Capabilities: LogSourceCapabilities.Read,
            ResourceId: resource.OwnerResourceId ?? resource.ParentResourceId ?? resource.Id,
            ProducerResourceId: resource.OwnerResourceId ?? resource.ParentResourceId ?? resource.Id,
            Description: "Runtime replica container logs.",
            Origin: ResourceLogSourceOrigin.ProviderProjected,
            Purpose: ResourceLogSourcePurpose.Default,
            Availability: LogSourceAvailability.ProducerRunning);
    }

    private async Task<IReadOnlyList<LogEntry>> ReadReplicaLogsAsync(
        ResourceManagerResource resource,
        int maxEntries,
        DateTimeOffset? before,
        CancellationToken cancellationToken)
    {
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

        var containerName = GetAttribute(resource, ResourceAttributeNames.RuntimeContainerName);
        arguments.Add(containerName);
        var ownerResource = FirstNonEmpty(resource.OwnerResourceId, resource.ParentResourceId) is { } ownerResourceId
            ? resourceManager.GetResource(ownerResourceId)
            : null;
        var format = ResolveRuntimeLogFormat(ownerResource);

        var result = await commandRunner.RunAsync(
            "docker",
            arguments,
            cancellationToken,
            throwOnError: false);
        var entries = ParseContainerLogOutput(result.Output, containerName, null, format)
            .Concat(ParseContainerLogOutput(
                result.Error,
                containerName,
                result.ExitCode == 0 ? "Error" : null,
                format))
            .OrderBy(entry => entry.Timestamp)
            .TakeLast(Math.Max(1, maxEntries))
            .ToArray();

        if (result.ExitCode != 0 && entries.Length == 0)
        {
            return
            [
                new LogEntry(
                    DateTimeOffset.UtcNow,
                    "Container runtime did not return logs for the runtime replica.",
                    "Error",
                    containerName)
            ];
        }

        return entries;
    }

    private ResourceManagerResource? ResolveRuntimeContainer(string logSourceId) =>
        resourceManager
            .GetResources()
            .FirstOrDefault(resource =>
                IsRuntimeContainerReplica(resource) &&
                string.Equals(CreateLogSourceId(resource), logSourceId, StringComparison.OrdinalIgnoreCase));

    private static bool IsRuntimeContainerReplica(ResourceManagerResource resource) =>
        string.Equals(resource.EffectiveTypeId, "runtime.container", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(
            GetAttribute(resource, ResourceAttributeNames.RuntimeKind),
            "containerReplica",
            StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrWhiteSpace(GetAttribute(resource, ResourceAttributeNames.RuntimeContainerName));

    private static string ResolveReplicaOrdinal(ResourceManagerResource resource) =>
        FirstNonEmpty(
            GetAttribute(resource, ResourceAttributeNames.RuntimeReplicaOrdinal),
            resource.Name) ?? "1";

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private static LogFormat ResolveRuntimeLogFormat(ResourceManagerResource? resource)
    {
        var source = resource?.ResourceLogSources.FirstOrDefault(source =>
            source.Kind is ResourceLogSourceKind.ProcessOutput or ResourceLogSourceKind.Container &&
            source.Purpose == ResourceLogSourcePurpose.Default);
        return source?.Format ?? LogFormat.PlainText;
    }

    private static string GetAttribute(ResourceManagerResource resource, string name) =>
        resource.ResourceAttributes.TryGetValue(name, out var value)
            ? value
            : string.Empty;

    private static IReadOnlyList<LogEntry> ParseContainerLogOutput(
        string output,
        string source,
        string? severity,
        LogFormat format)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return [];
        }

        return output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => ContainerApplicationRuntimeLogParser.ParseContainerLogLine(
                line,
                source,
                severity,
                format))
            .ToArray();
    }
}
