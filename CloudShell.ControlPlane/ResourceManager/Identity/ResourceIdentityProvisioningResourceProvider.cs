using CloudShell.Abstractions.Authorization;
using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.ControlPlane.ResourceManager.Identity;

public sealed class ResourceIdentityProvisioningResourceProvider(
    ResourceDeclarationStore declarations,
    ResourceIdentityProviderSetupService setupService) :
    IResourceProvider,
    IResourceProcedureProvider,
    IResourceActionAvailabilityProvider
{
    public const string SetupIdentityProviderActionId = "setupIdentityProvider";

    private static readonly ResourceAction SetupIdentityProviderAction = new(
        SetupIdentityProviderActionId,
        "Set up identity provider",
        Description: "Run setup or reconciliation for the identity provider attached to this provisioning resource.",
        RequiredPermission: ResourceIdentityProvisioningOperationPermissions.ProvisionIdentities);

    public string Id => ResourceIdentityProvisioningResources.ProviderId;

    public string DisplayName => "Identity Provisioning";

    public IReadOnlyList<Resource> GetResources() =>
        declarations.GetDeclarations()
            .Where(declaration => string.Equals(
                declaration.ProviderId,
                Id,
                StringComparison.OrdinalIgnoreCase))
            .Select(CreateResource)
            .OrderBy(resource => resource.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private Resource CreateResource(ResourceDeclaration declaration)
    {
        var attributes = new Dictionary<string, string>(
            declaration.ResourceAttributes,
            StringComparer.OrdinalIgnoreCase);
        attributes.TryAdd(ResourceAttributeNames.InfrastructureKind, "identity-provisioning");

        return new Resource(
            declaration.ResourceId,
            GetResourceName(declaration.ResourceId),
            "Identity Provisioning",
            DisplayName,
            "local",
            null,
            [],
            "n/a",
            declaration.DeclaredAt,
            declaration.DependsOn,
            ParentResourceId: declaration.ParentResourceId,
            TypeId: ResourceIdentityProvisioningResources.ResourceType,
            Actions: [SetupIdentityProviderAction],
            ResourceClass: ResourceClass.Infrastructure,
            Attributes: attributes,
            Source: ResourceSource.User,
            DisplayName: GetDisplayName(declaration));
    }

    public Task<ResourceProcedureResult> DeleteAsync(
        ResourceProcedureContext context,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Identity provisioning resources are declared resources and cannot be deleted by this provider.");

    public bool CanEvaluateAction(Resource resource, ResourceAction action) =>
        string.Equals(resource.EffectiveTypeId, ResourceIdentityProvisioningResources.ResourceType, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(action.Id, SetupIdentityProviderActionId, StringComparison.OrdinalIgnoreCase);

    public Task<string?> GetActionUnavailableReasonAsync(
        ResourceProcedureContext context,
        ResourceAction action,
        CancellationToken cancellationToken = default)
    {
        if (!CanEvaluateAction(context.Resource, action))
        {
            return Task.FromResult<string?>(null);
        }

        var provider = setupService.ResolveProviderForProvisioningResource(context.Resource.Id);
        return Task.FromResult(provider is null
            ? $"No resource identity provider is attached to provisioning resource '{context.Resource.Id}'."
            : null);
    }

    public async Task<ResourceProcedureResult> ExecuteActionAsync(
        ResourceProcedureContext context,
        ResourceAction action,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(action.Id, SetupIdentityProviderActionId, StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException(
                $"Identity provisioning resources do not support action '{action.DisplayName}'.");
        }

        var provider = setupService.ResolveProviderForProvisioningResource(context.Resource.Id)
            ?? throw new InvalidOperationException(
                $"No resource identity provider is attached to provisioning resource '{context.Resource.Id}'.");
        var result = await setupService.SetupAsync(provider.Id, cancellationToken);
        var messages = result.SetupDiagnostics
            .Select(diagnostic => diagnostic.Message)
            .Where(message => !string.IsNullOrWhiteSpace(message))
            .ToArray();
        var message = messages.Length == 0
            ? $"Set up identity provider '{provider.Id}'."
            : $"Set up identity provider '{provider.Id}'. {string.Join(" ", messages)}";

        return ResourceProcedureResult.Completed(message);
    }

    private static string GetResourceName(string resourceId) =>
        ResourceId.TryParse(resourceId, out var id) && !string.IsNullOrWhiteSpace(id.Name)
            ? id.Name
            : resourceId;

    private static string GetDisplayName(ResourceDeclaration declaration)
    {
        if (declaration.ResourceAttributes.TryGetValue("identity.provider", out var provider) &&
            !string.IsNullOrWhiteSpace(provider))
        {
            return $"{provider.Trim()} Identity Provisioning";
        }

        var name = declaration.ResourceId.Contains(':', StringComparison.Ordinal)
            ? declaration.ResourceId[(declaration.ResourceId.IndexOf(':', StringComparison.Ordinal) + 1)..]
            : declaration.ResourceId;
        return string.Join(
            " ",
            name.Split(['-', '_', '.', ':'], StringSplitOptions.RemoveEmptyEntries)
                .Select(segment => string.Concat(segment[..1].ToUpperInvariant(), segment[1..])));
    }
}
