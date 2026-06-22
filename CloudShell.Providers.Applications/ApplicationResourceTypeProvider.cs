using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Providers.Applications;

public abstract class ApplicationResourceTypeProvider(
    IApplicationResourceProjectionSource projections,
    ApplicationResourceService applications) :
    IResourceProvider,
    IResourceProcedureProvider,
    IResourceTemplateProvider,
    IProgrammaticResourceDeclarationProvider,
    IResourceAutoStartPolicyProvider,
    IResourceOrchestrationDescriptorProvider,
    IResourceActionAvailabilityProvider
{
    public abstract string Id { get; }

    public string DisplayName => "Applications";

    protected ApplicationResourceService Applications { get; } = applications;

    protected abstract ApplicationResourceProjection Projection { get; }

    public IReadOnlyList<Resource> GetResources() =>
        projections.GetResources(Projection);

    public Task<ResourceProcedureResult> DeleteAsync(
        ResourceProcedureContext context,
        CancellationToken cancellationToken = default) =>
        Applications.DeleteAsync(context, cancellationToken);

    public Task<ResourceProcedureResult> ExecuteActionAsync(
        ResourceProcedureContext context,
        ResourceAction action,
        CancellationToken cancellationToken = default) =>
        Applications.ExecuteActionAsync(context, action, cancellationToken);

    public bool CanExport(Resource resource) =>
        Projection.CanProject(Applications.GetApplication(resource.Id) ?? EmptyDefinition(resource)) &&
        Applications.CanExport(resource);

    public async Task<ResourceTemplateDefinition> ExportAsync(
        Resource resource,
        ResourceTemplateExportContext context,
        CancellationToken cancellationToken = default)
    {
        var template = await Applications.ExportAsync(resource, context, cancellationToken);
        return template with { ProviderId = Id };
    }

    public bool CanImport(ResourceTemplateDefinition template) =>
        string.Equals(template.ProviderId, Id, StringComparison.OrdinalIgnoreCase) &&
        Projection.CanProject(EmptyDefinition(template.ResourceId ?? template.Name, template.ResourceType)) &&
        Applications.CanImport(template);

    public Task<ResourceTemplateImportResult> ImportAsync(
        ResourceTemplateDefinition template,
        ResourceTemplateImportContext context,
        CancellationToken cancellationToken = default) =>
        Applications.ImportAsync(template, context, cancellationToken);

    public bool CanApplyDeclaration(ResourceDeclaration declaration) =>
        string.Equals(declaration.ProviderId, Id, StringComparison.OrdinalIgnoreCase);

    public Task ApplyDeclarationAsync(
        ResourceDeclaration declaration,
        IResourceRegistrationStore registrations,
        CancellationToken cancellationToken = default) =>
        Applications.ApplyDeclarationAsync(declaration, registrations, cancellationToken);

    public bool CanEvaluateAutoStartPolicy(ResourceDeclaration declaration) =>
        CanApplyDeclaration(declaration);

    public ResourceAutoStartPolicy GetAutoStartPolicy(ResourceDeclaration declaration) =>
        Applications.GetAutoStartPolicy(declaration);

    public bool CanDescribe(Resource resource) =>
        Applications.CanDescribe(resource) &&
        Projection.CanProject(Applications.GetApplication(resource.Id) ?? EmptyDefinition(resource));

    public Task<ResourceOrchestrationDescriptor> DescribeAsync(
        Resource resource,
        ResourceOrchestrationDescriptorContext context,
        CancellationToken cancellationToken = default) =>
        Applications.DescribeAsync(resource, context, cancellationToken);

    public bool CanEvaluateAction(Resource resource, ResourceAction action) =>
        Applications.CanEvaluateAction(resource, action);

    public Task<string?> GetActionUnavailableReasonAsync(
        ResourceProcedureContext context,
        ResourceAction action,
        CancellationToken cancellationToken = default) =>
        Applications.GetActionUnavailableReasonAsync(context, action, cancellationToken);

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
