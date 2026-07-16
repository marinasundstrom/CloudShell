namespace CloudShell.ControlPlane.Providers;

public interface IConfigurationStoreInspector
{
    ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> InspectAsync(
        Resource resource,
        CancellationToken cancellationToken = default);
}

public sealed class NoopConfigurationStoreInspector :
    IConfigurationStoreInspector
{
    public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> InspectAsync(
        Resource resource,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>(
        [
            ConfigurationStoreInspectorReadiness.CreateMissingDiagnostic(resource)
        ]);
}

internal static class ConfigurationStoreInspectorReadiness
{
    public const string DiagnosticCode = "configuration.store.inspectorMissing";

    public static bool IsMissing(IConfigurationStoreInspector? inspector) =>
        inspector is null or NoopConfigurationStoreInspector;

    public static string CreateMissingReason(Resource resource) =>
        $"Configuration Store resource '{resource.EffectiveResourceId}' cannot be inspected because no Configuration Store inspector is registered.";

    public static ResourceDefinitionDiagnostic CreateMissingDiagnostic(Resource resource) =>
        ResourceDefinitionDiagnostic.Error(
            DiagnosticCode,
            CreateMissingReason(resource),
            resource.EffectiveResourceId);
}

public sealed class ConfigurationStoreRuntimeInspector(
    ConfigurationStoreRuntimeOptions? options = null) :
    IConfigurationStoreInspector
{
    private readonly ConfigurationStoreRuntimeOptions _options =
        options ?? new ConfigurationStoreRuntimeOptions();

    public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> InspectAsync(
        Resource resource,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resource);

        return ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>(
        [
            new(
                ResourceDefinitionDiagnosticSeverity.Information,
                "configuration.store.inspect.runtimeSettings",
                $"Configuration Store runtime has {_options.Settings.Count} configured setting{(_options.Settings.Count == 1 ? string.Empty : "s")} for '{resource.EffectiveResourceId}'.",
                resource.EffectiveResourceId)
        ]);
    }
}
