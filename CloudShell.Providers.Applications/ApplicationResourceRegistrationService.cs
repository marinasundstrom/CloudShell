using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Providers.Applications;

public sealed class ApplicationResourceRegistrationService(
    ApplicationResourceStore store,
    ApplicationResourceDefinitionNormalizer normalizer)
{
    public async Task SetupApplicationAsync(
        ApplicationResourceDefinition definition,
        string? resourceGroupId,
        IResourceRegistrationStore registrations,
        CancellationToken cancellationToken = default)
    {
        var normalized = normalizer.Normalize(
            string.IsNullOrWhiteSpace(definition.Id)
                ? definition with { Id = CreateUniqueImportId(definition.Name) }
                : definition);
        store.Save(normalized);

        await registrations.RegisterAsync(
            ApplicationResourceProviderIds.ForResourceType(normalized.ResourceType),
            normalized.Id,
            NormalizeGroupId(resourceGroupId),
            normalized.DependsOn,
            cancellationToken);
    }

    public async Task UpdateApplicationAsync(
        ApplicationResourceDefinition definition,
        string? resourceGroupId,
        IResourceRegistrationStore registrations,
        CancellationToken cancellationToken = default)
    {
        var existing = store.GetApplication(definition.Id);
        if (existing is null)
        {
            throw new InvalidOperationException($"Application resource '{definition.Id}' is not configured.");
        }

        var normalized = normalizer.Normalize(definition);

        store.Save(normalized);
        await registrations.AssignToGroupAsync(
            normalized.Id,
            NormalizeGroupId(resourceGroupId),
            normalized.DependsOn,
            cancellationToken);
    }

    public string CreateUniqueImportId(string name) =>
        CreateUniqueId(name, resourceId => store.GetApplication(resourceId) is not null);

    public string ValidateAvailableImportId(string resourceId)
    {
        var normalized = resourceId.Trim();
        if (store.GetApplication(normalized) is not null)
        {
            throw new InvalidOperationException($"Resource id '{normalized}' is already in use.");
        }

        return normalized;
    }

    private static string? NormalizeGroupId(string? resourceGroupId) =>
        string.IsNullOrWhiteSpace(resourceGroupId) ? null : resourceGroupId;

    private static string CreateUniqueId(string name, Func<string, bool> exists)
    {
        var candidate = ResourceId.FromName("application", name).Value;
        if (!exists(candidate))
        {
            return candidate;
        }

        var suffix = 2;
        while (exists($"{candidate}-{suffix}"))
        {
            suffix++;
        }

        return $"{candidate}-{suffix}";
    }
}
