namespace CloudShell.Configuration.Client;

public sealed record CloudShellConfigurationEntry(
    string Name,
    string Value,
    bool IsSecret);
