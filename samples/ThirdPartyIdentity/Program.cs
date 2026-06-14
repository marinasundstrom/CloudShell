using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.Authorization;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.Hosting;
using CloudShell.Hosting;
using CloudShell.Hosting.Components;
using CloudShell.Hosting.ResourceManager;
using CloudShell.Hosting.Shell;
using CloudShell.Providers.Configuration;

var builder = CloudShellApplication.CreateBuilder(args);

var cloudShell = builder.AddCloudShellControlPlane();
builder.AddCloudShell();

cloudShell
    .AddExtension<ResourceManagerExtension>()
    .AddExtension<ObservabilityExtension>()
    .AddConfigurationProvider();

cloudShell.Resources(resources =>
{
    var authority = builder.Configuration["Authentication:OpenIdConnect:Authority"] ??
        "http://localhost:8080/realms/cloudshell";
    var clientId = builder.Configuration["Authentication:OpenIdConnect:ClientId"] ??
        "cloudshell-ui";
    var provisioningResource = resources
        .Declare("identity.provisioning", "identity-provisioning:keycloak")
        .WithResourceClass(ResourceClass.Infrastructure)
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
            ["Authority"] = authority,
            ["ClientId"] = clientId
        },
        provisioningResourceId: provisioningResource.ResourceId,
        useAsDefault: true);
    var api = resources
        .Declare("applications", "application:keycloak-provisioned-api")
        .WithIdentity(identityProvider, identity =>
        {
            identity.Name = "keycloak-provisioned-api";
            identity.Subject = "client:cloudshell-keycloak-provisioned-api";
            identity.Scopes.Add("cloudshell.resource");
            identity.Claims["resource"] = "application:keycloak-provisioned-api";
        })
        .ProvisionIdentityOnStartup();

    resources
        .AddConfigurationStore(
            "configuration:third-party-identity",
            "Third-party Identity Settings")
        .WithEntries(
        [
            new("Authority", authority),
            new("RoleClaimType", builder.Configuration["Authentication:RoleClaimType"] ?? string.Empty)
        ])
        .Allow(api.Identity, ConfigurationStoreResourceOperationPermissions.ReadEntries);
});

var app = builder.Build();

await app.UseCloudShellControlPlaneAsync();
await app.UseCloudShellAsync();
app.MapCloudShellControlPlane();
app.MapCloudShell<App>();

app.Run();
