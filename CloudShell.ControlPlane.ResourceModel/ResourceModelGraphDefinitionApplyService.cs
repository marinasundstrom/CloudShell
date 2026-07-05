using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.ControlPlane.ResourceModel;

public sealed class ResourceModelGraphDefinitionApplyService(
    ResourceGraphModel graphModel,
    ResourceDefinitionGraphChangeApplier changeApplier,
    IEnumerable<IResourceModelGraphApplyReconciler>? reconcilers = null,
    IEnumerable<ResourceDeclarationStore>? declarationStores = null)
{
    private readonly IReadOnlyList<IResourceModelGraphApplyReconciler> _reconcilers =
        (reconcilers ?? []).ToArray();
    private readonly ResourceDeclarationStore? _declarations =
        declarationStores?.LastOrDefault();

    public async ValueTask<ResourceModelGraphDefinitionApplyResult> ApplyDefinitionsAsync(
        IEnumerable<ResourceDefinition> definitions,
        ResourceGraphCommitContext commitContext,
        CancellationToken cancellationToken = default) =>
        await ApplyDefinitionsAsync(
            definitions,
            commitContext,
            ResourceModelGraphDefinitionApplyOptions.Default,
            cancellationToken);

    public async ValueTask<ResourceModelGraphDefinitionApplyResult> ApplyDefinitionsAsync(
        IEnumerable<ResourceDefinition> definitions,
        ResourceGraphCommitContext commitContext,
        ResourceModelGraphDefinitionApplyOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(commitContext);
        ArgumentNullException.ThrowIfNull(options);

        var snapshot = await graphModel.GetSnapshotAsync(cancellationToken);
        var changes = await changeApplier.ApplyDefinitionsAsync(
            snapshot,
            definitions,
            new ResourceChangeApplyContext(
                commitContext.EnvironmentId,
                commitContext.PrincipalId,
                Commit: true),
            new ResourceDefinitionGraphChangeApplierOptions(options.Mode),
            cancellationToken);
        var commit = await graphModel.CommitAsync(
            changes,
            commitContext,
            cancellationToken);

        var reconciliation = await ReconcileAsync(
            snapshot,
            changes,
            commit,
            commitContext,
            options,
            cancellationToken);

        return new(snapshot, changes, commit, reconciliation);
    }

    public async ValueTask<ResourceModelGraphDefinitionApplyResult> ApplyTemplateAsync(
        ResourceTemplate template,
        ResourceGraphCommitContext commitContext,
        CancellationToken cancellationToken = default) =>
        await ApplyTemplateAsync(
            template,
            commitContext,
            ResourceModelGraphDefinitionApplyOptions.CreateMissing,
            cancellationToken);

    public async ValueTask<ResourceModelGraphDefinitionApplyResult> ApplyTemplateAsync(
        ResourceTemplate template,
        ResourceGraphCommitContext commitContext,
        ResourceModelGraphDefinitionApplyOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(commitContext);
        ArgumentNullException.ThrowIfNull(options);

        var effectiveCommitContext = string.IsNullOrWhiteSpace(commitContext.EnvironmentId) &&
            !string.IsNullOrWhiteSpace(template.EnvironmentId)
                ? commitContext with { EnvironmentId = template.EnvironmentId }
                : commitContext;

        var result = await ApplyDefinitionsAsync(
            template.Resources,
            effectiveCommitContext,
            options,
            cancellationToken);

        if (result.IsCommitted)
        {
            ApplyTemplateDeclarationMetadata(template);
        }

        return result;
    }

    public void ApplyTemplateDeclarationMetadata(ResourceTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);

        if (_declarations is null)
        {
            return;
        }

        foreach (var definition in template.Resources)
        {
            var resourceId = definition.EffectiveResourceId;
            var declarationAttributes = definition.GetDeclarationAttributes();
            if (_declarations.GetDeclaration(resourceId) is not null)
            {
                if (declarationAttributes.Identity is { } identity)
                {
                    _declarations.SetIdentity(
                        resourceId,
                        CreateIdentityBinding(identity));
                }

                if (declarationAttributes.ProvisionIdentityOnStartup is { } provision)
                {
                    _declarations.SetProvisionIdentityOnStartup(
                        resourceId,
                        provision);
                }
            }

            foreach (var grant in declarationAttributes.AccessGrantsOrEmpty)
            {
                _declarations.AddPermissionGrant(CreatePermissionGrant(resourceId, grant));
            }
        }
    }

    public static ResourceIdentityBinding CreateIdentityBinding(
        ResourceIdentityBindingAttribute identity)
    {
        var kind = ParseIdentityBindingKind(identity.Kind);
        return kind == ResourceIdentityBindingKind.Required
            ? ResourceIdentityBinding.RequireIdentity(identity.Scopes, identity.Claims) with
            {
                Name = NormalizeOptional(identity.Name),
                Subject = NormalizeOptional(identity.Subject)
            }
            : new ResourceIdentityBinding(
                identity.ProviderId,
                NormalizeOptional(identity.Subject),
                NormalizeList(identity.Scopes),
                NormalizeDictionary(identity.Claims),
                kind,
                NormalizeOptional(identity.Name));
    }

    private static ResourcePermissionGrant CreatePermissionGrant(
        string targetResourceId,
        ResourceAccessGrantAttribute grant) =>
        new(
            CreatePrincipalReference(grant.Principal),
            RequireValue(targetResourceId, nameof(targetResourceId)),
            RequireValue(grant.Permission, nameof(grant.Permission)));

    private static ResourcePrincipalReference CreatePrincipalReference(
        ResourcePrincipalReferenceAttribute principal) =>
        new(
            ParsePrincipalKind(principal.Kind),
            RequireValue(principal.Id, nameof(principal.Id)),
            NormalizeOptional(principal.DisplayName),
            NormalizeOptional(principal.ProviderId),
            NormalizeOptional(principal.SourceResourceId),
            NormalizeOptional(principal.SourceIdentityName));

    private static ResourceIdentityBindingKind ParseIdentityBindingKind(string? kind) =>
        string.Equals(kind, ResourceIdentityBindingAttributeKinds.Required, StringComparison.OrdinalIgnoreCase)
            ? ResourceIdentityBindingKind.Required
            : ResourceIdentityBindingKind.Provider;

    private static ResourcePrincipalKind ParsePrincipalKind(string kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);

        return NormalizeKind(kind) switch
        {
            "resourceidentity" => ResourcePrincipalKind.ResourceIdentity,
            "user" => ResourcePrincipalKind.User,
            "group" => ResourcePrincipalKind.Group,
            "serviceaccount" => ResourcePrincipalKind.ServiceAccount,
            "serviceprincipal" => ResourcePrincipalKind.ServicePrincipal,
            "managedidentity" => ResourcePrincipalKind.ManagedIdentity,
            "workloadidentity" => ResourcePrincipalKind.WorkloadIdentity,
            "external" => ResourcePrincipalKind.External,
            "deviceidentity" => ResourcePrincipalKind.DeviceIdentity,
            _ => Enum.TryParse<ResourcePrincipalKind>(kind, ignoreCase: true, out var parsed)
                ? parsed
                : throw new ArgumentException($"Unknown resource principal kind '{kind}'.", nameof(kind))
        };
    }

    private static string NormalizeKind(string kind) =>
        new(kind.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static IReadOnlyList<string>? NormalizeList(IReadOnlyList<string>? values) =>
        values?
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static IReadOnlyDictionary<string, string>? NormalizeDictionary(
        IReadOnlyDictionary<string, string>? values)
    {
        if (values is null || values.Count == 0)
        {
            return null;
        }

        var normalized = values
            .Where(value => !string.IsNullOrWhiteSpace(value.Key) &&
                !string.IsNullOrWhiteSpace(value.Value))
            .ToDictionary(
                value => value.Key.Trim(),
                value => value.Value.Trim(),
                StringComparer.OrdinalIgnoreCase);
        return normalized.Count == 0 ? null : normalized;
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string RequireValue(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value.Trim();
    }

    private async ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ReconcileAsync(
        ResourceGraphSnapshot snapshot,
        ResourceGraphChangeSet changes,
        ResourceGraphCommitResult commit,
        ResourceGraphCommitContext commitContext,
        ResourceModelGraphDefinitionApplyOptions options,
        CancellationToken cancellationToken)
    {
        if (!options.ReconcileRuntime ||
            !commit.IsCommitted ||
            commit.Summary.Status != ResourceGraphCommitStatus.Committed ||
            _reconcilers.Count == 0)
        {
            return [];
        }

        var context = new ResourceModelGraphDefinitionApplyReconciliationContext(
            snapshot,
            changes,
            commit,
            commitContext);
        var diagnostics = new List<ResourceDefinitionDiagnostic>();

        foreach (var reconciler in _reconcilers)
        {
            diagnostics.AddRange(await reconciler.ReconcileAsync(
                context,
                cancellationToken));
        }

        return diagnostics;
    }
}

public sealed record ResourceModelGraphDefinitionApplyOptions(
    ResourceDefinitionApplyMode Mode = ResourceDefinitionApplyMode.UpdateExisting,
    bool ReconcileRuntime = true)
{
    public static ResourceModelGraphDefinitionApplyOptions Default { get; } = new();

    public static ResourceModelGraphDefinitionApplyOptions CreateMissing { get; } =
        new(ResourceDefinitionApplyMode.CreateOrUpdate);

    public ResourceModelGraphDefinitionApplyOptions WithoutRuntimeReconciliation() =>
        this with { ReconcileRuntime = false };
}

public sealed record ResourceModelGraphDefinitionApplyResult(
    ResourceGraphSnapshot BaseSnapshot,
    ResourceGraphChangeSet Changes,
    ResourceGraphCommitResult Commit,
    IReadOnlyList<ResourceDefinitionDiagnostic>? ReconciliationDiagnostics = null)
{
    public ResourceGraphVersion BaseVersion => BaseSnapshot.Version;

    public bool HasErrors =>
        Changes.HasErrors ||
        Commit.HasErrors ||
        ReconciliationDiagnosticsOrEmpty.Any(diagnostic =>
            diagnostic.Severity == ResourceDefinitionDiagnosticSeverity.Error);

    public bool IsCommitted => Commit.IsCommitted;

    public IReadOnlyList<ResourceDefinitionDiagnostic> Diagnostics =>
        ReconciliationDiagnosticsOrEmpty.Count > 0
            ? [.. Commit.Diagnostics, .. Changes.Diagnostics, .. ReconciliationDiagnosticsOrEmpty]
            : Commit.Diagnostics.Count > 0
                ? Commit.Diagnostics
                : Changes.Diagnostics;

    private IReadOnlyList<ResourceDefinitionDiagnostic> ReconciliationDiagnosticsOrEmpty =>
        ReconciliationDiagnostics ?? [];
}

public interface IResourceModelGraphApplyReconciler
{
    ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ReconcileAsync(
        ResourceModelGraphDefinitionApplyReconciliationContext context,
        CancellationToken cancellationToken = default);
}

public sealed record ResourceModelGraphDefinitionApplyReconciliationContext(
    ResourceGraphSnapshot BaseSnapshot,
    ResourceGraphChangeSet Changes,
    ResourceGraphCommitResult Commit,
    ResourceGraphCommitContext CommitContext);
