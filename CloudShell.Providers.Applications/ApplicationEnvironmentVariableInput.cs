namespace CloudShell.Providers.Applications;

public sealed class ApplicationEnvironmentVariableInput(string? name = null, string? value = null)
{
    public string? Name { get; set; } = name;

    public string? Value { get; set; } = value;
}
