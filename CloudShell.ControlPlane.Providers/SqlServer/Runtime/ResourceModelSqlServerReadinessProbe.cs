using Microsoft.Extensions.Configuration;

namespace CloudShell.ControlPlane.Providers;

public sealed class ResourceModelSqlServerReadinessProbe(
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
