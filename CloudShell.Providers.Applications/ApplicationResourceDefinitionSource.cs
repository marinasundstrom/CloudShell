namespace CloudShell.Providers.Applications;

internal sealed class ApplicationResourceDefinitionSource(
    ApplicationResourceStore store,
    ApplicationResourceDefinitionNormalizer definitionNormalizer) : IApplicationResourceDefinitionSource
{
    public ApplicationResourceDefinition? GetApplication(string id) =>
        store.GetApplication(id) is { } application
            ? definitionNormalizer.Resolve(application)
            : null;

    public IReadOnlyList<ApplicationResourceDefinition> GetApplications() => store
        .GetApplications()
        .Select(definitionNormalizer.Resolve)
        .ToArray();
}
