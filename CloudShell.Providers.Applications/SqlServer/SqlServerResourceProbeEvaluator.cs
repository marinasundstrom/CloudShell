using CloudShell.Abstractions.ResourceManager;
using Microsoft.Data.SqlClient;
using System.Globalization;

namespace CloudShell.Providers.Applications;

internal sealed class SqlServerResourceProbeEvaluator(ApplicationResourceStore store) : IResourceProbeEvaluator
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

    public bool CanEvaluate(Resource resource, ResourceHealthCheck check) =>
        string.Equals(resource.EffectiveTypeId, ApplicationResourceTypes.SqlServer, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(
            check.EffectiveSource.Kind,
            ApplicationResourceProbeSourceKinds.SqlServer,
            StringComparison.OrdinalIgnoreCase);

    public async Task<ResourceHealthCheckResult> EvaluateAsync(
        Resource resource,
        ResourceHealthCheck check,
        CancellationToken cancellationToken = default)
    {
        var definition = store.GetApplication(resource.Id);
        if (definition is null)
        {
            return new ResourceHealthCheckResult(
                check,
                ResourceHealthStatus.Unknown,
                "SQL Server definition was not found.",
                null,
                ResourceHealthCheckOutcome.Unresolved);
        }

        var connectionUri = ResolveConnectionUri(resource, check);
        if (connectionUri is null)
        {
            return new ResourceHealthCheckResult(
                check,
                ResourceHealthStatus.Unknown,
                "No resolved SQL Server TDS endpoint.",
                null,
                ResourceHealthCheckOutcome.Unresolved);
        }

        if (!TryCreateConnectionString(definition, connectionUri, GetDatabaseName(check), GetTimeout(check), out var connectionString))
        {
            return new ResourceHealthCheckResult(
                check,
                ResourceHealthStatus.Unknown,
                "SQL Server credentials or endpoint are unavailable.",
                connectionUri,
                ResourceHealthCheckOutcome.Unresolved);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(GetTimeout(check));

        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(timeout.Token);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            command.CommandTimeout = Math.Max(1, (int)Math.Ceiling(GetTimeout(check).TotalSeconds));
            await command.ExecuteScalarAsync(timeout.Token);

            return new ResourceHealthCheckResult(
                check,
                ResourceHealthStatus.Healthy,
                "SQL Server accepted a liveness connection.",
                connectionUri,
                ResourceHealthCheckOutcome.Responded);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ResourceHealthCheckResult(
                check,
                ResourceHealthStatus.Unhealthy,
                "Timed out",
                connectionUri,
                ResourceHealthCheckOutcome.NoResponse);
        }
        catch (SqlException exception)
        {
            return new ResourceHealthCheckResult(
                check,
                ResourceHealthStatus.Unhealthy,
                exception.Message,
                connectionUri,
                ResourceHealthCheckOutcome.NoResponse);
        }
        catch (InvalidOperationException exception)
        {
            return new ResourceHealthCheckResult(
                check,
                ResourceHealthStatus.Unhealthy,
                exception.Message,
                connectionUri,
                ResourceHealthCheckOutcome.NoResponse);
        }
    }

    private static Uri? ResolveConnectionUri(Resource resource, ResourceHealthCheck check)
    {
        var endpointName = GetMetadata(check, "endpoint") ?? "tds";
        return resource.TryGetResolvedEndpointUri(endpointName, out var endpoint)
            ? endpoint
            : null;
    }

    private static string GetDatabaseName(ResourceHealthCheck check) =>
        GetMetadata(check, "database") ?? "master";

    private static TimeSpan GetTimeout(ResourceHealthCheck check) =>
        check.Timeout ?? DefaultTimeout;

    private static string? GetMetadata(ResourceHealthCheck check, string name) =>
        check.EffectiveSource.Metadata?.TryGetValue(name, out var value) == true &&
        !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : null;

    private static bool TryCreateConnectionString(
        ApplicationResourceDefinition definition,
        Uri endpoint,
        string databaseName,
        TimeSpan timeout,
        out string connectionString)
    {
        connectionString = string.Empty;

        var password = definition.EnvironmentVariables.FirstOrDefault(variable =>
            string.Equals(variable.Name, "MSSQL_SA_PASSWORD", StringComparison.OrdinalIgnoreCase))?.Value;
        if (string.IsNullOrWhiteSpace(password))
        {
            return false;
        }

        var builder = new SqlConnectionStringBuilder
        {
            DataSource = CreateDataSource(endpoint),
            UserID = "sa",
            Password = password,
            InitialCatalog = string.IsNullOrWhiteSpace(databaseName) ? "master" : databaseName.Trim(),
            Encrypt = false,
            TrustServerCertificate = true,
            ConnectTimeout = Math.Max(1, (int)Math.Ceiling(timeout.TotalSeconds))
        };

        connectionString = builder.ConnectionString;
        return true;
    }

    private static string CreateDataSource(Uri endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint.Host))
        {
            return endpoint.ToString();
        }

        return endpoint.Port > 0
            ? $"{endpoint.Host},{endpoint.Port.ToString(CultureInfo.InvariantCulture)}"
            : endpoint.Host;
    }
}
