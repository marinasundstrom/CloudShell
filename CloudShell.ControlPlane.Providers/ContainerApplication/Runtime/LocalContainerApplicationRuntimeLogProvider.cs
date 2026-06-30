using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.ResourceManager;
using System.Globalization;
using System.Text.Json;
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

        return new LogSource(
            CreateLogSourceId(resource),
            $"Replica {replicaOrdinal} logs",
            DisplayName,
            sourceName,
            LogSourceKind.Resource,
            Kind: ResourceLogSourceKind.Container,
            Format: LogFormat.JsonConsole,
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

        var result = await commandRunner.RunAsync(
            "docker",
            arguments,
            cancellationToken,
            throwOnError: false);
        var entries = ParseContainerLogOutput(result.Output, containerName, null)
            .Concat(ParseContainerLogOutput(
                result.Error,
                containerName,
                result.ExitCode == 0 ? "Error" : null))
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

    private static string GetAttribute(ResourceManagerResource resource, string name) =>
        resource.ResourceAttributes.TryGetValue(name, out var value)
            ? value
            : string.Empty;

    private static IReadOnlyList<LogEntry> ParseContainerLogOutput(
        string output,
        string source,
        string? severity)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return [];
        }

        return output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => ParseContainerLogLine(line, source, severity))
            .ToArray();
    }

    private static LogEntry ParseContainerLogLine(
        string line,
        string source,
        string? severity)
    {
        var normalized = line.TrimEnd('\r');
        var timestamp = DateTimeOffset.UtcNow;
        var message = normalized;
        var separatorIndex = normalized.IndexOf(' ');
        if (separatorIndex > 0 &&
            DateTimeOffset.TryParse(
                normalized[..separatorIndex],
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out var parsedTimestamp))
        {
            timestamp = parsedTimestamp;
            message = normalized[(separatorIndex + 1)..];
        }

        return TryParseJsonConsoleLog(message, source, severity, timestamp) ??
            new LogEntry(timestamp, message, severity, source);
    }

    private static LogEntry? TryParseJsonConsoleLog(
        string line,
        string fallbackSource,
        string? fallbackSeverity,
        DateTimeOffset fallbackTimestamp)
    {
        if (!line.TrimStart().StartsWith('{'))
        {
            return null;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(line);
        }
        catch (JsonException)
        {
            return null;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var timestamp = TryGetDateTimeOffset(root, "timestamp") ??
                TryGetDateTimeOffset(root, "Timestamp") ??
                fallbackTimestamp;
            var message = FirstNonEmpty(
                TryGetString(root, "message"),
                TryGetString(root, "Message"),
                TryGetString(root, "renderedMessage"),
                TryGetString(root, "body"),
                line) ?? line;
            var severity = FirstNonEmpty(
                TryGetString(root, "severity"),
                TryGetString(root, "Severity"),
                TryGetString(root, "logLevel"),
                TryGetString(root, "LogLevel"),
                fallbackSeverity);
            var source = FirstNonEmpty(
                TryGetString(root, "source"),
                TryGetString(root, "Source"),
                TryGetString(root, "sourceContext"),
                TryGetString(root, "SourceContext"),
                fallbackSource) ?? fallbackSource;
            var category = FirstNonEmpty(
                TryGetString(root, "category"),
                TryGetString(root, "Category"),
                TryGetString(root, "logger"),
                TryGetString(root, "Logger"));
            var eventId = FirstNonEmpty(
                TryGetScalar(root, "eventId"),
                TryGetScalar(root, "EventId"));
            var traceId = FirstNonEmpty(
                TryGetString(root, "traceId"),
                TryGetString(root, "TraceId"));
            var spanId = FirstNonEmpty(
                TryGetString(root, "spanId"),
                TryGetString(root, "SpanId"));
            var exceptionSummary = FirstNonEmpty(
                TryGetString(root, "exceptionSummary"),
                TryGetString(root, "ExceptionSummary"),
                TryGetString(root, "exception"),
                TryGetString(root, "Exception"));
            var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            AddStructuredAttributes(root, "attributes", attributes);
            AddStructuredAttributes(root, "Attributes", attributes);
            AddStructuredAttributes(root, "state", attributes);
            AddStructuredAttributes(root, "State", attributes);

            return new LogEntry(
                timestamp,
                message.TrimEnd(),
                severity,
                source,
                eventId,
                category,
                traceId,
                spanId,
                exceptionSummary,
                attributes.Count == 0 ? null : attributes);
        }
    }

    private static DateTimeOffset? TryGetDateTimeOffset(JsonElement root, string propertyName)
    {
        var value = TryGetString(root, propertyName);
        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal,
            out var parsed)
                ? parsed
                : null;
    }

    private static string? TryGetString(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static string? TryGetScalar(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Number => property.GetRawText(),
            JsonValueKind.True => bool.TrueString,
            JsonValueKind.False => bool.FalseString,
            _ => null
        };
    }

    private static void AddStructuredAttributes(
        JsonElement root,
        string propertyName,
        Dictionary<string, string> attributes)
    {
        if (!root.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var item in property.EnumerateObject())
        {
            var value = item.Value.ValueKind == JsonValueKind.String
                ? item.Value.GetString()
                : item.Value.GetRawText();
            if (!string.IsNullOrWhiteSpace(value))
            {
                attributes[item.Name] = value!;
            }
        }
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
}
