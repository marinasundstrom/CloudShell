using CloudShell.Providers.Configuration;
using CloudShell.SecretsVaultService;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<SecretsVaultServiceOptions>(
    builder.Configuration.GetSection(SecretsVaultServiceOptions.SectionName));
builder.Services.AddSingleton<SecretsVaultServiceStore>();

var app = builder.Build();

app.MapGet("/healthz", () => Results.Ok(new
{
    status = "Healthy",
    service = "CloudShell Secrets Vault Service"
}))
.AllowAnonymous();

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
    if (string.IsNullOrWhiteSpace(GetAccessToken(request)))
    {
        return Unauthorized();
    }

    if (vault is null ||
        !store.IsAuthorized(vault, GetAccessToken(request)))
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
    if (string.IsNullOrWhiteSpace(GetAccessToken(request)))
    {
        return Unauthorized();
    }

    if (vault is null ||
        !store.IsAuthorized(vault, GetAccessToken(request)))
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

static string? GetAccessToken(HttpRequest request)
{
    var headerToken = request.Headers["X-CloudShell-Secrets-Token"].FirstOrDefault();
    if (!string.IsNullOrWhiteSpace(headerToken))
    {
        return headerToken;
    }

    var authorization = request.Headers.Authorization.FirstOrDefault();
    const string bearerPrefix = "Bearer ";
    return authorization?.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase) == true
        ? authorization[bearerPrefix.Length..].Trim()
        : null;
}

static IResult Unauthorized() =>
    Results.Problem(
        "A Secrets Vault service token is required.",
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
