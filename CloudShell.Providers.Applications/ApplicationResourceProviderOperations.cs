using CloudShell.Abstractions.Authorization;
using CloudShell.Abstractions.ResourceManager;
using System.Security.Claims;

namespace CloudShell.Providers.Applications;

public interface IApplicationResourceDefinitionSource
{
    ApplicationResourceDefinition? GetApplication(string id);

    IReadOnlyList<ApplicationResourceDefinition> GetApplications();
}

public interface IApplicationResourceRunningStateOperations
{
    bool IsRunning(string applicationId);
}

public interface IApplicationResourceConfigurationOperations :
    IApplicationResourceDefinitionSource,
    IApplicationResourceRunningStateOperations
{
    Task UpdateApplicationAsync(
        ApplicationResourceDefinition definition,
        string? resourceGroupId,
        IResourceRegistrationStore registrations,
        CancellationToken cancellationToken = default);
}

public interface IApplicationResourceManagementOperations : IApplicationResourceDefinitionSource
{
    Task SetupApplicationAsync(
        ApplicationResourceDefinition definition,
        string? resourceGroupId,
        IResourceRegistrationStore registrations,
        CancellationToken cancellationToken = default);

    Task UpdateApplicationAsync(
        ApplicationResourceDefinition definition,
        string? resourceGroupId,
        IResourceRegistrationStore registrations,
        CancellationToken cancellationToken = default);

    bool IsRunning(string applicationId);
}

public interface IContainerApplicationHistoryOperations
{
    IReadOnlyList<ApplicationContainerDeployment> GetContainerDeployments(string applicationId);

    IReadOnlyList<ApplicationContainerRevisionHistoryEntry> GetContainerRevisions(string applicationId);
}

public interface ISqlServerDatabaseInspectionOperations
{
    Task<IReadOnlyList<SqlServerDatabaseInfo>> QuerySqlServerDatabasesAsync(
        string sqlServerResourceId,
        CancellationToken cancellationToken = default);
}

public interface ISqlServerCredentialResolutionOperations
{
    Task<SqlServerCredentialResolutionResult> ResolveSqlServerCredentialAsync(
        string sqlServerResourceName,
        string databaseName,
        string permission,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken = default);
}

public interface IApplicationResourceProcedureOperations
{
    Task<ResourceProcedureResult> DeleteAsync(
        ResourceProcedureContext context,
        CancellationToken cancellationToken = default);

    Task<ResourceProcedureResult> ExecuteActionAsync(
        ResourceProcedureContext context,
        ResourceAction action,
        CancellationToken cancellationToken = default);
}

public interface IApplicationResourceTemplateOperations
{
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
}

public interface IApplicationResourceDeclarationOperations
{
    ResourceAutoStartPolicy GetAutoStartPolicy(ResourceDeclaration declaration);

    Task ApplyDeclarationAsync(
        ResourceDeclaration declaration,
        IResourceRegistrationStore registrations,
        CancellationToken cancellationToken = default);
}

public interface IApplicationResourceDescriptorOperations
{
    bool CanDescribe(Resource resource);

    Task<ResourceOrchestrationDescriptor> DescribeAsync(
        Resource resource,
        ResourceOrchestrationDescriptorContext context,
        CancellationToken cancellationToken = default);
}

public interface IApplicationResourceActionAvailabilityOperations
{
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
        CancellationToken cancellationToken = default,
        int? requestedReplicas = null);

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

    bool CanDescribeDeployment(Resource resource);

    Task<ResourceOrchestratorDeployment?> DescribeDeploymentAsync(
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

    Task<IReadOnlyList<ResourceOrchestratorReplicaGroupTearDownRequest>> DescribeDeploymentTearDownAsync(
        ResourceProcedureContext context,
        ResourceOrchestratorDeploymentApplyResult applyResult,
        CancellationToken cancellationToken = default);

    Task HandleDeploymentAppliedAsync(
        ResourceProcedureContext context,
        ResourceOrchestratorDeploymentApplyResult applyResult,
        CancellationToken cancellationToken = default);

    Task HandleDeploymentApplyFailedAsync(
        ResourceProcedureContext context,
        ResourceOrchestratorDeployment deployment,
        Exception exception,
        CancellationToken cancellationToken = default);
}

public interface ISqlServerApplicationResourceProviderOperations
{
    Task<ResourcePermissionGrantStatus> GetSqlServerPermissionGrantStatusAsync(
        ResourcePermissionGrantStatusRequest request,
        CancellationToken cancellationToken = default);
}
