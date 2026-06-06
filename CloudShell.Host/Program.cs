using CloudShell.Host.Components;
using CloudShell.Abstractions.Extensions;
using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.Host.Authentication;
using CloudShell.Abstractions.Logs;
using CloudShell.Host.Logs;
using CloudShell.Host.ResourceManager;
using CloudShell.Host.Shell;
using Microsoft.FluentUI.AspNetCore.Components;
using CloudShell.Providers.Docker;
using CloudShell.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddHttpClient();
builder.Services.AddFluentUIComponents();
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Warning);
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

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

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

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
    .AllowAnonymous();
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
