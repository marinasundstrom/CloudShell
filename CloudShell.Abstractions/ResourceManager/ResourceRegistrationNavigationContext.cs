namespace CloudShell.Abstractions.ResourceManager;

public sealed record ResourceRegistrationNavigationContext(string? ReturnUrl)
{
    public const string DefaultReturnUrl = "/resources";

    public string GetReturnUrlOrDefault()
    {
        var normalized = Normalize(ReturnUrl);
        return IsLocalReturnUrl(normalized)
            ? normalized!
            : DefaultReturnUrl;
    }

    public static bool IsLocalReturnUrl(string? returnUrl) =>
        !string.IsNullOrWhiteSpace(returnUrl) &&
        returnUrl.StartsWith('/', StringComparison.Ordinal) &&
        !returnUrl.StartsWith("//", StringComparison.Ordinal) &&
        !returnUrl.Contains("\r", StringComparison.Ordinal) &&
        !returnUrl.Contains("\n", StringComparison.Ordinal);

    private static string? Normalize(string? returnUrl) =>
        string.IsNullOrWhiteSpace(returnUrl) ? null : returnUrl.Trim();
}
