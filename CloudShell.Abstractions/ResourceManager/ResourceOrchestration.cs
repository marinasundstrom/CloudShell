using System.Text.Json;

namespace CloudShell.Abstractions.ResourceManager;

public sealed record ResourceOrchestrationContext(
    Resource Resource,
    ResourceRegistration? Registration,
    ResourceGroup? ResourceGroup,
    IResourceManagerStore ResourceManager,
    IResourceRegistrationStore Registrations,
    string? PreferredContainerHostId = null);

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
