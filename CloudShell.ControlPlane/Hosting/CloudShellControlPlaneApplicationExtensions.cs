using CloudShell.Abstractions.Extensions;
using CloudShell.ControlPlane.Api;
using CloudShell.ControlPlane.Authentication;
using CloudShell.ControlPlane.ResourceManager;
using CloudShell.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CloudShell.ControlPlane.Hosting;

public static class CloudShellControlPlaneApplicationExtensions
{
    public static async Task<WebApplication> UseCloudShellControlPlaneAsync(
        this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var authenticationOptions = app.Services
            .GetRequiredService<IOptions<CloudShellAuthenticationOptions>>()
            .Value;
        var usesLocalIdentity =
            authenticationOptions.Enabled &&
            authenticationOptions.Mode.Equals("Identity", StringComparison.OrdinalIgnoreCase);

        app.Services.InitializeCloudShellDatabase(usesLocalIdentity);
        app.Services.PersistProgrammaticResourceDeclarations();
        await app.Services.StartProgrammaticResourceDeclarationsAsync();

        if (usesLocalIdentity)
        {
            await using var scope = app.Services.CreateAsyncScope();
            await scope.ServiceProvider
                .GetRequiredService<CloudShellIdentitySeeder>()
                .SeedAsync();
        }

        var extensionRegistry = app.Services.GetRequiredService<CloudShellExtensionRegistry>();
        extensionRegistry.Validate(app.Services.GetRequiredService<ICloudShellExtensionActivationStore>());

        app.UseAuthentication();
        app.UseCloudShellBuiltInBearerAuthentication();
        app.UseAuthorization();

        return app;
    }

    public static IEndpointRouteBuilder MapCloudShellControlPlane(
        this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapCloudShellControlPlaneOpenApi();
        endpoints.MapCloudShellBuiltInAuthority();
        endpoints.MapCloudShellControlPlaneApi();
        endpoints.MapCloudShellContainerAppsApi();
        endpoints.MapCloudShellControlPlaneAuthentication();

        return endpoints;
    }

    private static IEndpointRouteBuilder MapCloudShellControlPlaneAuthentication(
        this IEndpointRouteBuilder endpoints)
    {
        var configuredOptions = endpoints.ServiceProvider
            .GetRequiredService<IOptions<CloudShellAuthenticationOptions>>()
            .Value;

        if (!configuredOptions.Enabled ||
            (!configuredOptions.Mode.Equals("OpenIdConnect", StringComparison.OrdinalIgnoreCase) &&
             !configuredOptions.Mode.Equals("External", StringComparison.OrdinalIgnoreCase)))
        {
            return endpoints;
        }

        endpoints.MapGet("/account/challenge", (
            string? returnUrl,
            IOptions<CloudShellAuthenticationOptions> options) =>
        {
            var redirectUri = IsLocalReturnUrl(returnUrl) ? returnUrl! : "/";
            return Results.Challenge(
                new Microsoft.AspNetCore.Authentication.AuthenticationProperties
                {
                    RedirectUri = redirectUri
                },
                [options.Value.ChallengeScheme]);
        })
        .AllowAnonymous()
        .ExcludeFromDescription();

        return endpoints;
    }

    private static bool IsLocalReturnUrl(string? returnUrl) =>
        !string.IsNullOrWhiteSpace(returnUrl) &&
        returnUrl.StartsWith('/') &&
        !returnUrl.StartsWith("//", StringComparison.Ordinal);
}
