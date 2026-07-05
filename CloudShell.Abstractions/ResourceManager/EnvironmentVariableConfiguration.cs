namespace CloudShell.Abstractions.ResourceManager;

public interface IEnvironmentVariableConfiguration
{
    IReadOnlyList<EnvironmentVariableAssignment> EnvironmentVariables { get; }
}

public interface IResourceEnvironmentVariableConfigurationProvider
{
    bool CanConfigureEnvironmentVariables(Resource resource);

    IReadOnlyList<EnvironmentVariableAssignment> GetConfiguredEnvironmentVariables(string resourceId);

    Task<ResourceProcedureResult> UpdateEnvironmentVariablesAsync(
        ResourceProcedureContext context,
        IReadOnlyList<EnvironmentVariableAssignment> environmentVariables,
        CancellationToken cancellationToken = default);
}

public sealed record EnvironmentVariableAssignment(
    string Name,
    string Value,
    ConfigurationSettingReference? ConfigurationSetting = null,
    SecretReference? Secret = null)
{
    public static EnvironmentVariableAssignment FromConfiguration(
        string name,
        ConfigurationSettingReference configurationSetting) =>
        new(name, string.Empty, ConfigurationSetting: configurationSetting);

    public static EnvironmentVariableAssignment FromSecret(
        string name,
        SecretReference secret) =>
        new(name, string.Empty, Secret: secret);
}
