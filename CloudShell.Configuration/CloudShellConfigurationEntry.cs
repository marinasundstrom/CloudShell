namespace CloudShell.Configuration;

public sealed record CloudShellConfigurationEntry(
    string Name,
    string Value,
    bool IsSecret);
