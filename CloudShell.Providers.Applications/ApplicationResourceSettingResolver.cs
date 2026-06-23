using CloudShell.Abstractions.Authorization;
using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Providers.Applications;

public sealed class ApplicationResourceSettingResolver(
    ResourceDeclarationStore declarations,
    IEnumerable<IConfigurationEntryReferenceResolver> configurationEntryResolvers,
    IEnumerable<ISecretReferenceResolver> secretResolvers)
{
    public async Task<IReadOnlyList<EnvironmentVariableAssignment>> ResolveConfiguredEnvironmentVariablesAsync(
        ApplicationResourceDefinition definition,
        string? resourceGroupId,
        CancellationToken cancellationToken = default)
    {
        var identity = ResolveIdentity(definition.Id);
        var context = new ResourceSettingResolutionContext(
            definition.Id,
            resourceGroupId,
            "run",
            identity,
            identity is null ? null : FormatIdentity(identity, definition));
        var variables = new List<EnvironmentVariableAssignment>();

        foreach (var setting in definition.AppSettings)
        {
            var value = await ResolveSettingValueAsync(
                setting.Name,
                setting.Value,
                setting.ConfigurationEntry,
                setting.Secret,
                context,
                cancellationToken);
            variables.Add(new EnvironmentVariableAssignment(setting.Name, value));
        }

        foreach (var variable in definition.EnvironmentVariables)
        {
            var value = await ResolveSettingValueAsync(
                variable.Name,
                variable.Value,
                variable.ConfigurationEntry,
                variable.Secret,
                context,
                cancellationToken);
            variables.Add(new EnvironmentVariableAssignment(variable.Name, value));
        }

        return variables;
    }

    public async Task<string?> GetSettingResolutionUnavailableReasonAsync(
        ApplicationResourceDefinition application,
        string? resourceGroupId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _ = await ResolveConfiguredEnvironmentVariablesAsync(
                application,
                resourceGroupId,
                cancellationToken);
            return null;
        }
        catch (ResourceSettingResolutionException exception)
        {
            return exception.Message;
        }
    }

    private async Task<string> ResolveSettingValueAsync(
        string name,
        string? literalValue,
        ConfigurationEntryReference? configurationEntry,
        SecretReference? secret,
        ResourceSettingResolutionContext context,
        CancellationToken cancellationToken)
    {
        if (configurationEntry is not null)
        {
            return ResolveConfigurationEntryValue(name, configurationEntry, context);
        }

        if (secret is not null)
        {
            return await ResolveSecretValueAsync(name, secret, context, cancellationToken);
        }

        return literalValue ?? string.Empty;
    }

    private string ResolveConfigurationEntryValue(
        string name,
        ConfigurationEntryReference reference,
        ResourceSettingResolutionContext context)
    {
        var errors = new List<string>();
        foreach (var resolver in configurationEntryResolvers)
        {
            var result = resolver.ResolveConfigurationEntry(reference, context);
            if (result.IsResolved)
            {
                return result.Value ?? string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
            {
                errors.Add(result.ErrorMessage);
            }
        }

        var message = errors.Count == 0
            ? $"No configuration provider can resolve entry '{reference.EntryName}' from '{reference.StoreResourceId}'."
            : string.Join(" ", errors);
        throw new ResourceSettingResolutionException(name, "configuration-entry", message);
    }

    private async Task<string> ResolveSecretValueAsync(
        string name,
        SecretReference reference,
        ResourceSettingResolutionContext context,
        CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        foreach (var resolver in secretResolvers)
        {
            var result = await resolver.ResolveSecretAsync(reference, context, cancellationToken);
            if (result.IsResolved)
            {
                return result.Value ?? string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
            {
                errors.Add(result.ErrorMessage);
            }
        }

        var message = errors.Count == 0
            ? $"No vault provider can resolve secret '{reference.SecretName}' from '{reference.VaultResourceId}'."
            : string.Join(" ", errors);
        throw new ResourceSettingResolutionException(name, "secret", message);
    }

    public ResourceIdentityReference? ResolveIdentity(string resourceId)
    {
        var declaration = declarations.GetDeclaration(resourceId);
        return declaration?.IdentityBinding is null
            ? null
            : ResourceIdentityReference.ForResource(resourceId, declaration.IdentityBinding.Name);
    }

    private static string FormatIdentity(
        ResourceIdentityReference identity,
        ApplicationResourceDefinition? definition = null)
    {
        var resourceName = definition is not null &&
            string.Equals(identity.ResourceId, definition.Id, StringComparison.OrdinalIgnoreCase)
                ? FormatApplicationResourceName(definition)
                : identity.ResourceId;
        return string.IsNullOrWhiteSpace(identity.Name)
            ? resourceName
            : $"{resourceName}/{identity.Name}";
    }

    private static string FormatApplicationResourceName(ApplicationResourceDefinition application) =>
        string.IsNullOrWhiteSpace(application.Name)
            ? application.Id
            : application.Name;
}
