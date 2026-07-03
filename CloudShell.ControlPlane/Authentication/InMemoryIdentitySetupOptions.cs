using System.Collections.ObjectModel;
using System.Security.Claims;
using CloudShell.Abstractions.Authorization;
using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.ControlPlane.Authentication;

public sealed class InMemoryIdentitySetupOptions
{
    public const string SectionName = ResourceIdentityOptions.SectionName + ":BuiltIn";

    public const string DefaultProviderId = "identity:built-in";

    public string ProviderId { get; set; } = DefaultProviderId;

    public string ProviderName { get; set; } = "Built-in Identity";

    public bool UseAsDefaultProvider { get; set; } = true;

    public bool UseAspNetCoreIdentityStore { get; set; } = true;

    public bool IsConfigured { get; set; }

    public InMemoryIdentityUserCollection Users { get; } = [];
}

public sealed class InMemoryIdentityUserCollection : KeyedCollection<string, InMemoryIdentityUserOptions>
{
    public InMemoryIdentityUserOptions Add(
        string userName,
        string? password = null,
        string? displayName = null,
        string? email = null,
        string? role = null)
    {
        var user = new InMemoryIdentityUserOptions
        {
            UserName = userName,
            Password = password,
            DisplayName = displayName,
            Email = email
        };
        if (!string.IsNullOrWhiteSpace(role))
        {
            user.Roles.Add(role.Trim());
        }

        Add(user);
        return user;
    }

    public new bool TryGetValue(string userName, out InMemoryIdentityUserOptions user)
    {
        if (Dictionary is null ||
            !Dictionary.TryGetValue(InMemoryIdentityUserOptions.NormalizeUserName(userName), out user!))
        {
            user = null!;
            return false;
        }

        return true;
    }

    protected override string GetKeyForItem(InMemoryIdentityUserOptions item) =>
        InMemoryIdentityUserOptions.NormalizeUserName(item.UserName);
}

public sealed class InMemoryIdentityUserOptions
{
    public string UserName { get; set; } = string.Empty;

    public string? Password { get; set; }

    public string? DisplayName { get; set; }

    public string? Email { get; set; }

    public IList<string> Roles { get; } = [];

    public IList<Claim> Claims { get; } = [];

    public InMemoryIdentityUserOptions AddRole(string role)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        Roles.Add(role.Trim());
        return this;
    }

    public InMemoryIdentityUserOptions AddClaim(string type, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Claims.Add(new Claim(type.Trim(), value.Trim()));
        return this;
    }

    public InMemoryIdentityUserOptions AddResourcePermission(string resourceId, string permission) =>
        AddClaim(
            CloudShellAuthorizationClaimTypes.ResourcePermission,
            ResourcePermissionClaimAuthorization.CreateResourcePermissionClaimValue(resourceId, permission));

    public ResourcePrincipalReference ToPrincipal(string? providerId = null) =>
        new(
            ResourcePrincipalKind.User,
            CreatePrincipalId(UserName),
            string.IsNullOrWhiteSpace(DisplayName) ? UserName : DisplayName.Trim(),
            providerId ?? InMemoryIdentitySetupOptions.DefaultProviderId);

    internal static string CreatePrincipalId(string userName) =>
        NormalizeUserName(userName);

    internal static string NormalizeUserName(string userName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userName);
        return userName.Trim().ToLowerInvariant();
    }
}
