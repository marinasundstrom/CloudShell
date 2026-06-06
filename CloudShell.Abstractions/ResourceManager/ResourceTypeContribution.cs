namespace CloudShell.Abstractions.ResourceManager;

public sealed record ResourceTypeContribution(
    string Id,
    string DisplayName,
    string Description,
    string Icon,
    int Order,
    Type RegistrationComponentType);
