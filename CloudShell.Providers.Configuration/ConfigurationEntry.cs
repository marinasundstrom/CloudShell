namespace CloudShell.Providers.Configuration;

public sealed record ConfigurationEntry(
    string Name,
    string Value,
    bool IsSecret = false);
