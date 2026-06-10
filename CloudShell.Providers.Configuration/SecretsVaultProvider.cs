using CloudShell.Abstractions.ResourceManager;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CloudShell.Providers.Configuration;

public sealed partial class SecretsVaultProvider(ConfigurationProviderOptions options) :
    IResourceProvider,
    IResourceProcedureProvider,
    ISecretReferenceResolver,
    IProgrammaticResourceDeclarationProvider,
    IResourceAutoStartPolicyProvider,
    IResourceTemplateProvider
{
    private static readonly JsonSerializerOptions TemplateSerializerOptions = new(JsonSerializerDefaults.Web);

    public const string ProviderId = "secrets-vault";

    public const string ResourceType = "secrets.vault";

    public string Id => ProviderId;

    public string DisplayName => "Secrets Vault";

    public SecretsVaultDefinition? GetVault(string id) =>
        options.DeclaredSecretsVaults
            .Select(vault => Normalize(vault.Definition))
            .FirstOrDefault(vault => string.Equals(vault.Id, id, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<SecretsVaultDefinition> GetVaults() =>
        options.DeclaredSecretsVaults
            .Select(vault => Normalize(vault.Definition))
            .ToArray();

    public IReadOnlyList<Resource> GetResources() =>
        options.DeclaredSecretsVaults
            .Select(vault => Normalize(vault.Definition))
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
        var vault = options.DeclaredSecretsVaults.FirstOrDefault(vault =>
            string.Equals(vault.Definition.Id, declaration.ResourceId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                $"Secrets Vault declaration '{declaration.ResourceId}' was not found.");

        vault.Definition = Normalize(vault.Definition);

        await registrations.RegisterAsync(
            Id,
            vault.Definition.Id,
            NormalizeGroupId(declaration.ResourceGroupId),
            [],
            cancellationToken: cancellationToken);
    }

    public async Task SetupVaultAsync(
        SecretsVaultDefinition definition,
        string? resourceGroupId,
        IResourceRegistrationStore registrations,
        CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(
            string.IsNullOrWhiteSpace(definition.Id)
                ? definition with { Id = CreateUniqueId(definition.Name) }
                : definition);

        RemoveVault(normalized.Id);
        options.DeclaredSecretsVaults.Add(new DeclaredSecretsVault(normalized));

        await registrations.RegisterAsync(
            Id,
            normalized.Id,
            NormalizeGroupId(resourceGroupId),
            [],
            cancellationToken: cancellationToken);
    }

    public async Task UpdateVaultAsync(
        SecretsVaultDefinition definition,
        string? resourceGroupId,
        IResourceRegistrationStore registrations,
        CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(definition);
        var existing = options.DeclaredSecretsVaults.FirstOrDefault(vault =>
            string.Equals(Normalize(vault.Definition).Id, normalized.Id, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            throw new InvalidOperationException($"Secrets Vault '{normalized.Id}' is not configured.");
        }

        existing.Definition = normalized;

        await registrations.AssignToGroupAsync(
            normalized.Id,
            NormalizeGroupId(resourceGroupId),
            [],
            cancellationToken: cancellationToken);
    }

    public async Task<ResourceProcedureResult> DeleteAsync(
        ResourceProcedureContext context,
        CancellationToken cancellationToken = default)
    {
        RemoveVault(context.Resource.Id);
        await context.Registrations.RemoveAsync(context.Resource.Id, cancellationToken);
        return ResourceProcedureResult.Completed("Secrets Vault removed.");
    }

    public ValueTask<ResourceSettingResolutionResult> ResolveSecretAsync(
        SecretReference reference,
        ResourceSettingResolutionContext context,
        CancellationToken cancellationToken = default)
    {
        var vault = options.DeclaredSecretsVaults
            .Select(vault => Normalize(vault.Definition))
            .FirstOrDefault(vault =>
                string.Equals(vault.Id, reference.VaultResourceId, StringComparison.OrdinalIgnoreCase));
        if (vault is null)
        {
            return ValueTask.FromResult(ResourceSettingResolutionResult.Failed(
                $"Secrets Vault '{reference.VaultResourceId}' was not found."));
        }

        var candidates = vault.Secrets
            .Where(secret => string.Equals(secret.Name, reference.SecretName, StringComparison.OrdinalIgnoreCase))
            .Where(secret => string.IsNullOrWhiteSpace(reference.Version) ||
                string.Equals(secret.Version, reference.Version, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var resolved = candidates.LastOrDefault();
        if (resolved is null)
        {
            var version = string.IsNullOrWhiteSpace(reference.Version)
                ? string.Empty
                : $" version '{reference.Version}'";
            return ValueTask.FromResult(ResourceSettingResolutionResult.Failed(
                $"Secret '{reference.SecretName}'{version} was not found in Secrets Vault '{reference.VaultResourceId}'."));
        }

        return ValueTask.FromResult(ResourceSettingResolutionResult.Resolved(resolved.Value));
    }

    public bool CanExport(Resource resource) =>
        string.Equals(resource.EffectiveTypeId, ResourceType, StringComparison.OrdinalIgnoreCase) &&
        options.DeclaredSecretsVaults
            .Select(vault => Normalize(vault.Definition))
            .Any(vault => string.Equals(vault.Id, resource.Id, StringComparison.OrdinalIgnoreCase));

    public Task<ResourceTemplateDefinition> ExportAsync(
        Resource resource,
        ResourceTemplateExportContext context,
        CancellationToken cancellationToken = default)
    {
        var vault = options.DeclaredSecretsVaults
            .Select(vault => Normalize(vault.Definition))
            .FirstOrDefault(vault => string.Equals(vault.Id, resource.Id, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Secrets Vault '{resource.Id}' is not configured.");

        var configuration = new SecretsVaultTemplateConfiguration(
            vault.Secrets
                .Select(secret => secret with { Value = string.Empty })
                .ToArray());

        return Task.FromResult(new ResourceTemplateDefinition(
            vault.Name,
            Id,
            ResourceType,
            resource.DependsOn,
            "1.0",
            JsonSerializer.SerializeToElement(configuration, TemplateSerializerOptions),
            vault.Id));
    }

    public bool CanImport(ResourceTemplateDefinition template) =>
        string.Equals(template.ProviderId, Id, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(template.ResourceType, ResourceType, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(template.ProviderConfigurationVersion, "1.0", StringComparison.OrdinalIgnoreCase);

    public async Task<ResourceTemplateImportResult> ImportAsync(
        ResourceTemplateDefinition template,
        ResourceTemplateImportContext context,
        CancellationToken cancellationToken = default)
    {
        if (!CanImport(template))
        {
            throw new InvalidOperationException("The Secrets Vault template is not supported.");
        }

        var configuration = template.Configuration.Deserialize<SecretsVaultTemplateConfiguration>(
            TemplateSerializerOptions)
            ?? throw new InvalidOperationException("The Secrets Vault template configuration is invalid.");

        var resourceId = string.IsNullOrWhiteSpace(template.ResourceId)
            ? CreateId(template.Name)
            : template.ResourceId.Trim();
        var vault = new DeclaredSecretsVault(new SecretsVaultDefinition(
            resourceId,
            template.Name,
            configuration.Secrets));

        options.DeclaredSecretsVaults.Add(vault);

        await context.Registrations.RegisterAsync(
            Id,
            resourceId,
            NormalizeGroupId(context.ResourceGroupId),
            [],
            cancellationToken: cancellationToken);

        return new ResourceTemplateImportResult(
            resourceId,
            $"Imported Secrets Vault '{template.Name}'. Secret values must be supplied after import.");
    }

    private Resource CreateResource(SecretsVaultDefinition vault) =>
        new(
            vault.Id,
            vault.Name,
            "Secrets Vault",
            DisplayName,
            "provider-owned",
            ResourceState.Running,
            [],
            $"{vault.Secrets.Count} secrets",
            DateTimeOffset.UtcNow,
            [],
            TypeId: ResourceType,
            ResourceClass: ResourceClass.SecretsVault,
            Attributes: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["secretsVault.secrets"] = vault.Secrets.Count.ToString(CultureInfo.InvariantCulture)
            });

    private static SecretsVaultDefinition Normalize(SecretsVaultDefinition vault)
    {
        var id = string.IsNullOrWhiteSpace(vault.Id)
            ? CreateId(vault.Name)
            : vault.Id.Trim();

        return vault with
        {
            Id = id,
            Name = string.IsNullOrWhiteSpace(vault.Name)
                ? id
                : vault.Name.Trim(),
            Secrets = vault.Secrets
                .Where(secret => !string.IsNullOrWhiteSpace(secret.Name))
                .Select(secret => secret with
                {
                    Name = secret.Name.Trim(),
                    Value = secret.Value ?? string.Empty,
                    Version = string.IsNullOrWhiteSpace(secret.Version) ? null : secret.Version.Trim()
                })
                .ToArray()
        };
    }

    private static string CreateId(string name)
    {
        var slug = SlugPattern()
            .Replace(name.Trim().ToLowerInvariant(), "-")
            .Trim('-');

        return string.IsNullOrWhiteSpace(slug)
            ? $"secrets-vault:{Guid.NewGuid():N}"
            : $"secrets-vault:{slug}";
    }

    private string CreateUniqueId(string name)
    {
        var candidate = CreateId(name);
        if (GetVault(candidate) is null)
        {
            return candidate;
        }

        var suffix = 2;
        while (GetVault($"{candidate}-{suffix}") is not null)
        {
            suffix++;
        }

        return $"{candidate}-{suffix}";
    }

    private void RemoveVault(string resourceId)
    {
        var existing = options.DeclaredSecretsVaults.FirstOrDefault(vault =>
            string.Equals(Normalize(vault.Definition).Id, resourceId, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            options.DeclaredSecretsVaults.Remove(existing);
        }
    }

    private static string? NormalizeGroupId(string? resourceGroupId) =>
        string.IsNullOrWhiteSpace(resourceGroupId) ? null : resourceGroupId;

    private sealed record SecretsVaultTemplateConfiguration(
        IReadOnlyList<SecretsVaultSecret> Secrets);

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex SlugPattern();
}
