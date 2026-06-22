using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Providers.Applications;

internal sealed class AspNetCoreProjectResourceProvider(
    IApplicationResourceProjectionSource projections,
    IApplicationResourceDefinitionSource definitions,
    IApplicationResourceProcedureOperations procedures,
    IApplicationResourceTemplateOperations templates,
    IApplicationResourceDeclarationOperations declarations,
    IApplicationResourceDescriptorOperations descriptors,
    IApplicationResourceActionAvailabilityOperations actions)
    : ApplicationResourceTypeProvider(
        projections,
        definitions,
        procedures,
        templates,
        declarations,
        descriptors,
        actions)
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
