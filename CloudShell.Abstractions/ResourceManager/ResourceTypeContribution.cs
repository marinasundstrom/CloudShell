namespace CloudShell.Abstractions.ResourceManager;

public sealed record ResourceTypeContribution(
    string Id,
    string DisplayName,
    string Description,
    string Icon,
    int Order,
    Type RegistrationComponentType,
    Type? UpdateComponentType = null,
    IReadOnlyList<ResourceTabContribution>? Tabs = null)
{
    public IReadOnlyList<ResourceTabContribution> ResourceTabs => Tabs ?? [];
}
