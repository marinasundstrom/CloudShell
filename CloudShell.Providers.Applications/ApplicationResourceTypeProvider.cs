using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Providers.Applications;

internal abstract class ApplicationResourceTypeProvider(
    IApplicationResourceProjectionSource projections,
    ApplicationResourceService applications) :
    IResourceProvider,
    IResourceProcedureProvider,
    IResourceImageUpdateProvider,
    IResourceReplicaUpdateProvider,
    IResourceTemplateProvider,
    IProgrammaticResourceDeclarationProvider,
    IResourceAutoStartPolicyProvider,
    IResourceOrchestrationDescriptorProvider,
    IResourceOrchestratorServiceProcedureProvider,
    IResourceActionAvailabilityProvider
{
    public abstract string Id { get; }

    public string DisplayName => "Applications";

    protected abstract ApplicationResourceProjection Projection { get; }

    public IReadOnlyList<Resource> GetResources() =>
        projections.GetResources(Projection);

    public Task<ResourceProcedureResult> DeleteAsync(
        ResourceProcedureContext context,
        CancellationToken cancellationToken = default) =>
        applications.DeleteAsync(context, cancellationToken);

    public Task<ResourceProcedureResult> ExecuteActionAsync(
        ResourceProcedureContext context,
        ResourceAction action,
        CancellationToken cancellationToken = default) =>
        applications.ExecuteActionAsync(context, action, cancellationToken);

    public bool CanUpdateImage(Resource resource) =>
        applications.CanUpdateImage(resource);

    public Task<ResourceProcedureResult> UpdateImageAsync(
        ResourceProcedureContext context,
        string image,
        bool restartIfRunning,
        string? triggeredBy = null,
        CancellationToken cancellationToken = default) =>
        applications.UpdateImageAsync(context, image, restartIfRunning, triggeredBy, cancellationToken);

    public bool CanUpdateReplicas(Resource resource) =>
        applications.CanUpdateReplicas(resource);

    public Task<ResourceProcedureResult> UpdateReplicasAsync(
        ResourceProcedureContext context,
        int replicas,
        bool restartIfRunning,
        string? triggeredBy = null,
        CancellationToken cancellationToken = default) =>
        applications.UpdateReplicasAsync(context, replicas, restartIfRunning, triggeredBy, cancellationToken);

    public bool CanExport(Resource resource) =>
        Projection.CanProject(applications.GetApplication(resource.Id) ?? EmptyDefinition(resource)) &&
        applications.CanExport(resource);

    public async Task<ResourceTemplateDefinition> ExportAsync(
        Resource resource,
        ResourceTemplateExportContext context,
        CancellationToken cancellationToken = default)
    {
        var template = await applications.ExportAsync(resource, context, cancellationToken);
        return template with { ProviderId = Id };
    }

    public bool CanImport(ResourceTemplateDefinition template) =>
        string.Equals(template.ProviderId, Id, StringComparison.OrdinalIgnoreCase) &&
        Projection.CanProject(EmptyDefinition(template.ResourceId ?? template.Name, template.ResourceType)) &&
        applications.CanImport(template);

    public Task<ResourceTemplateImportResult> ImportAsync(
        ResourceTemplateDefinition template,
        ResourceTemplateImportContext context,
        CancellationToken cancellationToken = default) =>
        applications.ImportAsync(template, context, cancellationToken);

    public bool CanApplyDeclaration(ResourceDeclaration declaration) =>
        string.Equals(declaration.ProviderId, Id, StringComparison.OrdinalIgnoreCase);

    public Task ApplyDeclarationAsync(
        ResourceDeclaration declaration,
        IResourceRegistrationStore registrations,
        CancellationToken cancellationToken = default) =>
        applications.ApplyDeclarationAsync(declaration, registrations, cancellationToken);

    public bool CanEvaluateAutoStartPolicy(ResourceDeclaration declaration) =>
        CanApplyDeclaration(declaration);

    public ResourceAutoStartPolicy GetAutoStartPolicy(ResourceDeclaration declaration) =>
        applications.GetAutoStartPolicy(declaration);

    public bool CanDescribe(Resource resource) =>
        applications.CanDescribe(resource) &&
        Projection.CanProject(applications.GetApplication(resource.Id) ?? EmptyDefinition(resource));

    public Task<ResourceOrchestrationDescriptor> DescribeAsync(
        Resource resource,
        ResourceOrchestrationDescriptorContext context,
        CancellationToken cancellationToken = default) =>
        applications.DescribeAsync(resource, context, cancellationToken);

    public bool CanExecuteOrchestratorService(
        Resource resource,
        ResourceAction action) =>
        applications.CanExecuteOrchestratorService(resource, action);

    public Task<ResourceOrchestratorService> CreateOrchestratorServiceAsync(
        ResourceProcedureContext context,
        CancellationToken cancellationToken = default) =>
        applications.CreateOrchestratorServiceAsync(context, cancellationToken);

    public Task PrepareOrchestratorServiceAsync(
        ResourceOrchestratorServiceProcedureContext context,
        ResourceAction action,
        CancellationToken cancellationToken = default) =>
        applications.PrepareOrchestratorServiceAsync(context, action, cancellationToken);

    public Task ExecuteOrchestratorServiceInstanceAsync(
        ResourceOrchestratorServiceInstanceContext context,
        ResourceAction action,
        CancellationToken cancellationToken = default) =>
        applications.ExecuteOrchestratorServiceInstanceAsync(context, action, cancellationToken);

    public bool CanEvaluateAction(Resource resource, ResourceAction action) =>
        applications.CanEvaluateAction(resource, action);

    public Task<string?> GetActionUnavailableReasonAsync(
        ResourceProcedureContext context,
        ResourceAction action,
        CancellationToken cancellationToken = default) =>
        applications.GetActionUnavailableReasonAsync(context, action, cancellationToken);

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
