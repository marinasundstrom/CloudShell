using CloudShell.Abstractions.Authorization;
using System.Security.Claims;

namespace CloudShell.Host.Authentication;

public sealed class CloudShellAuthenticationOptions
{
    public const string SectionName = "Authentication";
    public const string PermissionClaimType = "cloudshell.permission";
    public const string ResourceGroupClaimType = "cloudshell.resource-group";
    public const string ResourceClaimType = "cloudshell.resource";
    public const string UngroupedScope = "__ungrouped";

    public bool Enabled { get; set; } = true;

    public string Mode { get; set; } = "Identity";

    public bool AllowLocalSetup { get; set; }

    public string? Secret { get; set; }

    public string DefaultScheme { get; set; } = "CloudShell.Cookie";

    public string ChallengeScheme { get; set; } = "CloudShell.OpenIdConnect";

    public string RoleClaimType { get; set; } = ClaimTypes.Role;

    public string AdministratorRole { get; set; } = "CloudShell.Administrator";

    public OpenIdConnectProviderOptions OpenIdConnect { get; set; } = new();

    public Dictionary<string, string[]> RolePermissions { get; set; } =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["CloudShell.Administrator"] = [CloudShellPermissions.All],
            ["CloudShell.Contributor"] =
            [
                CloudShellPermissions.ResourceGroups.Read,
                CloudShellPermissions.ResourceGroups.Create,
                CloudShellPermissions.ResourceGroups.Manage,
                CloudShellPermissions.Resources.Read,
                CloudShellPermissions.Resources.Create,
                CloudShellPermissions.Resources.Manage
            ],
            ["CloudShell.Reader"] =
            [
                CloudShellPermissions.ResourceGroups.Read,
                CloudShellPermissions.Resources.Read
            ]
        };

    public Dictionary<string, string[]> RoleResourceGroups { get; set; } =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["CloudShell.Administrator"] = [CloudShellPermissions.All]
        };
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
