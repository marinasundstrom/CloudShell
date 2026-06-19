using CloudShell.Abstractions.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;

namespace CloudShell.Hosting.Authentication;

internal sealed class ExternalAccountService(IHttpContextAccessor httpContextAccessor) : IAccountService
{
    public string Mode => "External";

    public bool AllowLocalSetup => false;

    public bool SupportsLocalUserAdministration => false;

    public CloudShellLocalUserStoreKind LocalUserStoreKind => CloudShellLocalUserStoreKind.Unavailable;

    public bool AllowUserNameSignIn => false;

    public Task<bool> HasLocalUsersAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(false);

    public Task<AccountOperationResult> SignInAsync(string identifier, string credential) =>
        Task.FromResult(AccountOperationResult.Failure(
            "This authentication provider uses its external sign-in flow."));

    public Task<AccountOperationResult> CreateAdministratorAsync(string email, string password) =>
        Task.FromResult(AccountOperationResult.Failure("Local account setup is unavailable."));

    public Task<IReadOnlyList<CloudShellAccountUser>> ListUsersAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<CloudShellAccountUser>>([]);

    public Task<AccountOperationResult> CreateUserAsync(
        CreateCloudShellAccountUserRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(AccountOperationResult.Failure("Local user administration is unavailable."));

    public async Task SignOutAsync()
    {
        var context = httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("An HTTP context is required to sign out.");

        await context.SignOutAsync();
    }
}
