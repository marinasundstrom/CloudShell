using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Providers.Applications;

internal sealed class ExecutableApplicationResourceProvider(
    IApplicationResourceProjectionSource projections,
    IApplicationResourceDefinitionSource definitions,
    IApplicationResourceProcedureOperations procedures,
    IApplicationResourceDeclarationOperations declarations,
    IApplicationResourceDescriptorOperations descriptors,
    IApplicationResourceActionAvailabilityOperations actions)
    : ApplicationResourceTypeProvider(
        projections,
        definitions,
        procedures,
        declarations,
        descriptors,
        actions)
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
