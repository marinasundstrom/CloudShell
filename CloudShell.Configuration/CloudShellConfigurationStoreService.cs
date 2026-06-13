using CloudShell.Client.Authentication;

namespace CloudShell.Configuration;

internal sealed record CloudShellConfigurationStoreService(
    string Endpoint,
    CloudShellResourceCredential Credential,
    string IdentityScope);
