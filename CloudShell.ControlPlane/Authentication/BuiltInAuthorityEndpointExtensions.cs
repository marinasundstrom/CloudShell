using System.Security.Cryptography;
using System.Security.Claims;
using System.Text;
using CloudShell.Abstractions.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CloudShell.ControlPlane.Authentication;

public static class BuiltInAuthorityEndpointExtensions
{
    public static IEndpointRouteBuilder MapCloudShellBuiltInAuthority(
        this IEndpointRouteBuilder endpoints)
    {
        var options = endpoints.ServiceProvider
            .GetRequiredService<IOptions<CloudShellAuthenticationOptions>>()
            .Value;
        if (!options.BuiltInAuthority.Enabled)
        {
            return endpoints;
        }

        endpoints.MapGet("/.well-known/openid-configuration", (
            HttpRequest request,
            BuiltInAuthorityTokenService authority) =>
        {
            return Results.Ok(authority.CreateDiscoveryDocument(GetBaseUrl(request)));
        })
        .AllowAnonymous()
        .ExcludeFromDescription();

        endpoints.MapGet("/api/auth/v1/jwks", (BuiltInAuthorityTokenService authority) =>
        {
            return Results.Ok(authority.CreateJsonWebKeySet());
        })
        .AllowAnonymous()
        .ExcludeFromDescription();

        endpoints.MapPost("/api/auth/v1/token", async (
            HttpRequest request,
            IServiceProvider services,
            IOptions<CloudShellAuthenticationOptions> configuredOptions,
            BuiltInAuthorityTokenService authority,
            BuiltInResourceIdentityRegistry resourceIdentityClients,
            CancellationToken cancellationToken) =>
        {
            var form = await request.ReadFormAsync(cancellationToken);
            var grantType = form["grant_type"].ToString();
            return grantType switch
            {
                "password" => await IssuePasswordTokenAsync(
                    form,
                    services,
                    configuredOptions.Value,
                    authority),
                "client_credentials" => IssueClientCredentialsToken(
                    form,
                    configuredOptions.Value,
                    authority,
                    resourceIdentityClients),
                _ => InvalidGrant("Unsupported grant_type.")
            };
        })
        .Accepts<IFormCollection>("application/x-www-form-urlencoded")
        .AllowAnonymous()
        .ExcludeFromDescription();

        return endpoints;
    }

    public static IApplicationBuilder UseCloudShellBuiltInBearerAuthentication(
        this IApplicationBuilder app)
    {
        return app.Use(async (context, next) =>
        {
            var options = context.RequestServices
                .GetRequiredService<IOptions<CloudShellAuthenticationOptions>>()
                .Value;
            if (!options.BuiltInAuthority.Enabled)
            {
                await next();
                return;
            }

            var authorization = context.Request.Headers.Authorization.ToString();
            const string bearerPrefix = "Bearer ";
            if (authorization.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var token = authorization[bearerPrefix.Length..].Trim();
                var principal = context.RequestServices
                    .GetRequiredService<BuiltInAuthorityTokenService>()
                    .ValidateToken(token);
                if (principal is not null)
                {
                    context.User = principal;
                }
            }

            await next();
        });
    }

    private static async Task<IResult> IssuePasswordTokenAsync(
        IFormCollection form,
        IServiceProvider services,
        CloudShellAuthenticationOptions options,
        BuiltInAuthorityTokenService authority)
    {
        if (!options.Mode.Equals("Identity", StringComparison.OrdinalIgnoreCase))
        {
            return InvalidGrant("The password grant is available only with Identity authentication.");
        }

        if (!ValidateClientForUserGrant(form, options))
        {
            return InvalidClient();
        }

        var userName = form["username"].ToString();
        var password = form["password"].ToString();
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(password))
        {
            return InvalidGrant("username and password are required.");
        }

        var userManager = services.GetRequiredService<UserManager<IdentityUser>>();
        var user =
            await userManager.FindByNameAsync(userName) ??
            await userManager.FindByEmailAsync(userName);
        if (user is null)
        {
            return InvalidGrant("The username or password is invalid.");
        }

