using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.ResourceManager;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System.Globalization;
using ResourceManagerResource = CloudShell.Abstractions.ResourceManager.Resource;

namespace CloudShell.ControlPlane.Providers;

public sealed class LocalRabbitMQDockerRuntimeLogProvider(
    ILocalRabbitMQDockerCommandRunner docker,
    IResourceManagerStore resourceManager,
    IConfiguration configuration,
    IHostEnvironment hostEnvironment,
    IOptions<LocalRabbitMQDockerRuntimeOptions> options) : ILogProvider
{
    public string Id => "resource-model.rabbitmq.local-docker.logs";

    public string DisplayName => "RabbitMQ local Docker runtime";

    private LocalRabbitMQDockerRuntimeOptions Options => options.Value;

    public IReadOnlyList<LogSource> GetLogSources() =>
        resourceManager
            .GetResources()
            .Where(resource => TryGetDefinition(resource, out _))
            .OrderBy(resource => resource.Name, StringComparer.OrdinalIgnoreCase)
            .Select(CreateLogSource)
            .ToArray();

    public bool CanOpenLogSource(LogSource source) =>
        source.Kind == ResourceLogSourceKind.Container &&
        source.ResourceId is not null &&
        ResolveRabbitMQResource(source.ResourceId) is not null;

    public Task<IReadOnlyList<LogEntry>> ReadLogSourceAsync(
        string logSourceId,
        int maxEntries = 200,
        DateTimeOffset? before = null,
        CancellationToken cancellationToken = default)
    {
        var resource = ResolveRabbitMQResource(logSourceId) ??
            resourceManager
                .GetResources()
                .FirstOrDefault(resource =>
                    TryGetDefinition(resource, out _) &&
                    string.Equals(CreateLogSourceId(resource), logSourceId, StringComparison.OrdinalIgnoreCase));

        return resource is null
            ? Task.FromResult<IReadOnlyList<LogEntry>>([])
            : ReadContainerLogsAsync(resource, maxEntries, before, cancellationToken);
    }

    public ValueTask<ILogSourceSession?> OpenLogSourceAsync(
        LogSource source,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return ValueTask.FromResult<ILogSourceSession?>(
            CanOpenLogSource(source)
                ? new DelegatingRabbitMQDockerLogSourceSession(this, source)
                : null);
    }

    private LogSource CreateLogSource(ResourceManagerResource resource)
    {
        return new LogSource(
            CreateLogSourceId(resource),
            "Container logs",
            DisplayName,
            resource.Name,
            LogSourceKind.Resource,
            Kind: ResourceLogSourceKind.Container,
            Format: LogFormat.PlainText,
            Capabilities: LogSourceCapabilities.Read | LogSourceCapabilities.Stream,
            ResourceId: resource.Id,
            ProducerResourceId: resource.Id,
            Description: "RabbitMQ broker container stdout and stderr.",
            Origin: ResourceLogSourceOrigin.ProviderProjected,
            Purpose: ResourceLogSourcePurpose.Default,
            Availability: LogSourceAvailability.ResourceRunning);
    }

    private async Task<IReadOnlyList<LogEntry>> ReadContainerLogsAsync(
        ResourceManagerResource resource,
        int maxEntries,
        DateTimeOffset? before,
        CancellationToken cancellationToken)
    {
        if (!TryGetDefinition(resource, out var definition))
        {
            return [];
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

        arguments.Add(definition.ContainerName);

        var result = await docker.RunAsync(
            arguments,
            cancellationToken,
            throwOnError: false);
        var entries = ParseContainerLogOutput(result.Output, definition.ContainerName, null)
            .Concat(ParseContainerLogOutput(
                result.Error,
                definition.ContainerName,
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
                    "Container runtime did not return logs for the RabbitMQ broker container.",
                    "Error",
                    definition.ContainerName)
            ];
        }

        return entries;
    }

    private ResourceManagerResource? ResolveRabbitMQResource(string resourceId) =>
        resourceManager
            .GetResources()
            .FirstOrDefault(resource =>
                string.Equals(resource.Id, resourceId, StringComparison.OrdinalIgnoreCase) &&
                TryGetDefinition(resource, out _));

    private bool TryGetDefinition(
        ResourceManagerResource resource,
        out LocalRabbitMQDockerDefinition definition)
    {
        if (!string.Equals(
                resource.EffectiveTypeId,
                RabbitMQResourceTypeProvider.ResourceTypeId.ToString(),
                StringComparison.OrdinalIgnoreCase))
        {
            definition = null!;
            return false;
        }

        return LocalRabbitMQDockerRuntimeDefinitions.TryGetDefinition(
            resource.Id,
            resource.Name,
            resource.EffectiveTypeId,
            Options,
            configuration,
            hostEnvironment,
            out definition);
    }

    private static string CreateLogSourceId(ResourceManagerResource resource) =>
        $"{resource.Id}:rabbitmq-container:logs";

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
            .Select(line => ContainerApplicationRuntimeLogParser.ParseContainerLogLine(
                line,
                source,
                severity,
                LogFormat.PlainText))
            .ToArray();
    }

    private sealed class DelegatingRabbitMQDockerLogSourceSession(
        ILogProvider provider,
        LogSource source) : ILogSourceSession
    {
        public string Id { get; } = Guid.NewGuid().ToString("N");

        public string SourceId => source.Id;

        public LogSourceSessionStatus Status { get; private set; } = LogSourceSessionStatus.Active;

        public Task<IReadOnlyList<LogEntry>> ReadAsync(
            int maxEntries = 200,
            DateTimeOffset? before = null,
            CancellationToken cancellationToken = default) =>
            source.ResourceId is null
                ? Task.FromResult<IReadOnlyList<LogEntry>>([])
                : provider.ReadLogSourceAsync(source.ResourceId, maxEntries, before, cancellationToken);

        public IAsyncEnumerable<LogEntry> StreamAsync(
            int initialEntries = 50,
            CancellationToken cancellationToken = default) =>
            provider.StreamLogSourceAsync(source.ResourceId ?? source.Id, initialEntries, cancellationToken);

        public ValueTask DisposeAsync()
        {
            Status = LogSourceSessionStatus.Closed;
            return ValueTask.CompletedTask;
        }
    }
}
