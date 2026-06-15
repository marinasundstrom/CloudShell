namespace CloudShell.Abstractions.ResourceManager;

public static class ResourceManagerUiState
{
    public const string ReadOnlyMessage = "Resource Manager is in read-only mode.";

    public static ResourceProcedureResult ReadOnlyResult() =>
        ResourceProcedureResult.Completed(ReadOnlyMessage);
}
