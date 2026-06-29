namespace CloudShell.ResourceDefinitions.ResourceManager;

public sealed class ResourceDefinitionTemplateService(
    ResourceGraphModel graphModel,
    ResourceGraphResolver graphResolver,
    ResourceModelGraphDefinitionApplyService definitionApply)
{
    public async ValueTask<ResourceDefinitionTemplateExportResult> ExportTemplateAsync(
        string name,
        ResourceDefinitionResolutionContext? resolutionContext = null,
        IReadOnlyList<string>? resourceIds = null,
        string? environmentId = null,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var snapshot = await graphModel.GetSnapshotAsync(cancellationToken);
        var requestedResourceIds = NormalizeResourceIds(resourceIds);
        var resourceIdsToExport = requestedResourceIds.Count == 0
            ? snapshot.Resources.Select(resource => resource.EffectiveResourceId).ToArray()
            : requestedResourceIds;
        var diagnostics = new List<ResourceDefinitionDiagnostic>();
        var definitions = new List<ResourceDefinition>();

        foreach (var resourceId in resourceIdsToExport)
        {
            var resolution = graphResolver.ResolveResource(
                snapshot,
                resourceId,
                resolutionContext);
            diagnostics.AddRange(resolution.Diagnostics);
            if (resolution.Resource is null)
            {
                continue;
            }

            definitions.Add(resolution.Resource.ToDefinition());
        }

        return new ResourceDefinitionTemplateExportResult(
            new ResourceTemplate(
                name.Trim(),
                definitions,
                environmentId,
                metadata),
            diagnostics);
    }

    public async ValueTask<ResourceDefinitionTemplateApplyResult> ApplyTemplateAsync(
        ResourceTemplate template,
        ResourceGraphCommitContext commitContext,
        ResourceModelGraphDefinitionApplyOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(commitContext);

        var result = await definitionApply.ApplyTemplateAsync(
            template,
            commitContext,
            options ?? ResourceModelGraphDefinitionApplyOptions.CreateMissing,
            cancellationToken);

        return new ResourceDefinitionTemplateApplyResult(template, result);
    }

    private static IReadOnlyList<string> NormalizeResourceIds(IReadOnlyList<string>? resourceIds) =>
        resourceIds?
            .Where(resourceId => !string.IsNullOrWhiteSpace(resourceId))
            .Select(resourceId => resourceId.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];
}

public sealed record ResourceDefinitionTemplateExportResult(
    ResourceTemplate Template,
    IReadOnlyList<ResourceDefinitionDiagnostic> Diagnostics)
{
    public bool HasErrors => Diagnostics.Any(diagnostic =>
        diagnostic.Severity == ResourceDefinitionDiagnosticSeverity.Error);
}

public sealed record ResourceDefinitionTemplateApplyResult(
    ResourceTemplate Template,
    ResourceModelGraphDefinitionApplyResult Apply)
{
    public bool HasErrors => Apply.HasErrors;

    public bool IsCommitted => Apply.IsCommitted;

    public IReadOnlyList<ResourceDefinitionDiagnostic> Diagnostics => Apply.Diagnostics;
}
