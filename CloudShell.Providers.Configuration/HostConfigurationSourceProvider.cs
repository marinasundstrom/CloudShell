using CloudShell.Abstractions.ResourceManager;
using Microsoft.Extensions.Configuration;
using System.Globalization;

namespace CloudShell.Providers.Configuration;

public sealed class HostConfigurationSourceProvider(
    ConfigurationProviderOptions options,
    IConfiguration configuration) :
    IResourceProvider,
    IConfigurationEntryReferenceResolver,
    IProgrammaticResourceDeclarationProvider,
    IResourceAutoStartPolicyProvider
{
    public const string ProviderId = "host-configuration";

    public const string ResourceType = "configuration.host";

    public string Id => ProviderId;

    public string DisplayName => "Host configuration";

    public IReadOnlyList<Resource> GetResources() =>
        options.DeclaredHostConfigurationSources
            .Select(source => Normalize(source.Definition))
            .Select(CreateResource)
            .ToArray();

    public bool CanApplyDeclaration(ResourceDeclaration declaration) =>
        string.Equals(declaration.ProviderId, Id, StringComparison.OrdinalIgnoreCase);

    public bool CanEvaluateAutoStartPolicy(ResourceDeclaration declaration) =>
        CanApplyDeclaration(declaration);

    public ResourceAutoStartPolicy GetAutoStartPolicy(ResourceDeclaration declaration) =>
        new(
            StartOnControlPlaneStart: false,
            StartAsDependency: true,
            StartAfterCreate: false);

    public async Task ApplyDeclarationAsync(
        ResourceDeclaration declaration,
        IResourceRegistrationStore registrations,
        CancellationToken cancellationToken = default)
    {
        var source = options.DeclaredHostConfigurationSources.FirstOrDefault(source =>
            string.Equals(source.Definition.Id, declaration.ResourceId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                $"Host configuration source declaration '{declaration.ResourceId}' was not found.");

        source.Definition = Normalize(source.Definition);

        await registrations.RegisterAsync(
            Id,
            source.Definition.Id,
            NormalizeGroupId(declaration.ResourceGroupId),
            [],
            cancellationToken: cancellationToken);
    }

    public ResourceSettingResolutionResult ResolveConfigurationEntry(
        ConfigurationEntryReference reference,
        ResourceSettingResolutionContext context)
    {
        var source = options.DeclaredHostConfigurationSources
            .Select(source => Normalize(source.Definition))
            .FirstOrDefault(source =>
                string.Equals(source.Id, reference.StoreResourceId, StringComparison.OrdinalIgnoreCase));
        if (source is null)
        {
            return ResourceSettingResolutionResult.Failed(
                $"Host configuration source '{reference.StoreResourceId}' was not found.");
        }

        if (!source.Entries.Contains(reference.EntryName, StringComparer.OrdinalIgnoreCase))
        {
            return ResourceSettingResolutionResult.Failed(
                $"Host configuration entry '{reference.EntryName}' is not exposed by '{reference.StoreResourceId}'.");
        }

        var value = configuration[reference.EntryName];
        return value is null
            ? ResourceSettingResolutionResult.Failed(
                $"Host configuration entry '{reference.EntryName}' was not configured.")
            : ResourceSettingResolutionResult.Resolved(value);
    }

    private Resource CreateResource(HostConfigurationSourceDefinition source) =>
        new(
            source.Id,
            source.Name,
            "Host configuration",
            DisplayName,
            "local",
            ResourceState.Running,
            [],
            $"{source.Entries.Count} exposed entries",
            DateTimeOffset.UtcNow,
            [],
            TypeId: ResourceType,
            ResourceClass: ResourceClass.Configuration,
            Attributes: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [ResourceAttributeNames.ConfigurationEntryCount] =
                    source.Entries.Count.ToString(CultureInfo.InvariantCulture)
            });

    private static HostConfigurationSourceDefinition Normalize(HostConfigurationSourceDefinition source)
    {
        var id = string.IsNullOrWhiteSpace(source.Id)
            ? CreateId(source.Name)
            : source.Id.Trim();

        return source with
        {
            Id = id,
            Name = string.IsNullOrWhiteSpace(source.Name)
                ? id
                : source.Name.Trim(),
            Entries = source.Entries
                .Where(entry => !string.IsNullOrWhiteSpace(entry))
                .Select(entry => entry.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(entry => entry, StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };
    }

    private static string CreateId(string name)
    {
        var slug = string.Join(
            '-',
            name.Trim()
                .ToLowerInvariant()
                .Split(
                    [' ', '_', '.', ':', '/', '\\'],
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        return string.IsNullOrWhiteSpace(slug)
            ? $"configuration:host:{Guid.NewGuid():N}"
            : $"configuration:host:{slug}";
    }

    private static string? NormalizeGroupId(string? resourceGroupId) =>
        string.IsNullOrWhiteSpace(resourceGroupId) ? null : resourceGroupId;
}
