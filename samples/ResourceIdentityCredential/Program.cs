using CloudShell.Abstractions.Authentication;

var scope = args.Length > 0 && !string.IsNullOrWhiteSpace(args[0])
    ? args[0]
    : Environment.GetEnvironmentVariable(EnvironmentCloudShellResourceCredential.ScopeEnvironmentVariable) ??
        "ControlPlane.Access";

try
{
    var credential = new DefaultCloudShellResourceCredential();
    var token = await credential.GetTokenAsync(new CloudShellResourceTokenRequest([scope]));

    Console.WriteLine("CloudShell resource credential acquired a token.");
    Console.WriteLine($"Scope: {scope}");
    Console.WriteLine($"ExpiresOn: {token.ExpiresOn?.ToString("O") ?? "unknown"}");
}
catch (CloudShellCredentialUnavailableException exception)
{
    Console.Error.WriteLine($"CloudShell resource credential is unavailable: {exception.Message}");
    Environment.ExitCode = 2;
}
catch (CloudShellAuthenticationException exception)
{
    Console.Error.WriteLine($"CloudShell resource credential authentication failed: {exception.Message}");
    Environment.ExitCode = 3;
}
