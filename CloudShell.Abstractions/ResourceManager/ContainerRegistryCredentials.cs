namespace CloudShell.Abstractions.ResourceManager;

public sealed record ContainerRegistryCredentials(
    string Username,
    string PasswordEnvironmentVariable)
{
    public string NormalizedUsername => Username.Trim();

    public string NormalizedPasswordEnvironmentVariable => PasswordEnvironmentVariable.Trim();

    public string ResolvePassword()
    {
        var variableName = NormalizedPasswordEnvironmentVariable;
        var password = Environment.GetEnvironmentVariable(variableName);
        if (string.IsNullOrEmpty(password))
        {
            throw new InvalidOperationException(
                $"Container registry credential environment variable '{variableName}' is not configured.");
        }

        return password;
    }

    public static ContainerRegistryCredentials? Normalize(ContainerRegistryCredentials? credentials)
    {
        if (credentials is null)
        {
            return null;
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(credentials.Username);
        ArgumentException.ThrowIfNullOrWhiteSpace(credentials.PasswordEnvironmentVariable);
        return new ContainerRegistryCredentials(
            credentials.Username.Trim(),
            credentials.PasswordEnvironmentVariable.Trim());
    }
}
