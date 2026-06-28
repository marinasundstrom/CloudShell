using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.Authorization;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.Hosting;
using CloudShell.Hosting;
using CloudShell.Hosting.Components;
using CloudShell.Hosting.ResourceManager;
using CloudShell.Hosting.Shell;
using CloudShell.Providers.Applications;
using CloudShell.ResourceDefinitions;
using CloudShell.ResourceDefinitions.ReferenceProviders;
using CloudShell.ResourceDefinitions.ReferenceProviders.ResourceManager;
using CloudShell.ResourceDefinitions.ResourceManager;
using CloudShell.ThirdPartyIdentity;
using Microsoft.Extensions.DependencyInjection.Extensions;
using GraphResourceState = CloudShell.ResourceDefinitions.ResourceState;

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

var cloudShell = builder.AddCloudShellControlPlane();
builder.AddCloudShell();
IResourceDefinitionBuilder identityProvisioningResource = null!;
IResourceDefinitionBuilder configurationResource = null!;
IResourceDefinitionBuilder apiResource = null!;
cloudShell.DefineResources(
    resources =>
    {
        identityProvisioningResource = resources
            .AddIdentityProvisioning("keycloak")
            .WithDisplayName("Keycloak Identity Provisioning")
            .WithIdentityProvider("Keycloak")
            .WithIdentityProviderId(identityProviderId)
            .WithProviderKind("oidc");
        configurationResource = resources
            .AddConfigurationStore("third-party-identity")
            .WithDisplayName("Third-party Identity Settings")
            .WithEndpoint(configurationServiceEndpoint);
        apiResource = resources
            .AddAspNetCoreProject("keycloak-provisioned-api", apiProjectPath)
            .WithDisplayName("Keycloak Provisioned API")
            .DependsOn(configurationResource)
            .WithReference(configurationResource, ConfigurationStoreResourceTypeProvider.ResourceTypeId)
            .UseLaunchSettings(false)
            .WithHotReload(false)
            .WithHttpEndpoint(
                host: apiEndpointUri.Host,
                port: apiEndpointUri.Port)
            .WithEnvironmentVariable(
                "CLOUDSHELL_APPLICATION",
                "Keycloak Provisioned API")
            .WithEnvironmentVariable(
                "CLOUDSHELL_CONFIGURATION_SERVICE_NAME",
                "third-party-identity");
    },
    projectState: AddProjectionState);
builder.Services
    .AddIdentityProvisioningResourceType()
    .AddConfigurationStoreResourceType(options =>
    {
        options.ServiceProjectPath = configurationStoreServiceProjectPath;
        options.ServiceWorkingDirectory = repositoryRootPath;
        options.ServiceBearerAuthority = authority;
        options.ServiceBearerIssuer = authority;
        options.ServiceBearerRequireHttpsMetadata =
            builder.Configuration.GetValue("Authentication:OpenIdConnect:RequireHttpsMetadata", true);
        options.Entries.Add(new(
            "Sample:Message",
            "Hello from a Keycloak-provisioned resource identity"));
    })
    .AddAspNetCoreProjectResourceType();
cloudShell.UseResourceGraphIntegration();

cloudShell
    .AddExtension<ResourceManagerExtension>()
    .AddExtension<ObservabilityExtension>();
cloudShell.AddApplicationResourceManagerUi();
builder.Services.TryAddEnumerable(
    ServiceDescriptor.Singleton<IResourceIdentityProvisioner, KeycloakResourceIdentityProvisioner>());
builder.Services.TryAddEnumerable(
    ServiceDescriptor.Singleton<IResourceIdentityProvisioningStatusProvider, KeycloakResourceIdentityProvisioner>());
builder.Services.TryAddEnumerable(
    ServiceDescriptor.Singleton<IResourceIdentityProviderSetupHandler, KeycloakResourceIdentityProvisioner>());
builder.Services.TryAddEnumerable(
    ServiceDescriptor.Singleton<IResourceIdentityCredentialEnvironmentProvider, KeycloakResourceIdentityProvisioner>());
builder.Services.AddSingleton<
    IThirdPartyIdentityGraphIdentityProvisioningSetupBridge,
    ThirdPartyIdentityGraphResourceManagerIdentitySetupBridge>();
builder.Services.AddSingleton<IIdentityProvisioningSetupHandler, GraphIdentityProvisioningSetupHandler>();
builder.Services.TryAddEnumerable(
    ServiceDescriptor.Singleton<
        IAspNetCoreProjectRuntimeEnvironmentProvider,
        GraphAspNetCoreProjectIdentityEnvironmentProvider>());

cloudShell.Resources(resources =>
{
    resources.Declare(identityProvisioningResource);
    var settings = resources.Declare(configurationResource);

    var identityProvider = AddIdentityProviderDefinition(resources, CreateKeycloakIdentityProviderDefinition(
        identityProviderId,
        "Keycloak",
        identityProvisioningResource.EffectiveResourceId));

    var api = resources
        .Declare(apiResource)
        .WithIdentity(
            identityProvider,
            scopes: [builder.Configuration["Keycloak:ResourceIdentityScope"] ?? "openid"],
            name: apiIdentityName)
        .ProvisionIdentityOnStartup();
    settings.Allow(api.Principal, ConfigurationStoreResourceOperationPermissions.ReadEntries);
});

var app = builder.Build();

await app.UseCloudShellControlPlaneAsync();
await app.UseCloudShellAsync();
app.MapCloudShellControlPlane();
app.MapCloudShell<App>();

app.Run();

static string FindRepositoryRoot(string startPath)
{
    var directory = new DirectoryInfo(startPath);
    while (directory is not null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "CloudShell.sln")))
        {
            return directory.FullName;
        }

        directory = directory.Parent;
    }

    return Path.GetFullPath("../..", startPath);
}

static ResourceIdentityProviderDefinition AddIdentityProviderDefinition(
    CloudShell.Abstractions.ResourceManager.IResourceGraphBuilder resources,
    ResourceIdentityProviderDefinition provider)
{
    var declarations = resources.Services
        .Where(descriptor => descriptor.ServiceType == typeof(ResourceDeclarationStore))
        .Select(descriptor => descriptor.ImplementationInstance)
        .OfType<ResourceDeclarationStore>()
        .SingleOrDefault() ?? throw new InvalidOperationException(
            "The resource declaration store is not registered.");
    return declarations.AddIdentityProvider(provider);
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
    if (state.EffectiveResourceId != configurationResource.EffectiveResourceId)
    {
        return state;
    }

    var attributes = state.ResourceAttributeValues.ToDictionary();
    attributes[ConfigurationStoreResourceTypeProvider.Attributes.EntryCount] = 1;
    return state with { Attributes = new ResourceAttributeValueMap(attributes) };
}
