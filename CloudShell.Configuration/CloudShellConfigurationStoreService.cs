using CloudShell.Abstractions.Authentication;

namespace CloudShell.Configuration;

internal sealed record CloudShellConfigurationStoreService(
    string Endpoint,
    CloudShellResourceCredential Credential,
    string IdentityScope);
