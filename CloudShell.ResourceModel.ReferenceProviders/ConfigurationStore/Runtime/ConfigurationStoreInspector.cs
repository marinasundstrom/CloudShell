namespace CloudShell.ResourceModel.ReferenceProviders;

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
        ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>([]);
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
                "configuration.store.inspect.runtimeEntries",
                $"Configuration Store runtime has {_options.Entries.Count} configured entr{(_options.Entries.Count == 1 ? "y" : "ies")} for '{resource.EffectiveResourceId}'.",
                resource.EffectiveResourceId)
        ]);
    }
}
