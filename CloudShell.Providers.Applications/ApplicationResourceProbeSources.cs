using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Providers.Applications;

internal static class ApplicationResourceProbeSourceKinds
{
    public const string SqlServer = "application.sql-server";
}

internal static class ApplicationResourceProbeSources
{
    public static ResourceProbeSource SqlServer { get; } =
        new(
            ApplicationResourceProbeSourceKinds.SqlServer,
            Metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["endpoint"] = "tds",
                ["database"] = "master"
            });
}
