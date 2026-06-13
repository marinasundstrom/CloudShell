using CloudShell.Client.Authentication;
using CloudShell.ControlPlane.Client;

var scope = args.Length > 0 && !string.IsNullOrWhiteSpace(args[0])
    ? args[0]
    : Environment.GetEnvironmentVariable(EnvironmentCloudShellResourceCredential.ScopeEnvironmentVariable) ??
        "ControlPlane.Access";
var controlPlaneBaseAddress = args.Length > 1 && Uri.TryCreate(args[1], UriKind.Absolute, out var argumentBaseAddress)
    ? argumentBaseAddress
    : GetControlPlaneBaseAddress();

try
{
    var credential = new DefaultCloudShellResourceCredential();
    var token = await credential.GetTokenAsync(new CloudShellResourceTokenRequest([scope]));

    Console.WriteLine("CloudShell resource credential acquired a token.");
    Console.WriteLine($"Scope: {scope}");
    Console.WriteLine($"ExpiresOn: {token.ExpiresOn?.ToString("O") ?? "unknown"}");

    if (controlPlaneBaseAddress is not null)
    {
        var controlPlane = new RemoteControlPlane(
            controlPlaneBaseAddress,
            credential,
            [scope]);
        var resources = await controlPlane.ListResourcesAsync();
        Console.WriteLine($"CloudShell Control Plane client listed {resources.Count} resources.");
    }
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

static Uri? GetControlPlaneBaseAddress()
{
    var configured = Environment.GetEnvironmentVariable("CloudShell__ControlPlane__BaseAddress") ??
        Environment.GetEnvironmentVariable("CLOUDSHELL_CONTROL_PLANE_ENDPOINT");
    return Uri.TryCreate(configured, UriKind.Absolute, out var value)
        ? value
        : null;
}
