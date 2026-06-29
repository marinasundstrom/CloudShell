using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Providers.Applications;

public abstract class ApplicationResourceTypeProvider(
    IApplicationResourceProjectionSource projections,
    IApplicationResourceDefinitionSource definitions,
    IApplicationResourceProcedureOperations procedures,
    IApplicationResourceDeclarationOperations declarations,
    IApplicationResourceDescriptorOperations descriptors,
    IApplicationResourceActionAvailabilityOperations actions) :
    IResourceProvider,
    IResourceProcedureProvider,
    IProgrammaticResourceDeclarationProvider,
    IResourceAutoStartPolicyProvider,
    IResourceOrchestrationDescriptorProvider,
    IResourceActionAvailabilityProvider
{
    public abstract string Id { get; }

    public string DisplayName => "Applications";

    protected IApplicationResourceDefinitionSource Definitions { get; } = definitions;

    protected abstract ApplicationResourceProjection Projection { get; }

    public IReadOnlyList<Resource> GetResources() =>
        projections.GetResources(Projection);

    public Task<ResourceProcedureResult> DeleteAsync(
        ResourceProcedureContext context,
        CancellationToken cancellationToken = default) =>
        procedures.DeleteAsync(context, cancellationToken);

    public Task<ResourceProcedureResult> ExecuteActionAsync(
        ResourceProcedureContext context,
        ResourceAction action,
        CancellationToken cancellationToken = default) =>
        procedures.ExecuteActionAsync(context, action, cancellationToken);

    public bool CanApplyDeclaration(ResourceDeclaration declaration) =>
        string.Equals(declaration.ProviderId, Id, StringComparison.OrdinalIgnoreCase);

    public Task ApplyDeclarationAsync(
        ResourceDeclaration declaration,
        IResourceRegistrationStore registrations,
        CancellationToken cancellationToken = default) =>
        declarations.ApplyDeclarationAsync(declaration, registrations, cancellationToken);

    public bool CanEvaluateAutoStartPolicy(ResourceDeclaration declaration) =>
        CanApplyDeclaration(declaration);

    public ResourceAutoStartPolicy GetAutoStartPolicy(ResourceDeclaration declaration) =>
        declarations.GetAutoStartPolicy(declaration);

    public bool CanDescribe(Resource resource) =>
        descriptors.CanDescribe(resource) &&
        Projection.CanProject(Definitions.GetApplication(resource.Id) ?? EmptyDefinition(resource));

    public Task<ResourceOrchestrationDescriptor> DescribeAsync(
        Resource resource,
        ResourceOrchestrationDescriptorContext context,
        CancellationToken cancellationToken = default) =>
        descriptors.DescribeAsync(resource, context, cancellationToken);

    public bool CanEvaluateAction(Resource resource, ResourceAction action) =>
        actions.CanEvaluateAction(resource, action);

    public Task<string?> GetActionUnavailableReasonAsync(
        ResourceProcedureContext context,
        ResourceAction action,
        CancellationToken cancellationToken = default) =>
        actions.GetActionUnavailableReasonAsync(context, action, cancellationToken);

    private static ApplicationResourceDefinition EmptyDefinition(Resource resource) =>
        EmptyDefinition(resource.Id, resource.EffectiveTypeId, resource.Name);

    private static ApplicationResourceDefinition EmptyDefinition(
        string resourceId,
        string resourceType,
        string? name = null) =>
        new(
            resourceId,
            string.IsNullOrWhiteSpace(name) ? resourceId : name,
            string.Empty,
            resourceType: resourceType);
}
