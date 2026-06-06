using CloudShell.ConfigurationService;
using CloudShell.Providers.Configuration;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<ConfigurationServiceOptions>(
    builder.Configuration.GetSection(ConfigurationServiceOptions.SectionName));
builder.Services.AddSingleton<ConfigurationServiceStore>();

var app = builder.Build();

app.MapGet("/healthz", () => Results.Ok(new
{
    status = "Healthy",
    service = "CloudShell Configuration Service"
}))
.AllowAnonymous();

var api = app
    .MapGroup("/api/configuration")
    .WithTags("Configuration");

api.MapGet("/entries", ListEntriesByQuery)
    .WithName("CloudShellConfigurationService_ListEntriesByResourceId")
    .AllowAnonymous();

api.MapGet("/entries/{name}", GetEntryByQuery)
    .WithName("CloudShellConfigurationService_GetEntryByResourceId")
    .AllowAnonymous();

api.MapGet("/stores/{storeId}/entries", ListEntries)
    .WithName("CloudShellConfigurationService_ListEntries")
    .AllowAnonymous();

api.MapGet("/stores/{storeId}/entries/{name}", GetEntry)
    .WithName("CloudShellConfigurationService_GetEntry")
    .AllowAnonymous();

app.Run();

static IResult ListEntriesByQuery(
    string resourceId,
    HttpRequest request,
    ConfigurationServiceStore store) =>
    ListEntries(resourceId, request, store);

static IResult GetEntryByQuery(
    string resourceId,
    string name,
    HttpRequest request,
    ConfigurationServiceStore store) =>
    GetEntry(resourceId, name, request, store);

static IResult ListEntries(
    string storeId,
    HttpRequest request,
    ConfigurationServiceStore store)
{
    var configurationStore = store.GetStore(storeId);
    if (string.IsNullOrWhiteSpace(GetAccessToken(request)))
    {
        return Unauthorized();
    }

    if (configurationStore is null ||
        !store.IsAuthorized(configurationStore, GetAccessToken(request)))
    {
        return NotFound();
    }

    return Results.Ok(configurationStore.Entries.Select(ToResponse).ToArray());
}

static IResult GetEntry(
    string storeId,
    string name,
    HttpRequest request,
    ConfigurationServiceStore store)
{
    var configurationStore = store.GetStore(storeId);
    if (string.IsNullOrWhiteSpace(GetAccessToken(request)))
    {
        return Unauthorized();
    }

    if (configurationStore is null ||
        !store.IsAuthorized(configurationStore, GetAccessToken(request)))
    {
        return NotFound();
    }

    var entry = configurationStore.Entries.FirstOrDefault(item =>
        string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));

    return entry is null
        ? NotFound()
        : Results.Ok(ToResponse(entry));
}

static ConfigurationEntryResponse ToResponse(ConfigurationEntry entry) =>
    new(entry.Name, entry.Value, entry.IsSecret);

static string? GetAccessToken(HttpRequest request)
{
    var headerToken = request.Headers["X-CloudShell-Configuration-Token"].FirstOrDefault();
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
        "A configuration service token is required.",
        statusCode: StatusCodes.Status401Unauthorized,
        title: "Unauthorized");

static IResult NotFound() =>
    Results.Problem(
        "The configuration service or entry was not found.",
        statusCode: StatusCodes.Status404NotFound,
        title: "Not found");
