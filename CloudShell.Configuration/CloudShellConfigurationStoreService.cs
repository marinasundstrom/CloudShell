namespace CloudShell.Configuration;

internal sealed record CloudShellConfigurationStoreService(
    string Endpoint,
    string? IdentityTokenEndpoint,
    string? IdentityClientId,
    string? IdentityClientSecret,
    string IdentityScope);
