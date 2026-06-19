using CloudShell.Abstractions.Authorization;
using System.Security.Claims;

namespace CloudShell.ControlPlane.Authentication;

public sealed class CloudShellAuthenticationOptions
{
    public const string SectionName = "Authentication";
    public const string PermissionClaimType = CloudShellAuthorizationClaimTypes.Permission;
    public const string ResourceGroupClaimType = CloudShellAuthorizationClaimTypes.ResourceGroup;
    public const string ResourceClaimType = CloudShellAuthorizationClaimTypes.Resource;
    public const string ResourcePermissionClaimType = CloudShellAuthorizationClaimTypes.ResourcePermission;
    public const char ResourcePermissionClaimSeparator = CloudShellAuthorizationClaimTypes.ResourcePermissionSeparator;
    public const string UngroupedScope = "__ungrouped";

    public bool Enabled { get; set; } = true;

    public bool EvaluateClaimsWhenDisabled { get; set; }

    public string Mode { get; set; } = "Identity";

    public bool AllowLocalSetup { get; set; }

    public string? Secret { get; set; }

    public string DefaultScheme { get; set; } = "CloudShell.Cookie";

    public string ChallengeScheme { get; set; } = "CloudShell.OpenIdConnect";

    public string RoleClaimType { get; set; } = ClaimTypes.Role;

    public string AdministratorRole { get; set; } = "CloudShell.Administrator";

    public OpenIdConnectProviderOptions OpenIdConnect { get; set; } = new();

    public BuiltInAuthorityOptions BuiltInAuthority { get; set; } = new();

    public BuiltInIdentityOptions BuiltInIdentity { get; set; } = new();

    public ServiceBearerAuthenticationOptions ServiceBearer { get; set; } = new();

    public Dictionary<string, string[]> RolePermissions { get; set; } =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["CloudShell.Administrator"] = [CloudShellPermissions.All],
            ["CloudShell.Contributor"] =
            [
                CloudShellPermissions.Shell.Read,
                CloudShellPermissions.ResourceGroups.Read,
                CloudShellPermissions.ResourceGroups.Create,
                CloudShellPermissions.ResourceGroups.Manage,
                CloudShellPermissions.Resources.Read,
                CloudShellPermissions.Resources.ReadRuntimeManaged,
                CloudShellPermissions.Resources.Create,
                CloudShellPermissions.Resources.Manage,
                CloudShellPermissions.Observability.Read
            ],
            ["CloudShell.Reader"] =
            [
                CloudShellPermissions.Shell.Read,
                CloudShellPermissions.ResourceGroups.Read,
                CloudShellPermissions.Resources.Read,
                CloudShellPermissions.Observability.Read
            ]
        };

    public Dictionary<string, string[]> RoleResourceGroups { get; set; } =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["CloudShell.Administrator"] = [CloudShellPermissions.All]
        };
}

public sealed class BuiltInIdentityOptions
{
    public bool AllowUserNameSignIn { get; set; }
}

public sealed class BuiltInAuthorityOptions
{
    public bool Enabled { get; set; }

    public string Issuer { get; set; } = "http://localhost";

    public string Audience { get; set; } = "cloudshell-control-plane";

    public int AccessTokenMinutes { get; set; } = 60;

    public string? SigningKeyPem { get; set; }

    public Dictionary<string, BuiltInAuthorityClientOptions> Clients { get; set; } =
        new(StringComparer.Ordinal);
}

public sealed class BuiltInAuthorityClientOptions
{
    public string? Secret { get; set; }

    public string[] Scopes { get; set; } = ["ControlPlane.Access"];

    public string[] Roles { get; set; } = [];

    public string[] Permissions { get; set; } = [];

    public string[] ResourceGroups { get; set; } = [];

    public string[] Resources { get; set; } = [];

    public BuiltInAuthorityResourcePermissionOptions[] ResourcePermissions { get; set; } = [];
}

public sealed class BuiltInAuthorityResourcePermissionOptions
{
    public string ResourceId { get; set; } = string.Empty;

    public string Permission { get; set; } = string.Empty;
}

public sealed class ServiceBearerAuthenticationOptions
{
    public bool Enabled { get; set; }

    public string? Authority { get; set; }

    public string? MetadataAddress { get; set; }

    public string? Issuer { get; set; }

    public string? Audience { get; set; }

    public bool RequireHttpsMetadata { get; set; } = true;

    public string? SigningKeyPem { get; set; }
}

public sealed class OpenIdConnectProviderOptions
{
    public string? Authority { get; set; }

    public string? MetadataAddress { get; set; }

    public string? ClientId { get; set; }

    public string? ClientSecret { get; set; }

    public string CallbackPath { get; set; } = "/signin-oidc";

    public bool RequireHttpsMetadata { get; set; } = true;

    public bool GetClaimsFromUserInfoEndpoint { get; set; } = true;

    public string[] Scopes { get; set; } = ["openid", "profile", "email"];
}
