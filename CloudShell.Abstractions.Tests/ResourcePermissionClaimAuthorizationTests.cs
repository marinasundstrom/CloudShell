using System.Security.Claims;
using System.Text.Json;
using CloudShell.Abstractions.Authorization;

namespace CloudShell.Abstractions.Tests;

public sealed class ResourcePermissionClaimAuthorizationTests
{
    [Fact]
    public void HasResourcePermission_MatchesScopedResourcePermissionClaim()
    {
        var user = CreateUser(ResourcePermissionClaimAuthorization.CreateResourcePermissionClaimValue(
            "configuration:sample-app",
            ConfigurationStoreResourceOperationPermissions.ReadSettings));

        Assert.True(ResourcePermissionClaimAuthorization.HasResourcePermission(
            user,
            "configuration:sample-app",
            ConfigurationStoreResourceOperationPermissions.ReadSettings));
        Assert.False(ResourcePermissionClaimAuthorization.HasResourcePermission(
            user,
            "secrets-vault:sample-app",
            SecretsVaultResourceOperationPermissions.ReadSecrets));
    }

    [Fact]
    public void HasResourcePermission_MatchesWildcardResourceOrPermission()
    {
        var wildcardResource = CreateUser(ResourcePermissionClaimAuthorization.CreateResourcePermissionClaimValue(
            CloudShellPermissions.All,
            ConfigurationStoreResourceOperationPermissions.ReadSettings));
        var wildcardPermission = CreateUser(ResourcePermissionClaimAuthorization.CreateResourcePermissionClaimValue(
            "configuration:sample-app",
            CloudShellPermissions.All));

        Assert.True(ResourcePermissionClaimAuthorization.HasResourcePermission(
            wildcardResource,
            "configuration:sample-app",
            ConfigurationStoreResourceOperationPermissions.ReadSettings));
        Assert.True(ResourcePermissionClaimAuthorization.HasResourcePermission(
            wildcardPermission,
            "configuration:sample-app",
            ConfigurationStoreResourceOperationPermissions.ReadSettings));
        Assert.False(ResourcePermissionClaimAuthorization.HasResourcePermission(
            wildcardResource,
            "configuration:sample-app",
            SecretsVaultResourceOperationPermissions.ReadSecrets));
    }

    [Fact]
    public void HasResourcePermission_MatchesNestedCloudShellResourcePermissionClaim()
    {
        var resourcePermission = ResourcePermissionClaimAuthorization.CreateResourcePermissionClaimValue(
            "configuration:sample-app",
            ConfigurationStoreResourceOperationPermissions.ReadSettings);
        var user = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(
                    "cloudshell",
                    JsonSerializer.Serialize(new Dictionary<string, string[]>
                    {
                        ["resource-permission"] = [resourcePermission]
                    }))
            ],
            authenticationType: "Test"));

        Assert.True(ResourcePermissionClaimAuthorization.HasResourcePermission(
            user,
            "configuration:sample-app",
            ConfigurationStoreResourceOperationPermissions.ReadSettings));
        Assert.False(ResourcePermissionClaimAuthorization.HasResourcePermission(
            user,
            "secrets-vault:sample-app",
            SecretsVaultResourceOperationPermissions.ReadSecrets));
    }

    [Fact]
    public void HasResourcePermission_RequiresAuthenticatedPrincipal()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(
                    CloudShellAuthorizationClaimTypes.ResourcePermission,
                    ResourcePermissionClaimAuthorization.CreateResourcePermissionClaimValue(
                        "configuration:sample-app",
                        ConfigurationStoreResourceOperationPermissions.ReadSettings))
            ]));

        Assert.False(ResourcePermissionClaimAuthorization.HasResourcePermission(
            user,
            "configuration:sample-app",
            ConfigurationStoreResourceOperationPermissions.ReadSettings));
    }

    private static ClaimsPrincipal CreateUser(string resourcePermissionClaim) =>
        new(new ClaimsIdentity(
            [
                new Claim(CloudShellAuthorizationClaimTypes.ResourcePermission, resourcePermissionClaim)
            ],
            authenticationType: "Test"));
}
