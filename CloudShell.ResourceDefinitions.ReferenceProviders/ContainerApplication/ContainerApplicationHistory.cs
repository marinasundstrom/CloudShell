namespace CloudShell.ResourceDefinitions.ReferenceProviders;

public interface IContainerApplicationHistoryOperations
{
    IReadOnlyList<ApplicationContainerDeployment> GetContainerDeployments(string applicationId);

    IReadOnlyList<ApplicationContainerRevisionHistoryEntry> GetContainerRevisions(string applicationId);
}

public sealed record ApplicationContainerDeployment(
    string Id,
    string ApplicationId,
    string RevisionId,
    string? BasedOnRevisionId,
    string Image,
    int RequestedReplicas,
    DateTimeOffset CreatedAt,
    string Status,
    string ChangeKind,
    string? TriggeredBy = null,
    string? OrchestratorDeploymentId = null);

public static class ApplicationContainerDeploymentStatuses
{
    public const string Completed = "completed";
    public const string Failed = "failed";
}

public sealed record ApplicationContainerRevisionHistoryEntry(
    string Id,
    string ApplicationId,
    string Image,
    int RequestedReplicas,
    DateTimeOffset CreatedAt,
    string Status,
    string ChangeKind,
    string? BasedOnRevisionId = null,
    string? ProvisionedBy = null,
    string? DeploymentId = null,
    int RevisionNumber = 0);

public static class ApplicationContainerRevisionStatuses
{
    public const string Active = "active";
    public const string Superseded = "superseded";
    public const string Failed = "failed";
}

public static class ApplicationContainerRevisionChangeKinds
{
    public const string Initial = "initial";
    public const string ImageDeployment = "image-deployment";
    public const string RestoreDeployment = "restore-deployment";
}
