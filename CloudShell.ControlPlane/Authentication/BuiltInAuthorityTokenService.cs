using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace CloudShell.ControlPlane.Authentication;

public sealed class BuiltInAuthorityTokenService : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly CloudShellAuthenticationOptions options;
    private readonly RSA rsa;
    private readonly string keyId;

    public BuiltInAuthorityTokenService(IOptions<CloudShellAuthenticationOptions> options)
    {
        this.options = options.Value;
        rsa = RSA.Create();
        if (string.IsNullOrWhiteSpace(this.options.BuiltInAuthority.SigningKeyPem))
        {
            rsa.KeySize = 2048;
        }
        else
        {
            rsa.ImportFromPem(this.options.BuiltInAuthority.SigningKeyPem);
        }

        keyId = CreateKeyId(rsa);
    }

    public BuiltInToken IssueToken(
        IEnumerable<Claim> claims,
        string audience,
        IEnumerable<string> scopes)
    {
        var now = DateTimeOffset.UtcNow;
        var expiresOn = now.AddMinutes(Math.Max(1, options.BuiltInAuthority.AccessTokenMinutes));
        var header = new Dictionary<string, object?>
        {
            ["alg"] = "RS256",
            ["kid"] = keyId,
            ["typ"] = "JWT"
        };
        var payload = new Dictionary<string, object?>
        {
            ["iss"] = options.BuiltInAuthority.Issuer,
            ["aud"] = audience,
            ["iat"] = now.ToUnixTimeSeconds(),
            ["nbf"] = now.ToUnixTimeSeconds(),
            ["exp"] = expiresOn.ToUnixTimeSeconds(),
            ["jti"] = Guid.NewGuid().ToString("N")
        };

        foreach (var group in claims.GroupBy(claim => claim.Type))
        {
            var values = group.Select(claim => claim.Value).Distinct(StringComparer.Ordinal).ToArray();
            payload[group.Key] = values.Length == 1 ? values[0] : values;
        }

        var scope = string.Join(' ', scopes.Where(value => !string.IsNullOrWhiteSpace(value)));
        if (!string.IsNullOrWhiteSpace(scope))
        {
            payload["scope"] = scope;
        }

        var unsignedToken = $"{EncodeJson(header)}.{EncodeJson(payload)}";
        var signature = rsa.SignData(
            Encoding.ASCII.GetBytes(unsignedToken),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        return new BuiltInToken(
            $"{unsignedToken}.{Base64UrlEncode(signature)}",
            expiresOn);
    }

    public ClaimsPrincipal? ValidateToken(string token)
    {
        try
        {
            var parts = token.Split('.');
            if (parts.Length != 3)
            {
                return null;
            }

            var signedContent = $"{parts[0]}.{parts[1]}";
            var signature = Base64UrlDecode(parts[2]);
            if (!rsa.VerifyData(
                    Encoding.ASCII.GetBytes(signedContent),
                    signature,
                    HashAlgorithmName.SHA256,
                    RSASignaturePadding.Pkcs1))
            {
                return null;
            }

            using var headerDocument = JsonDocument.Parse(Base64UrlDecode(parts[0]));
            if (!headerDocument.RootElement.TryGetProperty("kid", out var tokenKeyId) ||
                !string.Equals(tokenKeyId.GetString(), keyId, StringComparison.Ordinal))
            {
                return null;
            }

            using var payloadDocument = JsonDocument.Parse(Base64UrlDecode(parts[1]));
            var payload = payloadDocument.RootElement;
            if (!IsValidPayload(payload))
            {
                return null;
            }

            var claims = new List<Claim>();
            foreach (var property in payload.EnumerateObject())
            {
                if (IsReservedClaim(property.Name))
                {
                    continue;
                }

                if (property.Value.ValueKind == JsonValueKind.Array)
                {
                    claims.AddRange(property.Value
                        .EnumerateArray()
                        .Where(value => value.ValueKind == JsonValueKind.String)
                        .Select(value => new Claim(property.Name, value.GetString()!)));
                    continue;
                }

                if (property.Value.ValueKind == JsonValueKind.String)
                {
                    claims.Add(new Claim(property.Name, property.Value.GetString()!));
                }
            }

            var identity = new ClaimsIdentity(
                claims,
                "CloudShell.BuiltInBearer",
                ClaimTypes.Name,
                options.RoleClaimType);
            return new ClaimsPrincipal(identity);
        }
        catch (Exception exception) when (
            exception is FormatException or
            JsonException or
            CryptographicException or
            InvalidOperationException)
        {
            return null;
        }
    }

    public object CreateDiscoveryDocument(string baseUrl)
    {
        var issuer = options.BuiltInAuthority.Issuer.TrimEnd('/');
        var endpointBase = baseUrl.TrimEnd('/');
        return new
        {
            issuer,
            token_endpoint = $"{endpointBase}/api/auth/v1/token",
            jwks_uri = $"{endpointBase}/api/auth/v1/jwks",
            response_types_supported = Array.Empty<string>(),
            grant_types_supported = new[] { "password", "client_credentials" },
            token_endpoint_auth_methods_supported = new[] { "client_secret_post" },
            subject_types_supported = new[] { "public" },
            id_token_signing_alg_values_supported = new[] { "RS256" },
            scopes_supported = new[] { "ControlPlane.Access" }
        };
    }

    public object CreateJsonWebKeySet()
    {
        var parameters = rsa.ExportParameters(false);
        return new
        {
            keys = new[]
            {
                new
                {
                    kty = "RSA",
                    use = "sig",
                    kid = keyId,
                    alg = "RS256",
                    n = Base64UrlEncode(parameters.Modulus!),
                    e = Base64UrlEncode(parameters.Exponent!)
                }
            }
        };
    }

    public void Dispose() => rsa.Dispose();

    private bool IsValidPayload(JsonElement payload)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (!payload.TryGetProperty("iss", out var issuer) ||
            !string.Equals(issuer.GetString(), options.BuiltInAuthority.Issuer, StringComparison.Ordinal))
        {
            return false;
        }

        if (!payload.TryGetProperty("aud", out var audience) ||
            !string.Equals(audience.GetString(), options.BuiltInAuthority.Audience, StringComparison.Ordinal))
        {
            return false;
        }

        if (!payload.TryGetProperty("nbf", out var notBefore) ||
            notBefore.GetInt64() > now)
        {
            return false;
        }

        return payload.TryGetProperty("exp", out var expires) &&
            expires.GetInt64() > now;
    }

    private static bool IsReservedClaim(string claimType) =>
        claimType is "iss" or "aud" or "iat" or "nbf" or "exp" or "jti" or "scope";

    private static string CreateKeyId(RSA rsa)
    {
        var parameters = rsa.ExportParameters(false);
        var hash = SHA256.HashData(parameters.Modulus!);
        return Base64UrlEncode(hash[..16]);
    }

    private static string EncodeJson(object value) =>
        Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions));

    private static string Base64UrlEncode(byte[] value) =>
        Convert.ToBase64String(value)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
        return Convert.FromBase64String(padded);
    }
}

public sealed record BuiltInToken(string AccessToken, DateTimeOffset ExpiresOn);
