using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.ControlPlane.Providers;

public sealed class PythonAppEnvironmentReferenceResolver(
    IEnumerable<IConfigurationSettingReferenceResolver> configurationSettingResolvers,
    IEnumerable<ISecretReferenceResolver> secretResolvers) :
    IPythonAppRuntimeEnvironmentProvider
{
    private readonly IReadOnlyList<IConfigurationSettingReferenceResolver> _configurationSettingResolvers =
        configurationSettingResolvers.ToArray();
    private readonly IReadOnlyList<ISecretReferenceResolver> _secretResolvers =
        secretResolvers.ToArray();

    public async ValueTask<IReadOnlyDictionary<string, string>> ResolveAsync(
        Resource resource,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resource);

        var environmentVariables = ProjectEnvironmentVariableReader.ReadPythonApp(resource.Attributes);
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
                (variable.ConfigurationSettingRef is null && variable.SecretRef is null))
            {
                continue;
            }

            values[name.Trim()] = await ResolveValueAsync(
                name,
                variable.ConfigurationSettingRef,
                variable.SecretRef,
                context,
                cancellationToken);
        }

        return values;
    }

    private async ValueTask<string> ResolveValueAsync(
        string name,
        ResourceConfigurationSettingReference? configurationSetting,
        ResourceSecretReference? secret,
        ResourceSettingResolutionContext context,
        CancellationToken cancellationToken)
    {
        if (configurationSetting is not null)
        {
            return ResolveConfigurationSettingValue(
                name,
                ToAbstractionsReference(configurationSetting),
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

    private string ResolveConfigurationSettingValue(
        string name,
        ConfigurationSettingReference reference,
        ResourceSettingResolutionContext context)
    {
        var errors = new List<string>();
        foreach (var resolver in _configurationSettingResolvers)
        {
            var result = resolver.ResolveConfigurationSetting(reference, context);
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
            ? $"No configuration provider can resolve setting '{reference.SettingName}' from '{reference.StoreResourceId}'."
            : string.Join(" ", errors);
        throw new ResourceSettingResolutionException(name, "configuration-setting", message);
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

    private static ConfigurationSettingReference ToAbstractionsReference(
        ResourceConfigurationSettingReference reference) =>
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
