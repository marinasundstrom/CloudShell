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
    ResourceOrchestratorDeploymentStatus Status);

public sealed record ResourceOrchestratorDeploymentSpec(
    ResourceOrchestratorService Service,
    string WorkloadVersion,
    IReadOnlyDictionary<string, string>? Inputs = null)
{
    private static readonly IReadOnlyDictionary<string, string> EmptyInputs =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, string> DeploymentInputs => Inputs ?? EmptyInputs;
}

public sealed record ResourceOrchestratorRevision(
    string Id,
    string DeploymentId,
    string SourceResourceId,
    string ServiceId,
    int RevisionNumber,
    DateTimeOffset CreatedAt,
    ResourceOrchestratorRevisionStatus Status);

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
