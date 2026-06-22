using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.ResourceManager;
using System.Globalization;

namespace CloudShell.Providers.Applications;

internal static class ApplicationLogSources
{
    public static IReadOnlyList<ResourceLogSource> Normalize(
        IReadOnlyList<ResourceLogSource> logSources) =>
        logSources
            .Where(source =>
                !string.IsNullOrWhiteSpace(source.Id) &&
                !string.IsNullOrWhiteSpace(source.Name))
            .Select(source => source with
            {
                Id = source.Id.Trim(),
                Name = source.Name.Trim(),
                Location = NormalizeNullable(source.Location),
                ProducerResourceId = NormalizeNullable(source.ProducerResourceId),
                Description = NormalizeNullable(source.Description)
            })
            .DistinctBy(source => source.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public static IReadOnlyList<ResourceLogSource> GetApplicationLogSources(
        ApplicationResourceDefinition application) =>
        application.LogSources.Count == 0
            ? CreateDefaultResourceLogSources()
            : application.LogSources;

    public static ResourceLogSource GetPrimaryApplicationLogSource(
        ApplicationResourceDefinition application)
    {
        var sources = GetApplicationLogSources(application);
        return sources.FirstOrDefault(source => source.Purpose == ResourceLogSourcePurpose.Default) ??
            sources[0];
    }

    public static IReadOnlyList<ResourceLogSource> CreateRuntimeContainerLogSources(
        string producerResourceId,
        ResourceOrchestratorServiceInstance instance,
        ResourceLogSource source) =>
        [
            CreateRuntimeContainerLogSource(producerResourceId, instance, source)
        ];

    public static ResourceLogSource CreateRuntimeContainerLogSource(
        string producerResourceId,
        ResourceOrchestratorServiceInstance instance,
        ResourceLogSource source) =>
        new(
            "logs",
            $"Replica {instance.ReplicaOrdinal.ToString(CultureInfo.InvariantCulture)} logs",
            ResourceLogSourceKind.Container,
            Format: source.Format,
            Storage: source.Storage,
            Capabilities: ProjectRuntimeContainerLogSourceCapabilities(source.Capabilities),
            ProducerResourceId: producerResourceId,
            Description: "Runtime container stdout and stderr for this container app replica.",
            Origin: ResourceLogSourceOrigin.ProviderProjected,
            Configuration: source.Configuration,
            Purpose: source.Purpose,
            Availability: LogSourceAvailability.ProducerRunning);

    private static IReadOnlyList<ResourceLogSource> CreateDefaultResourceLogSources() =>
        [
            new ResourceLogSource(
                "console",
                "Console logs",
                ResourceLogSourceKind.ProcessOutput,
                Format: LogFormat.PlainText,
                Capabilities: LogSourceCapabilities.Read |
                    LogSourceCapabilities.Stream,
                Description: "Container app or process stdout and stderr.",
                Origin: ResourceLogSourceOrigin.ProviderDefault,
                Configuration: new LogSourceConfiguration(IsConfigurable: true, SchemaId: "cloudshell.logSource.format"),
                Purpose: ResourceLogSourcePurpose.Default,
                Availability: LogSourceAvailability.ResourceRunning)
        ];

    private static LogSourceCapabilities ProjectRuntimeContainerLogSourceCapabilities(
        LogSourceCapabilities capabilities) =>
        ((capabilities == LogSourceCapabilities.None ? LogSourceCapabilities.Read : capabilities) |
            LogSourceCapabilities.Read) &
        ~LogSourceCapabilities.Stream;

    private static string? NormalizeNullable(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
