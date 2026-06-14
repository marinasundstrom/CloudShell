using CloudShell.Abstractions.Authorization;
using CloudShell.ControlPlane.Authentication;
using CloudShell.Providers.Configuration;
using CloudShell.SecretsVaultService;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<SecretsVaultServiceOptions>(
    builder.Configuration.GetSection(SecretsVaultServiceOptions.SectionName));
builder.Services.Configure<CloudShellAuthenticationOptions>(
    builder.Configuration.GetSection(CloudShellAuthenticationOptions.SectionName));
builder.Services.AddSingleton<SecretsVaultServiceStore>();
builder.Services.AddSingleton<BuiltInAuthorityTokenService>();
builder.Services.AddSingleton<CloudShellBearerTokenValidationService>();

var app = builder.Build();

app.MapGet("/healthz", () => Results.Ok(new
{
    status = "Healthy",
    service = "CloudShell Secrets Vault Service"
}))
.AllowAnonymous();

app.UseCloudShellServiceBearerAuthentication();

var api = app
    .MapGroup("/api/secrets")
    .WithTags("Secrets");

api.MapGet("/secrets/{name}", GetSecretByQuery)
    .WithName("CloudShellSecretsVaultService_GetSecretByResourceId")
    .AllowAnonymous();

api.MapGet("/vaults/{vaultId}/secrets", ListSecrets)
    .WithName("CloudShellSecretsVaultService_ListSecrets")
    .AllowAnonymous();

api.MapGet("/vaults/{vaultId}/secrets/{name}", GetSecret)
    .WithName("CloudShellSecretsVaultService_GetSecret")
    .AllowAnonymous();

app.Run();

static IResult GetSecretByQuery(
    string resourceId,
    string name,
    string? version,
    HttpRequest request,
    SecretsVaultServiceStore store) =>
    GetSecret(resourceId, name, version, request, store);

static IResult ListSecrets(
    string vaultId,
    HttpRequest request,
    SecretsVaultServiceStore store)
{
    var vault = store.GetVault(vaultId);
    if (!HasBearerToken(request))
    {
        return Unauthorized();
    }

    if (vault is null ||
        !IsAuthorized(vault, request))
    {
        return NotFound();
    }

    return Results.Ok(vault.Secrets.Select(secret => new SecretMetadataResponse(
        secret.Name,
        secret.Version)).ToArray());
}

static IResult GetSecret(
    string vaultId,
    string name,
    string? version,
    HttpRequest request,
    SecretsVaultServiceStore store)
{
    var vault = store.GetVault(vaultId);
    if (!HasBearerToken(request))
    {
        return Unauthorized();
    }

    if (vault is null ||
        !IsAuthorized(vault, request))
    {
        return NotFound();
    }

    var candidates = vault.Secrets
        .Where(secret => string.Equals(secret.Name, name, StringComparison.OrdinalIgnoreCase))
        .Where(secret => string.IsNullOrWhiteSpace(version) ||
            string.Equals(secret.Version, version, StringComparison.OrdinalIgnoreCase))
        .ToArray();
    var secret = candidates.LastOrDefault();

    return secret is null
        ? NotFound()
        : Results.Ok(new SecretValueResponse(secret.Name, secret.Value, secret.Version));
}

static bool IsAuthorized(
    SecretsVaultDefinition vault,
    HttpRequest request) =>
    ResourcePermissionClaimAuthorization.HasResourcePermission(
        request.HttpContext.User,
        vault.Id,
        SecretsVaultResourceOperationPermissions.ReadSecrets);

static bool HasBearerToken(HttpRequest request)
{
    var authorization = request.Headers.Authorization.FirstOrDefault();
    const string bearerPrefix = "Bearer ";
    return authorization?.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase) == true &&
        !string.IsNullOrWhiteSpace(authorization[bearerPrefix.Length..]);
}

static IResult Unauthorized() =>
    Results.Problem(
        "A Secrets Vault bearer token is required.",
        statusCode: StatusCodes.Status401Unauthorized,
        title: "Unauthorized");

static IResult NotFound() =>
    Results.Problem(
        "The Secrets Vault or secret was not found.",
        statusCode: StatusCodes.Status404NotFound,
        title: "Not found");

public sealed record SecretMetadataResponse(
    string Name,
    string? Version);

public sealed record SecretValueResponse(
    string Name,
    string Value,
    string? Version);
