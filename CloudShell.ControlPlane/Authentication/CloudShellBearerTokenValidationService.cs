using System.Collections.Concurrent;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace CloudShell.ControlPlane.Authentication;

public sealed class CloudShellBearerTokenValidationService(
    IOptions<CloudShellAuthenticationOptions> options,
    IServiceProvider services)
{
    private readonly ConcurrentDictionary<string, IConfigurationManager<OpenIdConnectConfiguration>> configurationManagers =
        new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SecurityKey> signingKeys = new(StringComparer.Ordinal);

    public async ValueTask<ClaimsPrincipal?> ValidateTokenAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var configuredOptions = options.Value;
        if (configuredOptions.BuiltInAuthority.Enabled &&
            services.GetService(typeof(BuiltInAuthorityTokenService)) is BuiltInAuthorityTokenService builtInAuthority)
        {
            var principal = builtInAuthority.ValidateToken(token);
            if (principal is not null)
            {
                return principal;
            }
        }

        return await ValidateServiceBearerTokenAsync(
            token,
            configuredOptions,
            cancellationToken);
    }

    private async ValueTask<ClaimsPrincipal?> ValidateServiceBearerTokenAsync(
        string token,
        CloudShellAuthenticationOptions configuredOptions,
        CancellationToken cancellationToken)
    {
        var serviceBearer = configuredOptions.ServiceBearer;
        if (!serviceBearer.Enabled)
        {
            return null;
        }

        var configuration = await GetOpenIdConnectConfigurationAsync(serviceBearer, cancellationToken);
        var issuer = string.IsNullOrWhiteSpace(serviceBearer.Issuer)
            ? configuration?.Issuer
            : serviceBearer.Issuer;
        var signingKey = GetConfiguredSigningKey(serviceBearer);
        var signingKeysToValidate = configuration?.SigningKeys ?? [];
        if (signingKey is not null)
        {
            signingKeysToValidate = [signingKey, .. signingKeysToValidate];
        }

        if (signingKeysToValidate.Count == 0)
        {
            return null;
        }

        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = signingKeysToValidate,
            ValidateIssuer = !string.IsNullOrWhiteSpace(issuer),
            ValidIssuer = issuer,
            ValidateAudience = !string.IsNullOrWhiteSpace(serviceBearer.Audience),
            ValidAudience = serviceBearer.Audience,
            ValidateLifetime = true,
            TryAllIssuerSigningKeys = true,
            ClockSkew = TimeSpan.FromMinutes(2),
            NameClaimType = ClaimTypes.Name,
            RoleClaimType = configuredOptions.RoleClaimType
        };

        var handler = new JsonWebTokenHandler
        {
            MapInboundClaims = false
        };
        var result = await handler.ValidateTokenAsync(token, validationParameters);
        if (!result.IsValid || result.ClaimsIdentity is null)
        {
            return null;
        }

        var identity = new ClaimsIdentity(
            result.ClaimsIdentity.Claims,
            "CloudShell.ServiceBearer",
            ClaimTypes.Name,
            configuredOptions.RoleClaimType);
        return new ClaimsPrincipal(identity);
    }

    private async ValueTask<OpenIdConnectConfiguration?> GetOpenIdConnectConfigurationAsync(
        ServiceBearerAuthenticationOptions serviceBearer,
        CancellationToken cancellationToken)
    {
        var metadataAddress = ResolveMetadataAddress(serviceBearer);
        if (string.IsNullOrWhiteSpace(metadataAddress))
        {
            return null;
        }

        var manager = configurationManagers.GetOrAdd(
            $"{metadataAddress}\n{serviceBearer.RequireHttpsMetadata}",
            static (_, state) => new ConfigurationManager<OpenIdConnectConfiguration>(
                state.metadataAddress,
                new OpenIdConnectConfigurationRetriever(),
                new HttpDocumentRetriever
                {
                    RequireHttps = state.requireHttps
                }),
            (metadataAddress, requireHttps: serviceBearer.RequireHttpsMetadata));

        try
        {
            return await manager.GetConfigurationAsync(cancellationToken);
        }
        catch (Exception exception) when (
            exception is IOException or
            HttpRequestException or
            InvalidOperationException)
        {
            return null;
        }
    }

    private SecurityKey? GetConfiguredSigningKey(ServiceBearerAuthenticationOptions serviceBearer)
    {
        if (string.IsNullOrWhiteSpace(serviceBearer.SigningKeyPem))
        {
            return null;
        }

        return signingKeys.GetOrAdd(serviceBearer.SigningKeyPem, static pem =>
        {
            var rsa = RSA.Create();
            rsa.ImportFromPem(pem);
            return new RsaSecurityKey(rsa)
            {
                KeyId = CreateKeyId(rsa)
            };
        });
    }

    private static string? ResolveMetadataAddress(ServiceBearerAuthenticationOptions serviceBearer)
    {
        if (!string.IsNullOrWhiteSpace(serviceBearer.MetadataAddress))
        {
            return serviceBearer.MetadataAddress.Trim();
        }

        return string.IsNullOrWhiteSpace(serviceBearer.Authority)
            ? null
            : $"{serviceBearer.Authority.TrimEnd('/')}/.well-known/openid-configuration";
    }

    private static string CreateKeyId(RSA rsa)
    {
        var parameters = rsa.ExportParameters(false);
        var hash = SHA256.HashData(parameters.Modulus!);
        return Base64UrlEncode(hash[..16]);
    }

    private static string Base64UrlEncode(byte[] value) =>
        Convert.ToBase64String(value)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
