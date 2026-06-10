namespace CloudShell.Abstractions.ResourceManager;

public interface IEnvironmentVariableConfiguration
{
    IReadOnlyList<EnvironmentVariableAssignment> EnvironmentVariables { get; }
}

public sealed record EnvironmentVariableAssignment(
    string Name,
    string Value,
    ConfigurationEntryReference? ConfigurationEntry = null,
    SecretReference? Secret = null)
{
    public static EnvironmentVariableAssignment FromConfiguration(
        string name,
        ConfigurationEntryReference configurationEntry) =>
        new(name, string.Empty, ConfigurationEntry: configurationEntry);

    public static EnvironmentVariableAssignment FromSecret(
        string name,
        SecretReference secret) =>
        new(name, string.Empty, Secret: secret);
}
