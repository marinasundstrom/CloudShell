namespace CloudShell.Providers.Configuration;

public sealed class ConfigurationEntryInput(string? name = null, string? value = null)
{
    public string? Name { get; set; } = name;

    public string? Value { get; set; } = value;

    public static ConfigurationEntryInput FromEntry(ConfigurationEntry entry) =>
        new(entry.Name, entry.Value);
}
