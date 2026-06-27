using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.Authorization;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.Hosting;
using CloudShell.Hosting;
using CloudShell.Hosting.Components;
using CloudShell.Hosting.ResourceManager;
using CloudShell.Hosting.Shell;
using CloudShell.Providers.Applications;
using CloudShell.Providers.Configuration;
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
var apiEndpoint = builder.Configuration["Samples:ThirdPartyIdentity:ApiEndpoint"] ??
    "http://localhost:5234";
var configurationServiceBasePort = builder.Configuration.GetValue<int?>(
    "Samples:ThirdPartyIdentity:ConfigurationServiceBasePort") ?? 5138;
var graphConfigurationServiceEndpoint =
    builder.Configuration["Samples:ThirdPartyIdentity:GraphConfigurationServiceEndpoint"] ??
    $"http://localhost:{configurationServiceBasePort}";
var graphApiEndpoint = builder.Configuration["Samples:ThirdPartyIdentity:GraphApiEndpoint"] ??
    "http://localhost:5235";
var graphApiEndpointUri = new Uri(graphApiEndpoint);
const string graphIdentityProvisioningResourceId = "identity-provisioning:graph-keycloak";
const string graphConfigurationResourceId = "configuration.store:graph-third-party-identity";
const string graphApiResourceId = "application.aspnet-core-project:graph-keycloak-provisioned-api";

var graph = new ResourceDefinitionGraphBuilder();
graph
    .AddIdentityProvisioning("graph-keycloak")
    .WithResourceId(graphIdentityProvisioningResourceId)
    .WithDisplayName("Graph Keycloak Identity Provisioning")
    .WithIdentityProvider("Keycloak")
    .WithIdentityProviderId("identity:graph-keycloak")
    .WithProviderKind("oidc");
var graphSettings = graph
    .AddConfigurationStore("graph-third-party-identity")
    .WithResourceId(graphConfigurationResourceId)
    .WithDisplayName("Graph Third-party Identity Settings")
    .WithEndpoint(graphConfigurationServiceEndpoint);
graph
    .AddAspNetCoreProject("graph-keycloak-provisioned-api", apiProjectPath)
    .WithResourceId(graphApiResourceId)
    .WithDisplayName("Graph Keycloak Provisioned API")
    .DependsOn(graphSettings)
    .WithReference(graphSettings, ConfigurationStoreResourceTypeProvider.ResourceTypeId)
    .UseLaunchSettings(false)
    .WithHotReload(false)
    .AddEndpointRequest(
        "http",
        graphApiEndpointUri.Scheme,
        host: graphApiEndpointUri.Host,
        port: graphApiEndpointUri.Port,
        exposure: "Local")
    .WithEnvironmentVariable(
        "CLOUDSHELL_APPLICATION",
        "Graph Keycloak Provisioned API")
    .WithEnvironmentVariable(
        "CLOUDSHELL_CONFIGURATION_SERVICE_NAME",
        "graph-third-party-identity");

var cloudShell = builder.AddCloudShellControlPlane();
builder.AddCloudShell();
builder.Services
    .AddInMemoryResourceModelGraph(
        graph.BuildDeployment("third-party-identity", environmentId: "local")
            .Resources
            .Select(GraphResourceState.FromDefinition)
            .Select(AddGraphProjectionState))
    .AddIdentityProvisioningResourceType()
    .AddConfigurationStoreResourceType(options =>
    {
        options.ServiceProjectPath = configurationStoreServiceProjectPath;
        options.ServiceWorkingDirectory = repositoryRootPath;
        options.Entries.Add(new(
            "Sample:Message",
            "Hello from a graph Keycloak-provisioned resource identity"));
    })
    .AddAspNetCoreProjectResourceType()
    .AddResourceModelGraphServices()
    .AddReferenceProviderResourceManagerProjections()
    .AddResourceModelGraphProcedureProvider(
        ResourceModelResourceProvider.DefaultProviderId,
        "Resource model");

