namespace CloudShell.Abstractions.ResourceManager;

public sealed record ResourceProcedureResult(string Message)
{
    public static ResourceProcedureResult Completed(string message) => new(message);
}
