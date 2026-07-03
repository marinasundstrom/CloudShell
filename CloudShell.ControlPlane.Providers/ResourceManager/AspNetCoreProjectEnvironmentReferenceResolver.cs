using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.ControlPlane.Providers;

public sealed class AspNetCoreProjectEnvironmentReferenceResolver(
    IEnumerable<IConfigurationEntryReferenceResolver> configurationEntryResolvers,
    IEnumerable<ISecretReferenceResolver> secretResolvers) :
    IAspNetCoreProjectRuntimeEnvironmentProvider
{
    private readonly IReadOnlyList<IConfigurationEntryReferenceResolver> _configurationEntryResolvers =
        configurationEntryResolvers.ToArray();
    private readonly IReadOnlyList<ISecretReferenceResolver> _secretResolvers =
        secretResolvers.ToArray();

    public async ValueTask<IReadOnlyDictionary<string, string>> ResolveAsync(
        Resource resource,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resource);

        var environmentVariables = ProjectEnvironmentVariableReader.ReadAspNetCoreProject(resource.Attributes);
        if (environmentVariables.Count == 0)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var context = new ResourceSettingResolutionContext(
            resource.EffectiveResourceId,
            Operation: "run");
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (name, variable) in environmentVariables)
        {
            if (string.IsNullOrWhiteSpace(name) ||
                (variable.ConfigurationEntryRef is null && variable.SecretRef is null))
            {
                continue;
            }

            values[name.Trim()] = await ResolveValueAsync(
                name,
                variable.ConfigurationEntryRef,
                variable.SecretRef,
                context,
                cancellationToken);
        }

        return values;
    }

    private async ValueTask<string> ResolveValueAsync(
        string name,
        ResourceConfigurationEntryReference? configurationEntry,
        ResourceSecretReference? secret,
        ResourceSettingResolutionContext context,
        CancellationToken cancellationToken)
    {
        if (configurationEntry is not null)
        {
            return ResolveConfigurationEntryValue(
                name,
                ToAbstractionsReference(configurationEntry),
                context);
        }

        if (secret is not null)
        {
            return await ResolveSecretValueAsync(
                name,
                ToAbstractionsReference(secret),
                context,
                cancellationToken);
        }

        return string.Empty;
    }

    private string ResolveConfigurationEntryValue(
        string name,
        ConfigurationEntryReference reference,
        ResourceSettingResolutionContext context)
    {
        var errors = new List<string>();
        foreach (var resolver in _configurationEntryResolvers)
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

    private async ValueTask<string> ResolveSecretValueAsync(
        string name,
        SecretReference reference,
        ResourceSettingResolutionContext context,
        CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        foreach (var resolver in _secretResolvers)
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

    private static ConfigurationEntryReference ToAbstractionsReference(
        ResourceConfigurationEntryReference reference) =>
        new(
            reference.StoreResourceId,
            reference.Name,
            reference.Version);

    private static SecretReference ToAbstractionsReference(
        ResourceSecretReference reference) =>
        new(
            reference.VaultResourceId,
            reference.Name,
            reference.Version);
}
