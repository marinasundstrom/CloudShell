using CloudShell.Abstractions.Authentication;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace CloudShell.ControlPlane.Client;

public abstract class ControlPlaneCredential
{
    public abstract ValueTask<ControlPlaneAuthenticationResult?> AuthenticateAsync(
        ControlPlaneAuthenticationContext context,
        CancellationToken cancellationToken = default);
}

public sealed record ControlPlaneAuthenticationContext(Uri Resource, IReadOnlyList<string> Scopes);

public sealed record ControlPlaneAuthenticationResult(
    IReadOnlyDictionary<string, string> Headers,
    DateTimeOffset? ExpiresOn = null);

public sealed class EmptyControlPlaneCredential : ControlPlaneCredential
{
    public override ValueTask<ControlPlaneAuthenticationResult?> AuthenticateAsync(
        ControlPlaneAuthenticationContext context,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<ControlPlaneAuthenticationResult?>(null);
}

public sealed class StaticBearerControlPlaneCredential(string token) : ControlPlaneCredential
{
    public override ValueTask<ControlPlaneAuthenticationResult?> AuthenticateAsync(
        ControlPlaneAuthenticationContext context,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<ControlPlaneAuthenticationResult?>(
            new ControlPlaneAuthenticationResult(
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Authorization"] = $"Bearer {token}"
                }));
}

public sealed class ClientCredentialsControlPlaneCredential(
    IHttpClientFactory httpClientFactory,
    IOptions<RemoteControlPlaneOptions> options) : ControlPlaneCredential
{
    private readonly SemaphoreSlim refreshLock = new(1, 1);
    private ControlPlaneAuthenticationResult? cachedResult;

    public override async ValueTask<ControlPlaneAuthenticationResult?> AuthenticateAsync(
        ControlPlaneAuthenticationContext context,
        CancellationToken cancellationToken = default)
    {
        if (cachedResult?.ExpiresOn is { } expiresOn &&
            expiresOn > DateTimeOffset.UtcNow.AddMinutes(1))
        {
            return cachedResult;
        }

        await refreshLock.WaitAsync(cancellationToken);
        try
        {
            if (cachedResult?.ExpiresOn is { } lockedExpiresOn &&
                lockedExpiresOn > DateTimeOffset.UtcNow.AddMinutes(1))
            {
                return cachedResult;
            }

            var credential = options.Value.Credential;
            if (string.IsNullOrWhiteSpace(credential.ClientId) ||
                string.IsNullOrWhiteSpace(credential.ClientSecret))
            {
                throw new InvalidOperationException(
                    "CloudShell:ControlPlane:Credential:ClientId and ClientSecret are required.");
            }

            var tokenEndpoint = credential.TokenEndpoint ??
                new Uri(context.Resource, "/api/auth/v1/token");
            using var request = new HttpRequestMessage(HttpMethod.Post, tokenEndpoint)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials",
                    ["client_id"] = credential.ClientId,
                    ["client_secret"] = credential.ClientSecret,
                    ["scope"] = string.Join(' ', context.Scopes)
                })
            };

            var client = httpClientFactory.CreateClient("CloudShell.ControlPlane.Auth");
            using var response = await client.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            var token = await response.Content
                .ReadFromJsonAsync<TokenResponse>(cancellationToken: cancellationToken)
                ?? throw new InvalidOperationException("The Control Plane token endpoint returned an empty response.");
            if (string.IsNullOrWhiteSpace(token.AccessToken))
            {
                throw new InvalidOperationException("The Control Plane token endpoint returned no access token.");
            }

            cachedResult = new ControlPlaneAuthenticationResult(
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Authorization"] = $"Bearer {token.AccessToken}"
                },
                DateTimeOffset.UtcNow.AddSeconds(Math.Max(0, token.ExpiresIn)));
            return cachedResult;
        }
        finally
        {
            refreshLock.Release();
        }
    }

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("token_type")] string TokenType,
        [property: JsonPropertyName("expires_in")] int ExpiresIn,
        [property: JsonPropertyName("scope")] string? Scope);
}

/// <summary>
/// Adapts a CloudShell resource identity credential for Control Plane API calls.
/// </summary>
/// <remarks>
/// Public preview API. This lets authored services use the same
/// <see cref="CloudShellResourceCredential"/> chain for domain-shaped
/// Control Plane clients that they use for other CloudShell-protected services.
/// </remarks>
public sealed class CloudShellResourceControlPlaneCredential(
    CloudShellResourceCredential credential) : ControlPlaneCredential
{
    private readonly SemaphoreSlim refreshLock = new(1, 1);
    private ControlPlaneAuthenticationResult? cachedResult;

    public override async ValueTask<ControlPlaneAuthenticationResult?> AuthenticateAsync(
        ControlPlaneAuthenticationContext context,
        CancellationToken cancellationToken = default)
    {
        if (cachedResult?.ExpiresOn is { } expiresOn &&
            expiresOn > DateTimeOffset.UtcNow.AddMinutes(1))
        {
            return cachedResult;
        }

        await refreshLock.WaitAsync(cancellationToken);
        try
        {
            if (cachedResult?.ExpiresOn is { } lockedExpiresOn &&
                lockedExpiresOn > DateTimeOffset.UtcNow.AddMinutes(1))
            {
                return cachedResult;
            }

            var token = await credential.GetTokenAsync(
                new CloudShellResourceTokenRequest(context.Scopes),
                cancellationToken);
            if (string.IsNullOrWhiteSpace(token.Token))
            {
                throw new InvalidOperationException(
                    "The CloudShell resource credential returned no access token.");
            }

            cachedResult = new ControlPlaneAuthenticationResult(
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Authorization"] = $"Bearer {token.Token}"
                },
                token.ExpiresOn);
            return cachedResult;
        }
        finally
        {
            refreshLock.Release();
        }
    }
}

public sealed class ControlPlaneAuthenticationHandler(
    ControlPlaneCredential credential,
    IOptions<RemoteControlPlaneOptions> options) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var baseAddress = options.Value.BaseAddress ??
            request.RequestUri ??
            throw new InvalidOperationException(
                $"{RemoteControlPlaneOptions.SectionName}:BaseAddress must be configured.");
        var result = await credential.AuthenticateAsync(
            new ControlPlaneAuthenticationContext(
                baseAddress,
                options.Value.Credential.Scopes),
            cancellationToken);
        if (result is not null)
        {
            foreach (var header in result.Headers)
            {
                if (string.Equals(header.Key, "Authorization", StringComparison.OrdinalIgnoreCase))
                {
                    request.Headers.Authorization = AuthenticationHeaderValue.Parse(header.Value);
                    continue;
                }

                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
