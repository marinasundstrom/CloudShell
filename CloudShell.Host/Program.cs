using CloudShell.Host.Components;
using CloudShell.Abstractions.Extensions;
using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.Api;
using CloudShell.ControlPlane.Authentication;
using CloudShell.Abstractions.Logs;
using CloudShell.ControlPlane.Logs;
using CloudShell.Host.Localization;
using CloudShell.ControlPlane.ResourceManager;
using CloudShell.Host.ResourceManager;
using CloudShell.Host.Shell;
using Microsoft.AspNetCore.Localization;
using Microsoft.FluentUI.AspNetCore.Components;
using CloudShell.Providers.Applications;
using CloudShell.Providers.Docker;
using CloudShell.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using System.Globalization;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddHttpClient();
builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");
builder.Services.AddFluentUIComponents();
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Warning);
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddCloudShellControlPlaneOpenApi();

var localizationOptions = builder.Configuration
    .GetSection(CloudShellLocalizationOptions.SectionName)
    .Get<CloudShellLocalizationOptions>()
    ?? new CloudShellLocalizationOptions();
var supportedCultures = GetSupportedCultures(localizationOptions);
builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    options.DefaultRequestCulture = new RequestCulture(
        GetDefaultCulture(localizationOptions, supportedCultures));
    options.SupportedCultures = supportedCultures;
    options.SupportedUICultures = supportedCultures;
});
builder.Services.Configure<CloudShellLocalizationOptions>(
    builder.Configuration.GetSection(CloudShellLocalizationOptions.SectionName));

var persistenceOptions = builder.Configuration
    .GetSection(CloudShellPersistenceOptions.SectionName)
    .Get<CloudShellPersistenceOptions>()
    ?? new CloudShellPersistenceOptions();
ResolveSqlitePaths(persistenceOptions, builder.Environment.ContentRootPath);
builder.Services.AddCloudShellPersistence(persistenceOptions);
var authenticationOptions =
    builder.Services.AddCloudShellAuthentication(builder.Configuration);

builder.Services
    .AddCloudShell()
    .AddExtension<CoreShellExtension>()
    .AddExtension<ResourceManagerExtension>()
    .AddExtension<ObservabilityExtension>()
    .AddApplicationProvider(options =>
    {
        var sampleProjectPath = Path.GetFullPath(
            "../samples/CloudShell.ExampleWebApi/CloudShell.ExampleWebApi.csproj",
            builder.Environment.ContentRootPath);

        options.InitialApplications.Add(new ApplicationResourceDefinition(
            "application:example-web-api",
            "Example Web API",
            "dotnet",
            $"run --project \"{sampleProjectPath}\" --no-launch-profile",
            Path.GetDirectoryName(sampleProjectPath),
            "http://localhost:5127",
            [
                new("ASPNETCORE_URLS", "http://localhost:5127"),
                new("CLOUDSHELL_APPLICATION", "Example Web API")
            ]));
    })
    .AddDockerProvider();

builder.Services.AddSingleton<ShellCatalog>();
builder.Services.AddScoped<IResourceGroupStore, AuthorizedResourceGroupStore>();
builder.Services.AddScoped<IResourceRegistrationStore, AuthorizedResourceRegistrationStore>();
builder.Services.AddScoped<IResourceManagerStore, ResourceManagerStore>();
builder.Services.AddScoped<ILogStore, LogStore>();

var app = builder.Build();
var usesLocalIdentity =
    authenticationOptions.Enabled &&
    authenticationOptions.Mode.Equals("Identity", StringComparison.OrdinalIgnoreCase);
app.Services.InitializeCloudShellDatabase(usesLocalIdentity);
if (usesLocalIdentity)
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider
        .GetRequiredService<CloudShellIdentitySeeder>()
        .SeedAsync();
}

var extensionRegistry = app.Services.GetRequiredService<CloudShellExtensionRegistry>();
extensionRegistry.Validate();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseRequestLocalization();

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapCloudShellControlPlaneOpenApi();
app.MapCloudShellControlPlaneApi();

