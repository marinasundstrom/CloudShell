using CloudShell.Host.Components;
using CloudShell.Abstractions.Extensions;
using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.Host.ResourceManager;
using CloudShell.Host.Shell;
using Microsoft.FluentUI.AspNetCore.Components;
using CloudShell.Providers.Docker;
using CloudShell.Persistence;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddHttpClient();
builder.Services.AddFluentUIComponents();
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Warning);
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var dataDirectory = Path.Combine(builder.Environment.ContentRootPath, "Data");
Directory.CreateDirectory(dataDirectory);
builder.Services.AddCloudShellPersistence(
    $"Data Source={Path.Combine(dataDirectory, "cloudshell.db")}");

builder.Services
    .AddCloudShell()
    .AddExtension<CoreShellExtension>()
    .AddExtension<ResourceManagerExtension>()
    .AddExtension<ObservabilityExtension>()
    .AddDockerProvider();

builder.Services.AddSingleton<ShellCatalog>();
builder.Services.AddSingleton<IResourceManagerStore, ResourceManagerStore>();

var app = builder.Build();
app.Services.InitializeCloudShellDatabase();
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

app.UseAntiforgery();

app.MapStaticAssets();
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
