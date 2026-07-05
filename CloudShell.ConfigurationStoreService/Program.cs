using CloudShell.Abstractions.Authorization;
using CloudShell.ConfigurationStoreService;
using CloudShell.ControlPlane.Authentication;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<ConfigurationStoreServiceOptions>(
    builder.Configuration.GetSection(ConfigurationStoreServiceOptions.SectionName));
builder.Services.Configure<CloudShellAuthenticationOptions>(
    builder.Configuration.GetSection(CloudShellAuthenticationOptions.SectionName));
builder.Services.AddSingleton<ConfigurationStoreServiceStore>();
builder.Services.AddSingleton<BuiltInAuthorityTokenService>();
builder.Services.AddSingleton<CloudShellBearerTokenValidationService>();

var app = builder.Build();

app.MapGet("/healthz", () => Results.Ok(new
{
    status = "Healthy",
    service = "CloudShell Configuration Store Service"
}))
.AllowAnonymous();

app.UseCloudShellServiceBearerAuthentication();

var api = app
    .MapGroup("/api/configuration")
    .WithTags("Configuration");

api.MapGet("/settings", ListSettingsByQuery)
    .WithName("CloudShellConfigurationStoreService_ListSettingsByResourceId")
    .AllowAnonymous();

api.MapGet("/settings/{name}", GetSettingByQuery)
    .WithName("CloudShellConfigurationStoreService_GetSettingByResourceId")
    .AllowAnonymous();

api.MapGet("/stores/{storeId}/settings", ListSettings)
    .WithName("CloudShellConfigurationStoreService_ListSettings")
    .AllowAnonymous();

api.MapGet("/stores/{storeId}/settings/{name}", GetSetting)
    .WithName("CloudShellConfigurationStoreService_GetSetting")
    .AllowAnonymous();

app.Run();

static IResult ListSettingsByQuery(
    string resourceId,
    HttpRequest request,
    ConfigurationStoreServiceStore store) =>
    ListSettings(resourceId, request, store);

static IResult GetSettingByQuery(
    string resourceId,
    string name,
    HttpRequest request,
    ConfigurationStoreServiceStore store) =>
    GetSetting(resourceId, name, request, store);

static IResult ListSettings(
    string storeId,
    HttpRequest request,
    ConfigurationStoreServiceStore store)
{
    var configurationStore = store.GetStore(storeId);
    if (!HasBearerToken(request))
    {
        return Unauthorized();
    }

    if (configurationStore is null ||
        !IsAuthorized(configurationStore, request))
    {
        return NotFound();
    }

    return Results.Ok(configurationStore.Settings.Select(ToResponse).ToArray());
}

static IResult GetSetting(
    string storeId,
    string name,
    HttpRequest request,
    ConfigurationStoreServiceStore store)
{
    var configurationStore = store.GetStore(storeId);
    if (!HasBearerToken(request))
    {
        return Unauthorized();
    }

    if (configurationStore is null ||
        !IsAuthorized(configurationStore, request))
    {
        return NotFound();
    }

    var setting = configurationStore.Settings.FirstOrDefault(item =>
        string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));

    return setting is null
        ? NotFound()
        : Results.Ok(ToResponse(setting));
}

static ConfigurationSettingResponse ToResponse(ConfigurationSetting setting) =>
    new(setting.Name, setting.Value);

static bool IsAuthorized(
    ConfigurationStoreDefinition configurationStore,
    HttpRequest request) =>
    ResourcePermissionClaimAuthorization.HasResourcePermission(
        request.HttpContext.User,
        configurationStore.Id,
        ConfigurationStoreResourceOperationPermissions.ReadSettings);

static bool HasBearerToken(HttpRequest request)
{
    var authorization = request.Headers.Authorization.FirstOrDefault();
    const string bearerPrefix = "Bearer ";
    return authorization?.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase) == true &&
        !string.IsNullOrWhiteSpace(authorization[bearerPrefix.Length..]);
}

static IResult Unauthorized() =>
    Results.Problem(
        "A configuration bearer token is required.",
        statusCode: StatusCodes.Status401Unauthorized,
        title: "Unauthorized");

static IResult NotFound() =>
    Results.Problem(
        "The Configuration Store or setting was not found.",
        statusCode: StatusCodes.Status404NotFound,
        title: "Not found");