        var signInManager = services.GetRequiredService<SignInManager<IdentityUser>>();
        var signInResult = await signInManager.CheckPasswordSignInAsync(
            user,
            password,
            lockoutOnFailure: true);
        if (!signInResult.Succeeded)
        {
            return InvalidGrant("The username or password is invalid.");
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Name, user.UserName ?? user.Email ?? user.Id)
        };
        if (!string.IsNullOrWhiteSpace(user.Email))
        {
            claims.Add(new Claim(ClaimTypes.Email, user.Email));
        }

        foreach (var role in await userManager.GetRolesAsync(user))
        {
            claims.Add(new Claim(options.RoleClaimType, role));
        }

        claims.AddRange(await userManager.GetClaimsAsync(user));
        var scopes = GetRequestedScopes(form, options.BuiltInAuthority.Clients
            .GetValueOrDefault(form["client_id"].ToString())?.Scopes);
        var token = authority.IssueToken(
            claims,
            options.BuiltInAuthority.Audience,
            scopes);
        return TokenResponse(token, scopes);
    }

    private static IResult IssueClientCredentialsToken(
        IFormCollection form,
        CloudShellAuthenticationOptions options,
        BuiltInAuthorityTokenService authority,
        BuiltInResourceIdentityRegistry resourceIdentityClients)
    {
        var clientId = form["client_id"].ToString();
        if (!TryGetValidClient(form, options, resourceIdentityClients, out var clientIdValue, out var client))
        {
            return InvalidClient();
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, clientIdValue),
            new(ClaimTypes.Name, clientIdValue)
        };
        claims.AddRange(client.Roles.Select(role => new Claim(options.RoleClaimType, role)));
        claims.AddRange(client.Permissions.Select(permission =>
            new Claim(CloudShellAuthenticationOptions.PermissionClaimType, permission)));
        claims.AddRange(client.ResourceGroups.Select(resourceGroup =>
            new Claim(CloudShellAuthenticationOptions.ResourceGroupClaimType, resourceGroup)));
        claims.AddRange(client.Resources.Select(resource =>
            new Claim(CloudShellAuthenticationOptions.ResourceClaimType, resource)));
        claims.AddRange(client.ResourcePermissions
            .Where(grant =>
                !string.IsNullOrWhiteSpace(grant.ResourceId) &&
                !string.IsNullOrWhiteSpace(grant.Permission))
            .Select(grant => new Claim(
                CloudShellAuthenticationOptions.ResourcePermissionClaimType,
                ResourcePermissionClaimAuthorization.CreateResourcePermissionClaimValue(
                    grant.ResourceId,
                    grant.Permission))));

        var scopes = GetRequestedScopes(form, client.Scopes);
        var token = authority.IssueToken(
            claims,
            options.BuiltInAuthority.Audience,
            scopes);
        return TokenResponse(token, scopes);
    }

    private static bool ValidateClientForUserGrant(
        IFormCollection form,
        CloudShellAuthenticationOptions options)
    {
        if (options.BuiltInAuthority.Clients.Count == 0)
        {
            return true;
        }

        return TryGetValidClient(form, options, null, out _, out _);
    }

    private static bool TryGetValidClient(
        IFormCollection form,
        CloudShellAuthenticationOptions options,
        BuiltInResourceIdentityRegistry? resourceIdentityClients,
        out string clientId,
        out BuiltInAuthorityClientOptions client)
    {
        clientId = form["client_id"].ToString();
        client = new BuiltInAuthorityClientOptions();
        if (string.IsNullOrWhiteSpace(clientId))
        {
            return false;
        }

        var hasClient = options.BuiltInAuthority.Clients.TryGetValue(
            clientId,
            out var configuredClient);
        if (!hasClient && resourceIdentityClients is not null)
        {
            hasClient = resourceIdentityClients.TryGetClient(clientId, out configuredClient!);
        }

        if (!hasClient ||
            configuredClient is null ||
            string.IsNullOrWhiteSpace(configuredClient.Secret))
        {
            return false;
        }

        var clientSecret = form["client_secret"].ToString();
        if (!FixedTimeEquals(clientSecret, configuredClient.Secret))
        {
            return false;
        }

        client = configuredClient;
        return true;
    }

    private static string[] GetRequestedScopes(
        IFormCollection form,
        string[]? allowedScopes)
    {
        allowedScopes ??= ["ControlPlane.Access"];
        var requested = form["scope"].ToString();
        if (string.IsNullOrWhiteSpace(requested))
        {
            return allowedScopes;
        }

        var allowed = allowedScopes.ToHashSet(StringComparer.Ordinal);
        return requested
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(allowed.Contains)
            .ToArray();
    }

    private static IResult TokenResponse(BuiltInToken token, string[] scopes) =>
        Results.Json(new
        {
            access_token = token.AccessToken,
            token_type = "Bearer",
            expires_in = Math.Max(0, (int)(token.ExpiresOn - DateTimeOffset.UtcNow).TotalSeconds),
            scope = string.Join(' ', scopes)
        });

    private static IResult InvalidClient() =>
        Results.Json(
            new { error = "invalid_client" },
            statusCode: StatusCodes.Status401Unauthorized);

    private static IResult InvalidGrant(string description) =>
        Results.BadRequest(new
        {
            error = "invalid_grant",
            error_description = description
        });

    private static string GetBaseUrl(HttpRequest request) =>
        $"{request.Scheme}://{request.Host}{request.PathBase}";

    private static bool FixedTimeEquals(string value, string expected)
    {
        var valueBytes = Encoding.UTF8.GetBytes(value);
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        return valueBytes.Length == expectedBytes.Length &&
            CryptographicOperations.FixedTimeEquals(valueBytes, expectedBytes);
    }
}
