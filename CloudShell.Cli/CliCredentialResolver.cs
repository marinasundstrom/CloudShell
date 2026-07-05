using CloudShell.Client.Authentication;

namespace CloudShell.Cli;

internal static class CliCredentialResolver
{
    private static readonly CloudShellResourceTokenRequest ControlPlaneTokenRequest =
        new(["ControlPlane.Access"]);

    public static async ValueTask<string?> ResolveBearerTokenAsync(
        string? bearerToken,
        CancellationToken cancellationToken) =>
        await ResolveBearerTokenAsync(
            bearerToken,
            new CloudShellProfileCredentialOptions(),
            cancellationToken);

    internal static async ValueTask<string?> ResolveBearerTokenAsync(
        string? bearerToken,
        CloudShellProfileCredentialOptions profileOptions,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(bearerToken))
        {
            return bearerToken.Trim();
        }

        try
        {
            var token = await new CloudShellProfileCredential(profileOptions)
                .GetTokenAsync(ControlPlaneTokenRequest, cancellationToken);
            return string.IsNullOrWhiteSpace(token.Token)
                ? null
                : token.Token.Trim();
        }
        catch (CloudShellCredentialUnavailableException)
        {
            return null;
        }
    }
}
