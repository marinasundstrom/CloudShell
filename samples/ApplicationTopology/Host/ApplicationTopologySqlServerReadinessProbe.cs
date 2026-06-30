using CloudShell.ControlPlane.Providers;
using Resource = CloudShell.ResourceModel.Resource;

namespace CloudShell.ApplicationTopologyHost;

public sealed class ApplicationTopologySqlServerReadinessProbe(
    IConfiguration configuration) : ILocalSqlServerReadinessProbe
{
    public async Task WaitUntilReadyAsync(
        Resource resource,
        CancellationToken cancellationToken)
    {
        if (!ResourceModelSqlServerConnectionSupport.TryCreateAdministratorConnectionString(
                resource,
                configuration,
                "master",
                out var connectionString))
        {
            throw new InvalidOperationException(
                $"SQL Server resource '{resource.Name}' cannot be started because its TDS endpoint or administrator password is not available.");
        }

        await using var connection = await ResourceModelSqlServerConnectionSupport.OpenWithRetryAsync(
            resource,
            connectionString,
            cancellationToken);
    }
}
