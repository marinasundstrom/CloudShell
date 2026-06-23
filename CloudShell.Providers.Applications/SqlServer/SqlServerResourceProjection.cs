using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Providers.Applications;

internal static class SqlServerResourceProjection
{
    public static Resource? GetProjectedServerResource(
        ApplicationResourceDefinition application,
        IApplicationResourceProjectionSource projections)
    {
        var profile = ApplicationResourceProjectionProfiles.CreateInfrastructureProjection(application);
        var scopedProfile = new ApplicationResourceProjection(
            current => string.Equals(current.Id, application.Id, StringComparison.OrdinalIgnoreCase),
            profile.GetResourceKind,
            profile.GetResourceVersion,
            profile.GetWorkloadKind,
            profile.GetResourceClass);

        return projections
            .GetResources(scopedProfile)
            .FirstOrDefault(resource =>
                string.Equals(resource.Id, application.Id, StringComparison.OrdinalIgnoreCase));
    }
}
