namespace CloudShell.ControlPlane.Providers;

public sealed class SqlDatabaseEnsureCreatedExecutionHandler(
    ISqlDatabaseCreationHandler? creationHandler = null,
    ISqlDatabaseServerResolver? serverResolver = null) : IProviderExecutionHandler
{
    private readonly ISqlDatabaseCreationHandler _creationHandler =
        creationHandler ?? new NoopSqlDatabaseCreationHandler();
    private readonly ISqlDatabaseServerResolver _serverResolver =
        serverResolver ?? new ContextSqlDatabaseServerResolver();

    public string InstructionType =>
        ProviderExecutionInstructionTypes.SqlDatabaseEnsureCreated;

    public IReadOnlyList<string> Capabilities =>
    [
        ProviderExecutionCapabilities.SqlDatabaseCreation
    ];

    public async ValueTask<ProviderExecutionResult> ExecuteAsync(
        ProviderExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        var context = request.TryCreateProjectionExecutionContext();
        if (context is null)
        {
            return ProviderExecutionResult.Unavailable(
                request,
                ProviderExecutionDiagnosticCodes.ResourceSnapshotMissing,
                $"Provider execution instruction '{request.InstructionType}' requires a resource snapshot for '{request.TargetResourceId}'.");
        }

        if (!SqlDatabaseResourceTypeProvider.TryGetServerDependencyResourceId(
                context.Resource.State,
                out var serverResourceId))
        {
            return ProviderExecutionResult.Failed(
                request,
                [
                    ResourceDefinitionDiagnostic.Error(
                        "application.sqlDatabase.serverReferenceRequired",
                        "SQL database owning server reference is required.",
                        context.Resource.EffectiveResourceId)
                ]);
        }

        var server = await _serverResolver.ResolveServerAsync(
            context.Resource,
            context,
            cancellationToken);
        if (server is null)
        {
            return ProviderExecutionResult.Failed(
                request,
                [
                    ResourceDefinitionDiagnostic.Error(
                        ResourceDefinitionDiagnosticCodes.ResourceGraphResourceMissing,
                        $"SQL database owning server resource '{serverResourceId}' was not resolved.",
                        serverResourceId)
                ]);
        }

        var diagnostics = await _creationHandler.EnsureCreatedAsync(
            new SqlDatabaseCreationContext(context.Resource, server),
            cancellationToken);

        return diagnostics.Any(diagnostic =>
            diagnostic.Severity == ResourceDefinitionDiagnosticSeverity.Error)
            ? ProviderExecutionResult.Failed(request, diagnostics)
            : ProviderExecutionResult.Succeeded(request, diagnostics: diagnostics);
    }
}
