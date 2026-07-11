using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.Authorization;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.Hosting;
using CloudShell.Hosting;
using CloudShell.Hosting.Components;
using CloudShell.Hosting.ResourceManager;
using CloudShell.Hosting.Shell;
using CloudShell.ResourceModel;
using CloudShell.ControlPlane.Providers;
using CloudShell.ControlPlane.Providers.UI;
using CloudShell.ControlPlane.ResourceModel;
using CloudShell.ThirdPartyIdentity;
using Microsoft.Extensions.DependencyInjection.Extensions;
using GraphResourceState = CloudShell.ResourceModel.ResourceState;

var builder = CloudShellApplication.CreateBuilder(args);
var repositoryRootPath = FindRepositoryRoot(builder.Environment.ContentRootPath);
var sampleRootPath = Path.Combine(repositoryRootPath, "samples", "ThirdPartyIdentity");
var configurationStoreServiceProjectPath = Path.Combine(
    repositoryRootPath,
    "CloudShell.ConfigurationStoreService",
    "CloudShell.ConfigurationStoreService.csproj");
var apiProjectPath = Path.Combine(
    sampleRootPath,
    "Api",
    "CloudShell.ThirdPartyIdentity.Api.csproj");
var authority = builder.Configuration["Authentication:OpenIdConnect:Authority"] ??
    "http://localhost:8080/realms/cloudshell";
var clientId = builder.Configuration["Authentication:OpenIdConnect:ClientId"] ??
    "cloudshell-ui";
var configurationServiceBasePort = builder.Configuration.GetValue<int?>(
    "Samples:ThirdPartyIdentity:ConfigurationServiceBasePort") ?? 5138;
var configurationServiceEndpoint =
    builder.Configuration["Samples:ThirdPartyIdentity:ConfigurationServiceEndpoint"] ??
    $"http://localhost:{configurationServiceBasePort}";
var apiEndpoint = builder.Configuration["Samples:ThirdPartyIdentity:ApiEndpoint"] ??
    "http://localhost:5235";
var apiEndpointUri = new Uri(apiEndpoint);
const string identityProviderId = "identity:keycloak";
const string apiIdentityName = "keycloak-provisioned-api";

string configurationResourceId = string.Empty;
string identityProvisioningResourceId = string.Empty;
var cloudShell = builder.AddCloudShellControlPlaneApplication(
    configureBuiltInResourceModelProviders: null,
    configureControlPlane: controlPlane =>
    {
        controlPlane.DefineResources(
            resources =>
            {
                var identityProvisioningResource = resources
                    .AddIdentityProvisioning("keycloak")
                    .WithDisplayName("Keycloak Identity Provisioning")
                    .WithIdentityProvider("Keycloak")
                    .WithIdentityProviderId(identityProviderId)
                    .WithProviderKind("oidc");
                identityProvisioningResourceId = identityProvisioningResource.EffectiveResourceId;
                var configurationResource = resources
                    .AddConfigurationStore("third-party-identity")
                    .WithDisplayName("Third-party Identity Settings")
                    .WithEndpoint(configurationServiceEndpoint);
                configurationResourceId = configurationResource.EffectiveResourceId;
                var apiResource = resources
                    .AddDotnetApp("keycloak-provisioned-api", apiProjectPath)
                    .WithDisplayName("Keycloak Provisioned API")
                    .DependsOn(configurationResource)
                    .WithReference(configurationResource)
                    .UseLaunchSettings(false)
                    .WithHotReload(false)
                    .WithHttpEndpoint(
                        host: apiEndpointUri.Host,
                        port: apiEndpointUri.Port)
                    .WithIdentity(
                        identityProviderId,
                        scopes: [builder.Configuration["Keycloak:ResourceIdentityScope"] ?? "openid"],
                        name: apiIdentityName)
                    .ProvisionIdentityOnStartup()
                    .WithEnvironmentVariable(
                        "CLOUDSHELL_APPLICATION",
                        "Keycloak Provisioned API")
                    .WithEnvironmentVariable(
                        "CLOUDSHELL_CONFIGURATION_SERVICE_NAME",
                        "third-party-identity");
                configurationResource.Allow(apiResource, ConfigurationStoreResourceOperationPermissions.ReadSettings);
            },
            projectState: AddProjectionState);
        controlPlane.AddIdentityProvider(CreateKeycloakIdentityProviderDefinition(
            identityProviderId,
            "Keycloak",
            identityProvisioningResourceId));
    });
