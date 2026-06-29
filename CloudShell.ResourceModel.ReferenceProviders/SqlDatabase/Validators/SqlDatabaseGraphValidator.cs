namespace CloudShell.ResourceModel.ReferenceProviders;

public sealed class SqlDatabaseGraphValidator : IResourceDefinitionGraphValidator
{
    public ValueTask<ResourceDefinitionValidationResult> ValidateAsync(
        ResourceDefinitionGraphValidationContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var diagnostics = new List<ResourceDefinitionDiagnostic>();

        foreach (var resource in context.Resources.Where(resource =>
            resource.Type.TypeId == SqlDatabaseResourceTypeProvider.ResourceTypeId))
        {
            if (!SqlDatabaseResourceTypeProvider.TryGetServerDependencyResourceId(
                    resource.State,
                    out var serverResourceId))
            {
                continue;
            }

            var server = context.FindResource(serverResourceId);
            if (server is null)
            {
                diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                    ResourceDefinitionDiagnosticCodes.ResourceDependencyMissing,
                    $"SQL database '{resource.EffectiveResourceId}' references missing SQL Server resource '{serverResourceId}'.",
                    resource.EffectiveResourceId));
                continue;
            }

            if (server.Type.TypeId != SqlServerResourceTypeProvider.ResourceTypeId)
            {
                diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                    ResourceDefinitionDiagnosticCodes.ResourceCapabilityReferenceInvalid,
                    $"SQL database '{resource.EffectiveResourceId}' references resource type '{server.Type.TypeId}', expected '{SqlServerResourceTypeProvider.ResourceTypeId}'.",
                    resource.EffectiveResourceId));
            }
        }

        return ValueTask.FromResult(
            ResourceDefinitionValidationResult.FromDiagnostics(diagnostics));
    }
}
