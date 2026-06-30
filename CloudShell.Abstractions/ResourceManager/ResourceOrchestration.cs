using System.Text.Json;
using CloudShell.Abstractions.Logs;

namespace CloudShell.Abstractions.ResourceManager;

public sealed record ResourceOrchestrationContext(
    Resource Resource,
    ResourceRegistration? Registration,
    ResourceGroup? ResourceGroup,
    IResourceManagerStore ResourceManager,
    IResourceRegistrationStore Registrations,
    string? PreferredContainerHostId = null,
    string? TriggeredBy = null,
    string? Cause = null,
    IResourceEventSink? ResourceEvents = null);

public sealed record ResourceOrchestrationDescriptor(
    string ResourceId,
    string ResourceType,
    IReadOnlyList<string> DependsOn,
    IReadOnlyList<string> Networks,
    IReadOnlyList<ResourceEndpoint> Endpoints,
    string ProviderConfigurationVersion,
    JsonElement Configuration);

public sealed record ResourceOrchestrationDescriptorContext(
    ResourceRegistration? Registration,
    ResourceGroup? ResourceGroup,
    IResourceManagerStore ResourceManager);

public sealed record ResourceOrchestratorDeployment(
    string Id,
    string OrchestratorId,
    string SourceResourceId,
    string ServiceId,
    string RevisionId,
    ResourceOrchestratorDeploymentSpec Spec,
    ResourceOrchestratorDeploymentStatus Status,
    ResourceOrchestratorEnvironmentRevisionId? BasedOnRevisionId = null);

public sealed record ResourceOrchestratorDeploymentSpec(
    ResourceOrchestratorService Service,
    string WorkloadVersion,
    IReadOnlyDictionary<string, string>? Inputs = null,
    ResourceOrchestratorDeploymentDefinition? Definition = null)
{
    private static readonly IReadOnlyDictionary<string, string> EmptyInputs =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, string> DeploymentInputs => Inputs ?? EmptyInputs;

    public ResourceOrchestratorDeploymentDefinition DeploymentDefinition =>
        CreateDeploymentDefinition();

    public ResourceOrchestratorDeploymentDefinition CreateDeploymentDefinition(string? runtimeRevisionId = null)
    {
        if (Definition is not null)
        {
            return Definition;
        }

        var service = string.IsNullOrWhiteSpace(runtimeRevisionId)
            ? Service
            : Service with { RuntimeRevisionId = runtimeRevisionId.Trim() };

        return ResourceOrchestratorDeploymentDefinition.FromService(
            service,
            WorkloadVersion,
            DeploymentInputs);
    }
}

public sealed record ResourceOrchestratorRevision(
    ResourceOrchestratorEnvironmentRevisionId Id,
    string DeploymentId,
    string SourceResourceId,
    string ServiceId,
    int RevisionNumber,
    DateTimeOffset CreatedAt,
    ResourceOrchestratorRevisionStatus Status,
    ResourceOrchestratorReplicaGroup? ReplicaGroup = null,
    ResourceOrchestratorEnvironmentRevisionId? BasedOnRevisionId = null,
    string? ProvisionedBy = null,
    ResourceOrchestratorDeploymentDefinition? Definition = null);

public sealed record ResourceOrchestratorDeploymentApplyResult(
    ResourceOrchestratorDeployment Deployment,
    ResourceOrchestratorRevision Revision,
    ResourceProcedureResult ProcedureResult,
    IReadOnlyList<ResourceOrchestratorReplicaGroupTearDownRequest>? RetiredReplicaGroups = null,
    ResourceOrchestratorReplicaGroup? PreviousReplicaGroup = null)
{
    public IReadOnlyList<ResourceOrchestratorReplicaGroupTearDownRequest> ReplicaGroupsToTearDown =>
        RetiredReplicaGroups ?? [];
}

public enum ResourceOrchestratorDeploymentStatus
{
    Pending,
    Applying,
    Active,
    Failed,
    Deleted
}

public enum ResourceOrchestratorRevisionStatus
{
    Pending,
    Active,
    Superseded,
    Failed,
    Deleted
}

public interface IResourceOrchestrator
{
    string Id { get; }

    string DisplayName { get; }

    bool CanExecute(
        ResourceOrchestrationContext context,
        ResourceAction action);

    Task<ResourceProcedureResult> ExecuteActionAsync(
        ResourceOrchestrationContext context,
        ResourceAction action,
        CancellationToken cancellationToken = default);

    bool CanDelete(ResourceOrchestrationContext context);

    Task<ResourceProcedureResult> DeleteAsync(
        ResourceOrchestrationContext context,
        CancellationToken cancellationToken = default);
}

