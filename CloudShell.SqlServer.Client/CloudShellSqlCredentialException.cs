namespace CloudShell.SqlServer.Client;

public sealed class CloudShellSqlCredentialException : Exception
{
    public CloudShellSqlCredentialException(string message)
        : base(message)
    {
    }

    public CloudShellSqlCredentialException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
