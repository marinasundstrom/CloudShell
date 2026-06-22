using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Providers.Applications;

public interface IApplicationResourceProjectionSource
{
    IReadOnlyList<Resource> GetResources(ApplicationResourceProjection projection);
}

public sealed record ApplicationResourceProjection(
    Func<ApplicationResourceDefinition, bool> CanProject,
    Func<ApplicationResourceDefinition, string> GetResourceKind,
    Func<ApplicationResourceDefinition, string> GetResourceVersion,
    Func<ApplicationResourceDefinition, string> GetWorkloadKind,
    Func<ApplicationResourceDefinition, ResourceClass> GetResourceClass);
