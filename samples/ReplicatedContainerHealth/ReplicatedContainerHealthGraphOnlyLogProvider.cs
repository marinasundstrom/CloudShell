using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.ResourceManager;
using System.Globalization;
using System.Text.Json;

internal sealed class ReplicatedContainerHealthGraphOnlyLogProvider(
    IReplicatedContainerHealthCommandRunner commandRunner,
    IResourceManagerStore resourceManager) : ILogProvider
{
    private const string ContainerReplicasAttribute = "container.replicas";

    public string Id => "replicated-container-health.runtime";

    public string DisplayName => "Replicated Container Health";

    public IReadOnlyList<LogSource> GetLogSources()
    {
        var resource = resourceManager.GetResource(ReplicatedContainerHealthGraphOnlyRuntimeConventions.GraphApiResourceId);
        if (resource is null)
        {
            return [];
        }

        var replicas = ResolveReplicas(resource);
        return Enumerable
            .Range(1, replicas)
            .Select(replica => new LogSource(
                GetLogSourceId(replica),
                $"Replica {replica.ToString(CultureInfo.InvariantCulture)} logs",
                DisplayName,
                resource.Name,
                LogSourceKind.Resource,
                Kind: ResourceLogSourceKind.Container,
                Format: LogFormat.JsonConsole,
                Capabilities: LogSourceCapabilities.Read,
                ResourceId: ReplicatedContainerHealthGraphOnlyRuntimeConventions.GraphApiResourceId,
                ProducerResourceId: ReplicatedContainerHealthGraphOnlyRuntimeConventions.GraphApiResourceId,
                Description: "Graph-only replica container logs.",
                Origin: ResourceLogSourceOrigin.ProviderProjected,
                Purpose: ResourceLogSourcePurpose.Default,
                Availability: LogSourceAvailability.ProducerRunning))
            .ToArray();
    }

    public bool CanOpenLogSource(LogSource source) =>
        string.Equals(source.Provider, DisplayName, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(source.ResourceId, ReplicatedContainerHealthGraphOnlyRuntimeConventions.GraphApiResourceId, StringComparison.OrdinalIgnoreCase) &&
        TryGetReplicaFromLogSourceId(source.Id, out _);

    public Task<IReadOnlyList<LogEntry>> ReadLogSourceAsync(
        string logSourceId,
        int maxEntries = 200,
        DateTimeOffset? before = null,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetReplicaFromLogSourceId(logSourceId, out var replica))
        {
            return Task.FromResult<IReadOnlyList<LogEntry>>([]);
        }

        return ReadReplicaLogsAsync(replica, maxEntries, before, cancellationToken);
    }

    internal static string GetLogSourceId(int replica) =>
        $"{ReplicatedContainerHealthGraphOnlyRuntimeConventions.GraphApiResourceId}:replica-{replica.ToString(CultureInfo.InvariantCulture)}:logs";

    private async Task<IReadOnlyList<LogEntry>> ReadReplicaLogsAsync(
        int replica,
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

        var containerName = ReplicatedContainerHealthGraphOnlyRuntimeConventions.CreateReplicaContainerName(replica);
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
                    "Container runtime did not return logs for the graph replica.",
                    "Error",
                    containerName)
            ];
        }

        return entries;
    }

    private static int ResolveReplicas(Resource resource) =>
        resource.ResourceAttributes.TryGetValue(ContainerReplicasAttribute, out var value) &&
        int.TryParse(value, out var replicas)
            ? Math.Max(1, replicas)
            : 1;

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
            out var timestamp)
                ? timestamp
                : null;
    }

    private static void AddStructuredAttributes(
        JsonElement root,
        string propertyName,
        Dictionary<string, string> attributes)
    {
        if (!TryGetProperty(root, propertyName, out var values) ||
            values.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var property in values.EnumerateObject())
        {
            var value = GetScalarString(property.Value);
            if (!string.IsNullOrWhiteSpace(value))
            {
                attributes[property.Name] = value;
            }
        }
    }

    private static bool TryGetProperty(
        JsonElement root,
        string name,
        out JsonElement value)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string? TryGetString(JsonElement root, string propertyName) =>
        TryGetProperty(root, propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string? TryGetScalar(JsonElement root, string propertyName)
    {
        if (!TryGetProperty(root, propertyName, out var value))
        {
            return null;
        }

        return GetScalarString(value);
    }

    private static string? GetScalarString(JsonElement value) =>
        value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => bool.TrueString,
            JsonValueKind.False => bool.FalseString,
            _ => null
        };

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static bool TryGetReplicaFromLogSourceId(
        string logSourceId,
        out int replica)
    {
        replica = 0;
        var prefix = $"{ReplicatedContainerHealthGraphOnlyRuntimeConventions.GraphApiResourceId}:replica-";
        const string suffix = ":logs";
        if (!logSourceId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            !logSourceId.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var value = logSourceId[prefix.Length..^suffix.Length];
        return int.TryParse(value, out replica) && replica > 0;
    }
}
