namespace CloudShell.Abstractions.Authentication;

public interface IAccountService
{
    string Mode { get; }

    bool AllowLocalSetup { get; }

    Task<bool> HasLocalUsersAsync(CancellationToken cancellationToken = default);

    Task<AccountOperationResult> SignInAsync(string userName, string credential);

    Task<AccountOperationResult> CreateAdministratorAsync(string email, string password);

    Task SignOutAsync();
}

public sealed record AccountOperationResult(bool Succeeded, IReadOnlyList<string> Errors)
{
    public static AccountOperationResult Success() => new(true, []);

    public static AccountOperationResult Failure(params string[] errors) => new(false, errors);
}
