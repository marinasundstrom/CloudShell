namespace CloudShell.Abstractions.ResourceManager;

public sealed record ResourceRegistration(
    string ResourceId,
    string ProviderId,
    string? ResourceGroupId,
    DateTimeOffset RegisteredAt,
    IReadOnlyList<string> DependsOn);
