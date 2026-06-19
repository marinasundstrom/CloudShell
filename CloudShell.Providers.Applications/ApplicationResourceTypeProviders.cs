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

internal sealed class ExecutableApplicationResourceProvider(
    IApplicationResourceProjectionSource projections,
    ApplicationResourceService applications)
    : ApplicationResourceTypeProvider(projections, applications)
{
    public const string ProviderId = ApplicationResourceProviderIds.Executable;

    public override string Id => ProviderId;

    protected override ApplicationResourceProjection Projection { get; } = new(
        application => string.Equals(
            application.ResourceType,
            ApplicationResourceTypes.ExecutableApplication,
            StringComparison.OrdinalIgnoreCase),
        application => ApplicationResourceProjectionSupport.IsContainerBacked(application)
            ? "Container app"
            : "Executable application",
        application => ApplicationResourceProjectionSupport.IsContainerBacked(application)
            ? ApplicationResourceProjectionSupport.GetContainerVersion(application)
            : Path.GetFileName(application.ExecutablePath),
        application => ApplicationResourceProjectionSupport.IsContainerBacked(application)
            ? ApplicationResourceProjectionSupport.GetContainerWorkloadKind(application)
            : ResourceWorkloadKind.LocalExecutable.ToString(),
        application => ApplicationResourceProjectionSupport.IsContainerBacked(application)
            ? ResourceClass.Container
            : ResourceClass.Executable);
}

internal sealed class AspNetCoreProjectResourceProvider(
    IApplicationResourceProjectionSource projections,
    ApplicationResourceService applications)
    : ApplicationResourceTypeProvider(projections, applications)
{
    public const string ProviderId = ApplicationResourceProviderIds.AspNetCoreProject;

    public override string Id => ProviderId;

    protected override ApplicationResourceProjection Projection { get; } = new(
        application => string.Equals(
            application.ResourceType,
            ApplicationResourceTypes.AspNetCoreProject,
            StringComparison.OrdinalIgnoreCase),
        _ => "ASP.NET Core project",
        application => ApplicationResourceProjectionSupport.FirstNonEmpty(
            Path.GetFileName(application.ProjectPath),
            "project") ?? "project",
        _ => ResourceWorkloadKind.AspNetCoreProject.ToString(),
        _ => ResourceClass.Project);
}

internal sealed class ContainerApplicationResourceProvider(
    IApplicationResourceProjectionSource projections,
    ApplicationResourceService applications)
    : ApplicationResourceTypeProvider(projections, applications)
{
    public const string ProviderId = ApplicationResourceProviderIds.ContainerApplication;

    public override string Id => ProviderId;

    protected override ApplicationResourceProjection Projection { get; } = new(
        application => ApplicationResourceTypes.IsContainerApp(application.ResourceType),
        _ => "Container app",
        ApplicationResourceProjectionSupport.GetContainerVersion,
        ApplicationResourceProjectionSupport.GetContainerWorkloadKind,
        _ => ResourceClass.Container);
}

internal sealed class SqlServerApplicationResourceProvider(
    IApplicationResourceProjectionSource projections,
    ApplicationResourceService applications)
    : ApplicationResourceTypeProvider(projections, applications)
{
    public const string ProviderId = ApplicationResourceProviderIds.SqlServer;

    public override string Id => ProviderId;

    protected override ApplicationResourceProjection Projection { get; } = new(
        application => string.Equals(
            application.ResourceType,
            ApplicationResourceTypes.SqlServer,
            StringComparison.OrdinalIgnoreCase),
        _ => "SQL Server",
        ApplicationResourceProjectionSupport.GetContainerVersion,
        ApplicationResourceProjectionSupport.GetContainerWorkloadKind,
        _ => ResourceClass.Service);
}

internal static class ApplicationResourceProjectionSupport
{
    public static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

    public static bool IsContainerBacked(ApplicationResourceDefinition application) =>
        !string.IsNullOrWhiteSpace(application.ContainerImage) ||
        application.ProjectContainerBuild ||
        !string.IsNullOrWhiteSpace(application.ContainerBuildContext);

    public static string GetContainerVersion(ApplicationResourceDefinition application) =>
        ApplicationResourceTypes.IsContainerApp(application.ResourceType)
            ? FirstNonEmpty(application.ContainerRevision) ?? "unrevisioned"
            : FirstNonEmpty(application.ContainerImage, application.ContainerBuildContext) ?? "container";

    public static string GetContainerWorkloadKind(ApplicationResourceDefinition application)
    {
        if (!string.IsNullOrWhiteSpace(application.ContainerImage))
        {
            return ResourceWorkloadKind.ContainerImage.ToString();
        }

        if (application.ProjectContainerBuild ||
            !string.IsNullOrWhiteSpace(application.ContainerBuildContext))
        {
            return ResourceWorkloadKind.ContainerBuild.ToString();
        }

        return ResourceWorkloadKind.LocalExecutable.ToString();
    }
}