public interface IResourceOrchestratorDeploymentApplier
{
    bool CanApplyDeployment(
        ResourceOrchestrationContext context,
        ResourceOrchestratorDeployment deployment);

    Task<ResourceOrchestratorDeploymentApplyResult> ApplyDeploymentAsync(
        ResourceOrchestrationContext context,
        ResourceOrchestratorDeployment deployment,
        CancellationToken cancellationToken = default);
}

public interface IResourceOrchestratorDeploymentCoordinator
{
    bool CanApplyDeployment(
        Resource resource,
        ResourceOrchestratorDeployment deployment);

    Task<ResourceOrchestratorDeploymentApplyResult> ApplyDeploymentAsync(
        Resource resource,
        ResourceOrchestratorDeployment deployment,
        CancellationToken cancellationToken = default,
        string? triggeredBy = null,
        string? cause = null);
}

public interface IResourceOrchestratorDeploymentCleanupCoordinator
{
    Task<ResourceProcedureResult> RunPostApplyCleanupAsync(
        Resource resource,
        ResourceOrchestratorDeploymentApplyResult applyResult,
        ResourceProcedureResult result,
        string? triggeredBy,
        Func<ResourceOrchestratorDeploymentApplyResult, CancellationToken, Task<IReadOnlyList<ResourceOrchestratorReplicaGroupTearDownRequest>>>? describeTearDownsAsync,
        Func<ResourceOrchestratorReplicaGroupTearDownRequest, ResourceOrchestratorReplicaGroup, string, CancellationToken, Task<ResourceProcedureResult>> tearDownReplicaGroupAsync,
        Func<ResourceProcedureResult, ResourceProcedureResult, ResourceProcedureResult>? mergeResults = null,
        CancellationToken cancellationToken = default);
}

public interface IResourceOrchestratorServiceTearDown
{
    bool CanTearDownService(
        ResourceOrchestrationContext context,
        ResourceOrchestratorService service);

    Task<ResourceProcedureResult> TearDownServiceAsync(
        ResourceOrchestrationContext context,
        ResourceOrchestratorService service,
        CancellationToken cancellationToken = default);
}

public interface IResourceOrchestratorReplicaGroupTearDown
{
    bool CanTearDownReplicaGroup(
        ResourceOrchestrationContext context,
        ResourceOrchestratorService service,
        ResourceOrchestratorReplicaGroup replicaGroup);

    Task<ResourceProcedureResult> TearDownReplicaGroupAsync(
        ResourceOrchestrationContext context,
        ResourceOrchestratorService service,
        ResourceOrchestratorReplicaGroup replicaGroup,
        CancellationToken cancellationToken = default);
}

public interface IResourceOrchestratorDeploymentProvider
{
    bool CanDescribeDeployment(Resource resource);

    Task<ResourceOrchestratorDeployment?> DescribeDeploymentAsync(
        ResourceProcedureContext context,
        CancellationToken cancellationToken = default);
}

public interface IResourceOrchestratorDeploymentTearDownProvider
{
    bool CanDescribeDeploymentTearDown(Resource resource);

    Task<IReadOnlyList<ResourceOrchestratorReplicaGroupTearDownRequest>> DescribeDeploymentTearDownAsync(
        ResourceProcedureContext context,
        ResourceOrchestratorDeploymentApplyResult applyResult,
        CancellationToken cancellationToken = default);
}

public interface IResourceOrchestratorDeploymentAppliedProvider
{
    bool CanHandleDeploymentApplied(Resource resource);

    Task HandleDeploymentAppliedAsync(
        ResourceProcedureContext context,
        ResourceOrchestratorDeploymentApplyResult applyResult,
        CancellationToken cancellationToken = default);
}

public interface IResourceOrchestratorDeploymentFailureProvider
{
    bool CanHandleDeploymentApplyFailed(Resource resource);

    Task HandleDeploymentApplyFailedAsync(
        ResourceProcedureContext context,
        ResourceOrchestratorDeployment deployment,
        Exception exception,
        CancellationToken cancellationToken = default);
}

public interface IResourceOrchestrationDescriptorProvider
{
    bool CanDescribe(Resource resource);

    Task<ResourceOrchestrationDescriptor> DescribeAsync(
        Resource resource,
        ResourceOrchestrationDescriptorContext context,
        CancellationToken cancellationToken = default);
}

public interface IResourceActionAvailabilityProvider
{
    bool CanEvaluateAction(Resource resource, ResourceAction action);

    Task<string?> GetActionUnavailableReasonAsync(
        ResourceProcedureContext context,
        ResourceAction action,
        CancellationToken cancellationToken = default);
}
