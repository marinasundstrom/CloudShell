namespace CloudShell.ResourceDefinitions.ReferenceProviders;

public interface IExecutableApplicationRuntimeController
{
    ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> StartAsync(
        Resource resource,
        CancellationToken cancellationToken = default);
}

public sealed class NoopExecutableApplicationRuntimeController :
    IExecutableApplicationRuntimeController
{
    public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> StartAsync(
        Resource resource,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>([]);
}
