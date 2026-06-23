namespace CloudShell.Providers.Applications;

public sealed partial class ApplicationResourceService
{
    public ApplicationResourceDefinition? GetApplication(string id) =>
        store.GetApplication(id) is { } application
            ? ResolveDefinition(application)
            : null;

    public IReadOnlyList<ApplicationResourceDefinition> GetApplications() => store
        .GetApplications()
        .Select(ResolveDefinition)
        .ToArray();
}
