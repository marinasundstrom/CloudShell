namespace CloudShell.Abstractions.Authentication;

public interface IAccountService
{
    string Mode { get; }

    bool AllowLocalSetup { get; }

    bool SupportsLocalUserAdministration { get; }

    Task<bool> HasLocalUsersAsync(CancellationToken cancellationToken = default);

    Task<AccountOperationResult> SignInAsync(string userName, string credential);

    Task<AccountOperationResult> CreateAdministratorAsync(string email, string password);

    Task<IReadOnlyList<CloudShellAccountUser>> ListUsersAsync(CancellationToken cancellationToken = default);

    Task<AccountOperationResult> CreateUserAsync(
        CreateCloudShellAccountUserRequest request,
        CancellationToken cancellationToken = default);

    Task SignOutAsync();
}

public sealed record AccountOperationResult(bool Succeeded, IReadOnlyList<string> Errors)
{
    public static AccountOperationResult Success() => new(true, []);

    public static AccountOperationResult Failure(params string[] errors) => new(false, errors);
}

public sealed record CloudShellAccountUser(
    string Id,
    string UserName,
    string? Email,
    IReadOnlyList<string> Roles,
    IReadOnlyList<CloudShellAccountClaim> Claims);

public sealed record CloudShellAccountClaim(string Type, string Value);

public sealed record CreateCloudShellAccountUserRequest(
    string Email,
    string Password,
    string? Role,
    IReadOnlyList<CloudShellAccountClaim>? Claims = null);
