using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Providers.Applications;

internal sealed class ApplicationProviderRouter(
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
    public string Id => applications.Id;

    public string DisplayName => applications.DisplayName;

    public IReadOnlyList<Resource> GetResources() => [];

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
        applications.CanExport(resource);

    public Task<ResourceTemplateDefinition> ExportAsync(
        Resource resource,
        ResourceTemplateExportContext context,
        CancellationToken cancellationToken = default) =>
        applications.ExportAsync(resource, context, cancellationToken);

    public bool CanImport(ResourceTemplateDefinition template) =>
        applications.CanImport(template);

    public Task<ResourceTemplateImportResult> ImportAsync(
        ResourceTemplateDefinition template,
        ResourceTemplateImportContext context,
        CancellationToken cancellationToken = default) =>
        applications.ImportAsync(template, context, cancellationToken);

    public bool CanApplyDeclaration(ResourceDeclaration declaration) =>
        applications.CanApplyDeclaration(declaration);

    public Task ApplyDeclarationAsync(
        ResourceDeclaration declaration,
        IResourceRegistrationStore registrations,
        CancellationToken cancellationToken = default) =>
        applications.ApplyDeclarationAsync(declaration, registrations, cancellationToken);

    public bool CanEvaluateAutoStartPolicy(ResourceDeclaration declaration) =>
        applications.CanEvaluateAutoStartPolicy(declaration);

    public ResourceAutoStartPolicy GetAutoStartPolicy(ResourceDeclaration declaration) =>
        applications.GetAutoStartPolicy(declaration);

    public bool CanDescribe(Resource resource) =>
        applications.CanDescribe(resource);

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
}