cloudShell
    .UseConfigurationStoreResourceProvider(runtime =>
    {
        runtime.ServiceProjectPath = configurationStoreServiceProjectPath;
        runtime.ServiceWorkingDirectory = repositoryRootPath;
        runtime.ServiceBearerAuthority = authority;
        runtime.ServiceBearerIssuer = authority;
        runtime.ServiceBearerRequireHttpsMetadata =
            builder.Configuration.GetValue("Authentication:OpenIdConnect:RequireHttpsMetadata", true);
        runtime.Settings.Add(new(
            "Sample:Message",
            "Hello from a Keycloak-provisioned resource identity"));
    });

builder.AddCloudShellUi(ui =>
{
    ui
        .AddExtension<ResourceManagerExtension>()
        .AddExtension<TelemetryExtension>()
        .AddExtension<UsageExtension>();
    ui.AddBuiltInProviderResourceManagerUi();
});
builder.Services.TryAddEnumerable(
    ServiceDescriptor.Singleton<IResourceIdentityProvisioner, KeycloakResourceIdentityProvisioner>());
builder.Services.TryAddEnumerable(
    ServiceDescriptor.Singleton<IResourceIdentityProvisioningStatusProvider, KeycloakResourceIdentityProvisioner>());
builder.Services.TryAddEnumerable(
    ServiceDescriptor.Singleton<IResourceIdentityProviderSetupHandler, KeycloakResourceIdentityProvisioner>());
builder.Services.TryAddEnumerable(
    ServiceDescriptor.Singleton<IResourceIdentityCredentialEnvironmentProvider, KeycloakResourceIdentityProvisioner>());
builder.Services.AddSingleton<
    IThirdPartyIdentityResourceModelSetupBridge,
    ThirdPartyIdentityResourceManagerIdentitySetupBridge>();
builder.Services.AddSingleton<IIdentityProvisioningSetupHandler, ThirdPartyIdentityResourceModelSetupHandler>();
builder.Services.TryAddEnumerable(
    ServiceDescriptor.Singleton<
        IAspNetCoreProjectRuntimeEnvironmentProvider,
        ThirdPartyIdentityAspNetCoreProjectIdentityEnvironmentProvider>());

var app = builder.Build();

await app.UseCloudShellControlPlaneAsync();
await app.UseCloudShellUiAsync();
app.MapCloudShellControlPlane();
app.MapCloudShellUi<App>();

app.Run();

static string FindRepositoryRoot(string startPath)
{
    var directory = new DirectoryInfo(startPath);
    while (directory is not null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "CloudShell.slnx")))
        {
            return directory.FullName;
        }

        directory = directory.Parent;
    }

    return Path.GetFullPath("../..", startPath);
}

ResourceIdentityProviderDefinition CreateKeycloakIdentityProviderDefinition(
    string id,
    string name,
    string? provisioningResourceId = null) =>
    new(
        id,
        name,
        ResourceIdentityProviderKind.Oidc,
        CreateKeycloakIdentityProviderSettings(),
        provisioningResourceId);

Dictionary<string, string> CreateKeycloakIdentityProviderSettings() =>
    new(StringComparer.OrdinalIgnoreCase)
    {
        ["Provider"] = "Keycloak",
        ["Authority"] = authority,
        ["ClientId"] = clientId,
        ["RoleClaimType"] = builder.Configuration["Authentication:RoleClaimType"] ?? "roles",
        ["TokenEndpoint"] = builder.Configuration["Keycloak:TokenEndpoint"] ??
            $"{authority.TrimEnd('/')}/protocol/openid-connect/token",
        ["Realm"] = builder.Configuration["Keycloak:Realm"] ?? "cloudshell",
        ["AdminBaseAddress"] = builder.Configuration["Keycloak:AdminBaseAddress"] ??
            "http://localhost:8080"
    };

GraphResourceState AddProjectionState(GraphResourceState state)
{
    if (state.EffectiveResourceId != configurationResourceId)
    {
        return state;
    }

    var attributes = state.ResourceAttributeValues.ToDictionary();
    attributes[ConfigurationStoreResourceTypeProvider.Attributes.SettingCount] = 1;
    return state with { Attributes = new ResourceAttributeValueMap(attributes) };
}
