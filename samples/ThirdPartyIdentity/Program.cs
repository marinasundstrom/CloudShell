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
using ResourceGraphState = CloudShell.ResourceDefinitions.ResourceState;

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
const string graphIdentityProvisioningResourceId = "identity-provisioning:graph-keycloak";

var cloudShell = builder.AddCloudShellControlPlane();
builder.AddCloudShell();
builder.Services
    .AddInMemoryResourceModelGraph(
    [
        new ResourceGraphState(
            "graph-keycloak",
            IdentityProvisioningResourceTypeProvider.ResourceTypeId,
            ResourceId: graphIdentityProvisioningResourceId,
            ProviderId: IdentityProvisioningResourceTypeProvider.ProviderId,
            DisplayName: "Graph Keycloak Identity Provisioning",
            Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeValue>
            {
                [IdentityProvisioningResourceTypeProvider.Attributes.IdentityProvider] = "Keycloak",
                [IdentityProvisioningResourceTypeProvider.Attributes.ProviderKind] = "oidc"
            })
    ])
    .AddIdentityProvisioningResourceType()
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
        options.ServiceBasePort = builder.Configuration.GetValue<int?>(
            "Samples:ThirdPartyIdentity:ConfigurationServiceBasePort") ?? options.ServiceBasePort;
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

cloudShell.Resources(resources =>
{
    resources
        .Declare(ResourceModelResourceProvider.DefaultProviderId, graphIdentityProvisioningResourceId);

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