app.MapGet("/localization/set", (
    string culture,
    string? returnUrl,
    HttpContext httpContext,
    IOptions<RequestLocalizationOptions> options) =>
{
    var supported = options.Value.SupportedUICultures ?? [];
    if (supported.Any(supportedCulture =>
            supportedCulture.Name.Equals(culture, StringComparison.OrdinalIgnoreCase)))
    {
        httpContext.Response.Cookies.Append(
            CookieRequestCultureProvider.DefaultCookieName,
            CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture)),
            new CookieOptions
            {
                Expires = DateTimeOffset.UtcNow.AddYears(1),
                IsEssential = true,
                SameSite = SameSiteMode.Lax,
                Secure = httpContext.Request.IsHttps
            });
    }

    return Results.LocalRedirect(IsLocalReturnUrl(returnUrl) ? returnUrl! : "/");
})
.AllowAnonymous()
.ExcludeFromDescription();

if (authenticationOptions.Enabled &&
    (authenticationOptions.Mode.Equals("OpenIdConnect", StringComparison.OrdinalIgnoreCase) ||
     authenticationOptions.Mode.Equals("External", StringComparison.OrdinalIgnoreCase)))
{
    app.MapGet("/account/challenge", (
        string? returnUrl,
        IOptions<CloudShellAuthenticationOptions> configuredOptions) =>
    {
        var options = configuredOptions.Value;
        var redirectUri = IsLocalReturnUrl(returnUrl) ? returnUrl! : "/";
        return Results.Challenge(
            new Microsoft.AspNetCore.Authentication.AuthenticationProperties
            {
                RedirectUri = redirectUri
            },
            [options.ChallengeScheme]);
    })
    .AllowAnonymous()
    .ExcludeFromDescription();
}

app.MapStaticAssets().AllowAnonymous();
var razorComponents = app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

var extensionAssemblies = extensionRegistry.ViewAssemblies
    .Where(assembly => assembly != typeof(Program).Assembly)
    .ToArray();

if (extensionAssemblies.Length > 0)
{
    razorComponents.AddAdditionalAssemblies(extensionAssemblies);
}

app.Run();

static void ResolveSqlitePaths(
    CloudShellPersistenceOptions options,
    string contentRootPath)
{
    if (!options.Provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
    {
        return;
    }

    options.ConnectionString = ResolveSqlitePath(
        options.ConnectionString,
        contentRootPath);
    options.IdentityConnectionString = ResolveSqlitePath(
        options.IdentityConnectionString,
        contentRootPath);
}

static string ResolveSqlitePath(string connectionString, string contentRootPath)
{
    var builder = new SqliteConnectionStringBuilder(connectionString);
    if (string.IsNullOrWhiteSpace(builder.DataSource) ||
        builder.DataSource.Equals(":memory:", StringComparison.OrdinalIgnoreCase) ||
        Path.IsPathRooted(builder.DataSource))
    {
        return connectionString;
    }

    var fullPath = Path.GetFullPath(builder.DataSource, contentRootPath);
    Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
    builder.DataSource = fullPath;
    return builder.ConnectionString;
}

static bool IsLocalReturnUrl(string? returnUrl) =>
    !string.IsNullOrWhiteSpace(returnUrl) &&
    returnUrl.StartsWith('/') &&
    !returnUrl.StartsWith("//", StringComparison.Ordinal);

static List<CultureInfo> GetSupportedCultures(CloudShellLocalizationOptions options)
{
    var cultures = options.SupportedCultures
        .Append(options.DefaultCulture)
        .Select(CreateCulture)
        .OfType<CultureInfo>()
        .DistinctBy(culture => culture.Name)
        .ToList();

    return cultures.Count == 0
        ? [CultureInfo.GetCultureInfo("en")]
        : cultures;
}

static CultureInfo GetDefaultCulture(
    CloudShellLocalizationOptions options,
    IReadOnlyList<CultureInfo> supportedCultures)
{
    var defaultCulture = CreateCulture(options.DefaultCulture);
    if (defaultCulture is not null &&
        supportedCultures.Any(culture =>
            culture.Name.Equals(defaultCulture.Name, StringComparison.OrdinalIgnoreCase)))
    {
        return defaultCulture;
    }

    return supportedCultures[0];
}

static CultureInfo? CreateCulture(string cultureName)
{
    try
    {
        return CultureInfo.GetCultureInfo(cultureName);
    }
    catch (CultureNotFoundException)
    {
        return null;
    }
}
