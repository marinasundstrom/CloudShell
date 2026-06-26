using System.Text.Json;
using CloudShell.Abstractions.ResourceManager;
using ResourceManagerResource = CloudShell.Abstractions.ResourceManager.Resource;

namespace CloudShell.ResourceDefinitions.ResourceManager;

public sealed class ResourceModelGraphProcedureProvider :
    IResourceProvider,
    IResourceModelDiagnosticProvider,
    IResourceProcedureProvider,
    IResourceActionAvailabilityProvider,
    IResourceTemplateProvider
{
    public const string ResourceDefinitionTemplateConfigurationVersion = "resource-definition.v1";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerOptions.Web);

    private readonly ResourceModelGraphResourceProvider _resourceProvider;
    private readonly ResourceModelGraphResourceResolver _resourceResolver;
    private readonly ResourceModelGraphDefinitionApplyService _definitionApply;
    private readonly ResourceDefinitionResolutionContext _resolutionContext;

    public ResourceModelGraphProcedureProvider(
        ResourceModelGraphResourceProvider resourceProvider,
        ResourceModelGraphResourceResolver resourceResolver,
        ResourceModelGraphDefinitionApplyService definitionApply,
        ResourceDefinitionResolutionContext? resolutionContext = null)
    {
        _resourceProvider = resourceProvider ?? throw new ArgumentNullException(nameof(resourceProvider));
        _resourceResolver = resourceResolver ?? throw new ArgumentNullException(nameof(resourceResolver));
        _definitionApply = definitionApply ?? throw new ArgumentNullException(nameof(definitionApply));
        _resolutionContext = resolutionContext ?? ResourceDefinitionResolutionContext.Empty;
    }

    public string Id => _resourceProvider.Id;

    public string DisplayName => _resourceProvider.DisplayName;

    public IReadOnlyList<ResourceManagerResource> GetResources() =>
        _resourceProvider.GetResources();

    public IReadOnlyList<ResourceModelDiagnostic> GetResourceModelDiagnostics() =>
        _resourceProvider.GetResourceModelDiagnostics();

    public bool CanExport(ResourceManagerResource resource) =>
        IsBridgeResource(resource);

    public async Task<ResourceTemplateDefinition> ExportAsync(
        ResourceManagerResource resource,
        ResourceTemplateExportContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(context);

        var resolution = await _resourceResolver.ResolveAsync(
            resource.Id,
            _resolutionContext,
            cancellationToken);
        if (resolution.Target is null)
        {
            throw new InvalidOperationException(
                $"Resource model graph resource '{resource.Id}' could not be resolved.");
        }

        if (resolution.HasErrors)
        {
            throw new InvalidOperationException(FormatDiagnostics(resolution.Diagnostics));
        }

        var definition = resolution.Target.ToDefinition();

        return new ResourceTemplateDefinition(
            definition.Name,
            Id,
            definition.TypeId.ToString(),
            definition.StartupDependencyIds,
            ResourceDefinitionTemplateConfigurationVersion,
            ResourceDefinitionJson.FromValue(definition, SerializerOptions),
            definition.EffectiveResourceId);
    }

    public bool CanImport(ResourceTemplateDefinition template) =>
        string.Equals(template.ProviderId, Id, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(
            template.ProviderConfigurationVersion,
            ResourceDefinitionTemplateConfigurationVersion,
            StringComparison.OrdinalIgnoreCase);

    public async Task<ResourceTemplateImportResult> ImportAsync(
        ResourceTemplateDefinition template,
        ResourceTemplateImportContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(context);

        if (!CanImport(template))
        {
            throw new InvalidOperationException("The resource model graph template is not supported.");
        }

        var definition = template.Configuration.Deserialize<ResourceDefinition>(SerializerOptions)
            ?? throw new InvalidOperationException("The resource model graph template configuration is invalid.");
        if (!string.Equals(definition.TypeId.ToString(), template.ResourceType, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Resource definition type '{definition.TypeId}' does not match template resource type '{template.ResourceType}'.");
        }

        var resourceId = string.IsNullOrWhiteSpace(template.ResourceId)
            ? definition.EffectiveResourceId
            : template.ResourceId.Trim();
        if (_resourceProvider.GetResources().Any(resource =>
            string.Equals(resource.Id, resourceId, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Resource id '{resourceId}' is already in use.");
        }

        var importDefinition = definition with
        {
            ResourceId = resourceId,
            DependsOn = context.DependsOn
                .Select(resourceId => ResourceReference.DependsOnResourceId(resourceId))
                .ToArray()
        };
        var result = await _definitionApply.ApplyDefinitionsAsync(
            [importDefinition],
            new ResourceGraphCommitContext(),
            ResourceModelGraphDefinitionApplyOptions.CreateMissing,
            cancellationToken);

        if (result.HasErrors || !result.IsCommitted)
        {
            throw new InvalidOperationException(FormatDiagnostics(result.Diagnostics));
        }

        await context.Registrations.RegisterAsync(
            Id,
            resourceId,
            context.ResourceGroupId,
            context.DependsOn,
            cancellationToken);

        return new ResourceTemplateImportResult(
            resourceId,
            $"Imported resource model graph resource '{template.Name}'.");
    }

    public Task<ResourceProcedureResult> DeleteAsync(
        ResourceProcedureContext context,
        CancellationToken cancellationToken = default) =>
        Task.FromException<ResourceProcedureResult>(
            new NotSupportedException("Resource model graph resources are not deleted through this bridge provider."));

    public bool CanEvaluateAction(
        ResourceManagerResource resource,
        ResourceAction action) =>
        IsBridgeResource(resource) &&
        resource.HasAction(action.Id);

    public async Task<string?> GetActionUnavailableReasonAsync(
        ResourceProcedureContext context,
        ResourceAction action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(action);

        var resolution = await ResolveExecutableOperationAsync(
            context.Resource.Id,
            action,
            cancellationToken);
        var blockingGraphDiagnostics = await ResolveBlockingGraphDiagnosticsAsync(
            context.Resource.Id,
            cancellationToken);

        if (blockingGraphDiagnostics.Count > 0)
        {
            return FormatDiagnostics(blockingGraphDiagnostics);
        }

        if (resolution.Diagnostics.Count > 0)
        {
            return FormatDiagnostics(resolution.Diagnostics);
        }

        if (resolution.Operation is not IResourceOperationExecutorProjection executableOperation)
        {
            return $"Resource model operation '{resolution.OperationId}' does not support execution.";
        }

        return await executableOperation.CanExecuteAsync(cancellationToken)
            ? null
            : $"Resource model operation '{resolution.OperationId}' cannot execute.";
    }

    public async Task<ResourceProcedureResult> ExecuteActionAsync(
        ResourceProcedureContext context,
        ResourceAction action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(action);

        var resolution = await ResolveExecutableOperationAsync(
            context.Resource.Id,
            action,
            cancellationToken);
        var blockingGraphDiagnostics = await ResolveBlockingGraphDiagnosticsAsync(
            context.Resource.Id,
            cancellationToken);

        if (blockingGraphDiagnostics.Count > 0)
        {
            throw new InvalidOperationException(FormatDiagnostics(blockingGraphDiagnostics));
        }

        if (resolution.Diagnostics.Count > 0)
        {
            throw new InvalidOperationException(FormatDiagnostics(resolution.Diagnostics));
        }

        if (resolution.Operation is not IResourceOperationExecutorProjection executableOperation)
        {
            throw new NotSupportedException(
                $"Resource model operation '{resolution.OperationId}' does not support execution.");
        }

        if (!await executableOperation.CanExecuteAsync(cancellationToken))
        {
            throw new InvalidOperationException(
                $"Resource model operation '{resolution.OperationId}' cannot execute.");
        }

        var execution = await executableOperation.ExecuteAsync(cancellationToken);
        if (execution.HasErrors)
        {
            throw new InvalidOperationException(FormatDiagnostics(execution.Diagnostics));
        }

        return ResourceProcedureResult.Completed(
            $"Executed {action.DisplayName} for {context.Resource.Name}.");
    }

    private async ValueTask<ResourceModelGraphOperationResolution> ResolveExecutableOperationAsync(
        string resourceId,
        ResourceAction action,
        CancellationToken cancellationToken) =>
        await _resourceResolver.ResolveOperationAsync(
            resourceId,
            action,
            _resolutionContext,
            cancellationToken);

    private async ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ResolveBlockingGraphDiagnosticsAsync(
        string resourceId,
        CancellationToken cancellationToken)
    {
        var resolution = await _resourceResolver.ResolveWithDependenciesAsync(
            resourceId,
            _resolutionContext,
            cancellationToken);

        return resolution.Diagnostics
            .Where(diagnostic =>
                diagnostic.Code == ResourceDefinitionDiagnosticCodes.ResourceReferenceTypeMismatch)
            .ToArray();
    }

    private static string FormatDiagnostics(
        IReadOnlyList<ResourceDefinitionDiagnostic> diagnostics) =>
        string.Join(
            " ",
            diagnostics.Select(diagnostic =>
                string.IsNullOrWhiteSpace(diagnostic.Target)
                    ? diagnostic.Message
                    : $"{diagnostic.Message} Target: {diagnostic.Target}."));

    private bool IsBridgeResource(ResourceManagerResource resource) =>
        resource.IsDeclaredResource &&
        resource.ResourceAttributes.TryGetValue(
            ResourceModelResourceManagerAttributeNames.BridgeProviderId,
            out var bridgeProviderId) &&
        string.Equals(bridgeProviderId, Id, StringComparison.OrdinalIgnoreCase);
}