cloudShell
    .AddExtension<ResourceManagerExtension>()
    .AddExtension<ObservabilityExtension>()
    .AddApplicationProvider()
    .AddConfigurationProvider(options =>
    {
        options.ServiceProjectPath = configurationStoreServiceProjectPath;
        options.ServiceWorkingDirectory = repositoryRootPath;
        options.ServiceBasePort = configurationServiceBasePort;
        options.ServiceBearerAuthority = authority;
        options.ServiceBearerIssuer = authority;
        options.ServiceBearerRequireHttpsMetadata =
            builder.Configuration.GetValue("Authentication:OpenIdConnect:RequireHttpsMetadata", true);
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
    IThirdPartyIdentityGraphIdentityProvisioningSetupBridge,
    ThirdPartyIdentityGraphResourceManagerIdentitySetupBridge>();
builder.Services.AddSingleton<IIdentityProvisioningSetupHandler, GraphIdentityProvisioningSetupHandler>();

cloudShell.Resources(resources =>
{
    resources
        .Declare(ResourceModelResourceProvider.DefaultProviderId, graphIdentityProvisioningResourceId);
    var graphSettings = resources
        .Declare(ResourceModelResourceProvider.DefaultProviderId, graphConfigurationResourceId);

    var provisioningResource = resources
        .Declare(ResourceIdentityProvisioningResources.ProviderId, "identity-provisioning:keycloak")
        .WithResourceClass(CloudShell.Abstractions.ResourceManager.ResourceClass.Infrastructure)
        .WithResourceAttribute(ResourceAttributeNames.InfrastructureKind, "identity-provisioning")
        .WithResourceAttribute("identity.provider", "Keycloak")
        .WithResourceAttribute("identity.authority", authority)
        .WithResourceAttribute("identity.clientId", clientId)
        .WithResourceAttribute("identity.provisioning.mode", "external");
    var identityProvider = resources.AddIdentityProvider(
        "identity:keycloak",
        "Keycloak",
        ResourceIdentityProviderKind.Oidc,
        new Dictionary<string, string>
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
        },
        provisioningResourceId: provisioningResource.ResourceId,
        useAsDefault: true);
    AddIdentityProviderDefinition(resources, new ResourceIdentityProviderDefinition(
        "identity:graph-keycloak",
        "Graph Keycloak",
        ResourceIdentityProviderKind.Oidc,
        new Dictionary<string, string>
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
        }));
    var settings = resources
        .AddConfigurationStore("third-party-identity")
        .WithDisplayName("Third-party Identity Settings")
        .WithEntries(
        [
            new("Authority", authority),
            new("RoleClaimType", builder.Configuration["Authentication:RoleClaimType"] ?? string.Empty),
            new("Sample:Message", "Hello from a Keycloak-provisioned resource identity")
        ]);

    var api = resources
        .AddAspNetCoreProject(
            "keycloak-provisioned-api",
            apiProjectPath,
            endpoint: apiEndpoint)
        .WithIdentity(identityProvider, identity =>
        {
            identity.Name = "keycloak-provisioned-api";
            identity.Subject = "client:cloudshell-keycloak-provisioned-api";
            identity.Scopes.Add(builder.Configuration["Keycloak:ResourceIdentityScope"] ?? "openid");
            identity.Claims["resource"] = "application:keycloak-provisioned-api";
        })
        .WithReference(settings)
        .WithServiceDiscovery()
        .DependsOn(settings)
        .WithAutoStart(false)
        .ProvisionIdentityOnStartup();

    settings.Allow(api.Principal, ConfigurationStoreResourceOperationPermissions.ReadEntries);

    var graphApi = resources
        .Declare(ResourceModelResourceProvider.DefaultProviderId, graphApiResourceId)
        .WithIdentity(identityProvider, name: "graph-keycloak-provisioned-api")
        .ProvisionIdentityOnStartup();
    graphSettings.Allow(graphApi.Principal, ConfigurationStoreResourceOperationPermissions.ReadEntries);
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

GraphResourceState AddGraphProjectionState(GraphResourceState state)
{
    if (state.EffectiveResourceId != graphConfigurationResourceId)
    {
        return state;
    }

    var attributes = state.ResourceAttributeValues.ToDictionary();
    attributes[ConfigurationStoreResourceTypeProvider.Attributes.EntryCount] = 1;
    return state with { Attributes = new ResourceAttributeValueMap(attributes) };
}
