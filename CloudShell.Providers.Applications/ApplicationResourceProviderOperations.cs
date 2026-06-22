using CloudShell.Abstractions.Authorization;
using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Providers.Applications;

public interface IApplicationResourceProviderOperations
{
    ApplicationResourceDefinition? GetApplication(string id);

    Task<ResourceProcedureResult> DeleteAsync(
        ResourceProcedureContext context,
        CancellationToken cancellationToken = default);

    Task<ResourceProcedureResult> ExecuteActionAsync(
        ResourceProcedureContext context,
        ResourceAction action,
        CancellationToken cancellationToken = default);

    bool CanExport(Resource resource);

    Task<ResourceTemplateDefinition> ExportAsync(
        Resource resource,
        ResourceTemplateExportContext context,
        CancellationToken cancellationToken = default);

    bool CanImport(ResourceTemplateDefinition template);

    Task<ResourceTemplateImportResult> ImportAsync(
        ResourceTemplateDefinition template,
        ResourceTemplateImportContext context,
        CancellationToken cancellationToken = default);

    ResourceAutoStartPolicy GetAutoStartPolicy(ResourceDeclaration declaration);

    Task ApplyDeclarationAsync(
        ResourceDeclaration declaration,
        IResourceRegistrationStore registrations,
        CancellationToken cancellationToken = default);

    bool CanDescribe(Resource resource);

    Task<ResourceOrchestrationDescriptor> DescribeAsync(
        Resource resource,
        ResourceOrchestrationDescriptorContext context,
        CancellationToken cancellationToken = default);

    bool CanEvaluateAction(Resource resource, ResourceAction action);

    Task<string?> GetActionUnavailableReasonAsync(
        ResourceProcedureContext context,
        ResourceAction action,
        CancellationToken cancellationToken = default);
}

public interface IContainerApplicationResourceProviderOperations
{
    bool CanUpdateImage(Resource resource);

    Task<ResourceProcedureResult> UpdateImageAsync(
        ResourceProcedureContext context,
        string image,
        bool restartIfRunning,
        string? triggeredBy = null,
        CancellationToken cancellationToken = default);

    bool CanUpdateReplicas(Resource resource);

    Task<ResourceProcedureResult> UpdateReplicasAsync(
        ResourceProcedureContext context,
        int replicas,
        bool restartIfRunning,
        string? triggeredBy = null,
        CancellationToken cancellationToken = default);

    bool CanExecuteOrchestratorService(
        Resource resource,
        ResourceAction action);

    Task<ResourceOrchestratorService> CreateOrchestratorServiceAsync(
        ResourceProcedureContext context,
        CancellationToken cancellationToken = default);

    Task PrepareOrchestratorServiceAsync(
        ResourceOrchestratorServiceProcedureContext context,
        ResourceAction action,
        CancellationToken cancellationToken = default);

    Task ExecuteOrchestratorServiceInstanceAsync(
        ResourceOrchestratorServiceInstanceContext context,
        ResourceAction action,
        CancellationToken cancellationToken = default);
}

public interface ISqlServerApplicationResourceProviderOperations
{
    Task<ResourcePermissionGrantStatus> GetSqlServerPermissionGrantStatusAsync(
        ResourcePermissionGrantStatusRequest request,
        CancellationToken cancellationToken = default);
}
