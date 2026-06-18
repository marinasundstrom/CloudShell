using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;

namespace CloudShell.ControlPlane.Authentication;

public sealed class InMemoryIdentityStore :
    IUserPasswordStore<IdentityUser>,
    IUserEmailStore<IdentityUser>,
    IUserRoleStore<IdentityUser>,
    IUserClaimStore<IdentityUser>,
    IQueryableUserStore<IdentityUser>,
    IRoleClaimStore<IdentityRole>,
    IQueryableRoleStore<IdentityRole>
{
    private readonly ConcurrentDictionary<string, IdentityUser> usersById =
        new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, IdentityRole> rolesById =
        new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, HashSet<string>> userRoles =
        new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, List<Claim>> userClaims =
        new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, List<Claim>> roleClaims =
        new(StringComparer.Ordinal);

    public IQueryable<IdentityUser> Users =>
        usersById.Values.Select(CloneUser).AsQueryable();

    public IQueryable<IdentityRole> Roles =>
        rolesById.Values.Select(CloneRole).AsQueryable();

    public void Dispose()
    {
    }

    public Task<IdentityResult> CreateAsync(IdentityUser user, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(user.Id))
        {
            user.Id = Guid.NewGuid().ToString("N");
        }

        usersById[user.Id] = CloneUser(user);
        return Task.FromResult(IdentityResult.Success);
    }

    public Task<IdentityResult> UpdateAsync(IdentityUser user, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        usersById[user.Id] = CloneUser(user);
        return Task.FromResult(IdentityResult.Success);
    }

    public Task<IdentityResult> DeleteAsync(IdentityUser user, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        usersById.TryRemove(user.Id, out _);
        userRoles.TryRemove(user.Id, out _);
        userClaims.TryRemove(user.Id, out _);
        return Task.FromResult(IdentityResult.Success);
    }

    public Task<string> GetUserIdAsync(IdentityUser user, CancellationToken cancellationToken) =>
        Task.FromResult(user.Id);

    public Task<string?> GetUserNameAsync(IdentityUser user, CancellationToken cancellationToken) =>
        Task.FromResult(user.UserName);

    public Task SetUserNameAsync(IdentityUser user, string? userName, CancellationToken cancellationToken)
    {
        user.UserName = userName;
        return Task.CompletedTask;
    }

    public Task<string?> GetNormalizedUserNameAsync(IdentityUser user, CancellationToken cancellationToken) =>
        Task.FromResult(user.NormalizedUserName);

    public Task SetNormalizedUserNameAsync(
        IdentityUser user,
        string? normalizedName,
        CancellationToken cancellationToken)
    {
        user.NormalizedUserName = normalizedName;
        return Task.CompletedTask;
    }

    public Task<IdentityUser?> FindByIdAsync(string userId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(usersById.TryGetValue(userId, out var user) ? CloneUser(user) : null);
    }

    public Task<IdentityUser?> FindByNameAsync(
        string normalizedUserName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = usersById.Values.FirstOrDefault(user => string.Equals(
            user.NormalizedUserName,
            normalizedUserName,
            StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(user is null ? null : CloneUser(user));
    }

    public Task SetPasswordHashAsync(
        IdentityUser user,
        string? passwordHash,
        CancellationToken cancellationToken)
    {
        user.PasswordHash = passwordHash;
        return Task.CompletedTask;
    }

    public Task<string?> GetPasswordHashAsync(IdentityUser user, CancellationToken cancellationToken) =>
        Task.FromResult(user.PasswordHash);

    public Task<bool> HasPasswordAsync(IdentityUser user, CancellationToken cancellationToken) =>
        Task.FromResult(!string.IsNullOrWhiteSpace(user.PasswordHash));

    public Task SetEmailAsync(IdentityUser user, string? email, CancellationToken cancellationToken)
    {
        user.Email = email;
        return Task.CompletedTask;
    }

    public Task<string?> GetEmailAsync(IdentityUser user, CancellationToken cancellationToken) =>
        Task.FromResult(user.Email);

    public Task<bool> GetEmailConfirmedAsync(IdentityUser user, CancellationToken cancellationToken) =>
        Task.FromResult(user.EmailConfirmed);

    public Task SetEmailConfirmedAsync(
        IdentityUser user,
        bool confirmed,
        CancellationToken cancellationToken)
    {
        user.EmailConfirmed = confirmed;
        return Task.CompletedTask;
    }

    public Task<IdentityUser?> FindByEmailAsync(
        string normalizedEmail,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = usersById.Values.FirstOrDefault(user => string.Equals(
            user.NormalizedEmail,
            normalizedEmail,
            StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(user is null ? null : CloneUser(user));
    }

    public Task<string?> GetNormalizedEmailAsync(IdentityUser user, CancellationToken cancellationToken) =>
        Task.FromResult(user.NormalizedEmail);

    public Task SetNormalizedEmailAsync(
        IdentityUser user,
        string? normalizedEmail,
        CancellationToken cancellationToken)
    {
        user.NormalizedEmail = normalizedEmail;
        return Task.CompletedTask;
    }

    public Task AddToRoleAsync(
        IdentityUser user,
        string roleName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var roles = userRoles.GetOrAdd(user.Id, _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        lock (roles)
        {
            roles.Add(roleName);
        }

        return Task.CompletedTask;
    }

    public Task RemoveFromRoleAsync(
        IdentityUser user,
        string roleName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (userRoles.TryGetValue(user.Id, out var roles))
        {
            lock (roles)
            {
                roles.Remove(roleName);
            }
        }

        return Task.CompletedTask;
    }

    public Task<IList<string>> GetRolesAsync(IdentityUser user, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!userRoles.TryGetValue(user.Id, out var roles))
        {
            return Task.FromResult<IList<string>>([]);
        }

        lock (roles)
        {
            return Task.FromResult<IList<string>>(roles
                .Select(FindRoleName)
                .Where(role => !string.IsNullOrWhiteSpace(role))
                .Select(role => role!)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray());
        }
    }

    public Task<bool> IsInRoleAsync(
        IdentityUser user,
        string roleName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(userRoles.TryGetValue(user.Id, out var roles) && roles.Contains(roleName));
    }

    public Task<IList<IdentityUser>> GetUsersInRoleAsync(
        string roleName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var users = userRoles
            .Where(entry => entry.Value.Contains(roleName))
            .Select(entry => usersById.TryGetValue(entry.Key, out var user) ? CloneUser(user) : null)
            .Where(user => user is not null)
            .Select(user => user!)
            .ToArray();
        return Task.FromResult<IList<IdentityUser>>(users);
    }

    public Task<IList<Claim>> GetClaimsAsync(IdentityUser user, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IList<Claim>>(GetClaims(userClaims, user.Id));
    }

    public Task AddClaimsAsync(
        IdentityUser user,
        IEnumerable<Claim> claims,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AddClaims(userClaims, user.Id, claims);
        return Task.CompletedTask;
    }

    public Task ReplaceClaimAsync(
        IdentityUser user,
        Claim claim,
        Claim newClaim,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RemoveClaims(userClaims, user.Id, [claim]);
        AddClaims(userClaims, user.Id, [newClaim]);
        return Task.CompletedTask;
    }

    public Task RemoveClaimsAsync(
        IdentityUser user,
        IEnumerable<Claim> claims,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RemoveClaims(userClaims, user.Id, claims);
        return Task.CompletedTask;
    }

    public Task<IList<IdentityUser>> GetUsersForClaimAsync(
        Claim claim,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var users = userClaims
            .Where(entry => entry.Value.Any(candidate => SameClaim(candidate, claim)))
            .Select(entry => usersById.TryGetValue(entry.Key, out var user) ? CloneUser(user) : null)
            .Where(user => user is not null)
            .Select(user => user!)
            .ToArray();
        return Task.FromResult<IList<IdentityUser>>(users);
    }

    public Task<IdentityResult> CreateAsync(IdentityRole role, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(role.Id))
        {
            role.Id = Guid.NewGuid().ToString("N");
        }

        rolesById[role.Id] = CloneRole(role);
        return Task.FromResult(IdentityResult.Success);
    }

    public Task<IdentityResult> UpdateAsync(IdentityRole role, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        rolesById[role.Id] = CloneRole(role);
        return Task.FromResult(IdentityResult.Success);
    }

    public Task<IdentityResult> DeleteAsync(IdentityRole role, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        rolesById.TryRemove(role.Id, out _);
        roleClaims.TryRemove(role.Id, out _);
        return Task.FromResult(IdentityResult.Success);
    }

    public Task<string> GetRoleIdAsync(IdentityRole role, CancellationToken cancellationToken) =>
        Task.FromResult(role.Id);

    public Task<string?> GetRoleNameAsync(IdentityRole role, CancellationToken cancellationToken) =>
        Task.FromResult(role.Name);

    public Task SetRoleNameAsync(IdentityRole role, string? roleName, CancellationToken cancellationToken)
    {
        role.Name = roleName;
        return Task.CompletedTask;
    }

    public Task<string?> GetNormalizedRoleNameAsync(IdentityRole role, CancellationToken cancellationToken) =>
        Task.FromResult(role.NormalizedName);

    public Task SetNormalizedRoleNameAsync(
        IdentityRole role,
        string? normalizedName,
        CancellationToken cancellationToken)
    {
        role.NormalizedName = normalizedName;
        return Task.CompletedTask;
    }

    Task<IdentityRole?> IRoleStore<IdentityRole>.FindByIdAsync(
        string roleId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(rolesById.TryGetValue(roleId, out var role) ? CloneRole(role) : null);
    }

    Task<IdentityRole?> IRoleStore<IdentityRole>.FindByNameAsync(
        string normalizedRoleName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var role = rolesById.Values.FirstOrDefault(role => string.Equals(
            role.NormalizedName,
            normalizedRoleName,
            StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(role is null ? null : CloneRole(role));
    }

    public Task<IList<Claim>> GetClaimsAsync(IdentityRole role, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IList<Claim>>(GetClaims(roleClaims, role.Id));
    }

    public Task AddClaimAsync(
        IdentityRole role,
        Claim claim,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AddClaims(roleClaims, role.Id, [claim]);
        return Task.CompletedTask;
    }

    public Task RemoveClaimAsync(
        IdentityRole role,
        Claim claim,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RemoveClaims(roleClaims, role.Id, [claim]);
        return Task.CompletedTask;
    }

    private string? FindRoleName(string normalizedRoleName) =>
        rolesById.Values.FirstOrDefault(role => string.Equals(
            role.NormalizedName,
            normalizedRoleName,
            StringComparison.OrdinalIgnoreCase))?.Name ?? normalizedRoleName;

    private static void AddClaims(
        ConcurrentDictionary<string, List<Claim>> claimsByOwner,
        string ownerId,
        IEnumerable<Claim> claims)
    {
        var target = claimsByOwner.GetOrAdd(ownerId, _ => []);
        lock (target)
        {
            foreach (var claim in claims)
            {
                if (!target.Any(candidate => SameClaim(candidate, claim)))
                {
                    target.Add(new Claim(claim.Type, claim.Value));
                }
            }
        }
    }

    private static void RemoveClaims(
        ConcurrentDictionary<string, List<Claim>> claimsByOwner,
        string ownerId,
        IEnumerable<Claim> claims)
    {
        if (!claimsByOwner.TryGetValue(ownerId, out var target))
        {
            return;
        }

        lock (target)
        {
            foreach (var claim in claims)
            {
                target.RemoveAll(candidate => SameClaim(candidate, claim));
            }
        }
    }

    private static IList<Claim> GetClaims(
        ConcurrentDictionary<string, List<Claim>> claimsByOwner,
        string ownerId)
    {
        if (!claimsByOwner.TryGetValue(ownerId, out var claims))
        {
            return [];
        }

        lock (claims)
        {
            return claims.Select(claim => new Claim(claim.Type, claim.Value)).ToArray();
        }
    }

    private static bool SameClaim(Claim left, Claim right) =>
        string.Equals(left.Type, right.Type, StringComparison.Ordinal) &&
        string.Equals(left.Value, right.Value, StringComparison.Ordinal);

    private static IdentityUser CloneUser(IdentityUser user) =>
        new()
        {
            Id = user.Id,
            UserName = user.UserName,
            NormalizedUserName = user.NormalizedUserName,
            Email = user.Email,
            NormalizedEmail = user.NormalizedEmail,
            EmailConfirmed = user.EmailConfirmed,
            PasswordHash = user.PasswordHash,
            SecurityStamp = user.SecurityStamp,
            ConcurrencyStamp = user.ConcurrencyStamp,
            PhoneNumber = user.PhoneNumber,
            PhoneNumberConfirmed = user.PhoneNumberConfirmed,
            TwoFactorEnabled = user.TwoFactorEnabled,
            LockoutEnd = user.LockoutEnd,
            LockoutEnabled = user.LockoutEnabled,
            AccessFailedCount = user.AccessFailedCount
        };

    private static IdentityRole CloneRole(IdentityRole role) =>
        new()
        {
            Id = role.Id,
            Name = role.Name,
            NormalizedName = role.NormalizedName,
            ConcurrencyStamp = role.ConcurrencyStamp
        };
}
