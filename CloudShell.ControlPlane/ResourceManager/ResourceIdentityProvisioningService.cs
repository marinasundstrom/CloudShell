using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.ControlPlane.ResourceManager;

public sealed record ResourceIdentityProvisioningPlan(
    IReadOnlyList<ResourceIdentityProvisioningRequest> Requests,
    IReadOnlyList<ResourceIdentityProvisioningDiagnostic> Diagnostics);

public sealed class ResourceIdentityProvisioningService(
    ResourceDeclarationStore declarations,
    ResourceIdentityProviderCatalog identityProviders,
    IEnumerable<IResourceIdentityProvisioner> provisioners)
{
    private readonly IReadOnlyList<IResourceIdentityProvisioner> provisioners = provisioners.ToArray();

    public ResourceIdentityProvisioningPlan CreatePlan()
    {
        var identities = new List<ResolvedIdentity>();
        var diagnostics = new List<ResourceIdentityProvisioningDiagnostic>();
        var effectiveProviders = declarations.CreateIdentityProviderCatalog(identityProviders);

        foreach (var declaration in declarations.GetDeclarations())
        {
            if (declaration.IdentityBinding is not { } binding)
            {
                continue;
            }

            var identity = ResourceIdentityReference.ForResource(declaration.ResourceId, binding.Name);
            var resolution = effectiveProviders.Resolve(binding);
            if (resolution.Provider is null)
            {
                diagnostics.Add(new ResourceIdentityProvisioningDiagnostic(
                    ResourceIdentityProvisioningDiagnosticSeverity.Error,
                    resolution.Reason ?? "Resource identity provider could not be resolved.",
                    identity,
                    binding.ProviderId));
                continue;
            }

            identities.Add(new ResolvedIdentity(identity, binding, resolution.Provider));
        }

        var requests = identities
            .GroupBy(identity => identity.Provider.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var provider = group.First().Provider;
                var entries = group
                    .OrderBy(identity => identity.Identity.ResourceId, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(identity => identity.Identity.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(identity => new ResourceIdentityProvisioningEntry(identity.Identity, identity.Binding))
                    .ToArray();
                return new ResourceIdentityProvisioningRequest(
                    provider,
                    entries,
                    GetPermissionGrants(entries));
            })
            .OrderBy(request => request.Provider.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new ResourceIdentityProvisioningPlan(requests, diagnostics);
    }

    public async Task<ResourceIdentityProvisioningPlan> ProvisionAsync(
        CancellationToken cancellationToken = default)
    {
        var plan = CreatePlan();
        var diagnostics = plan.Diagnostics.ToList();

        foreach (var request in plan.Requests)
        {
            var provisioner = provisioners.FirstOrDefault(provisioner =>
                provisioner.CanProvision(request.Provider));
            if (provisioner is null)
            {
                diagnostics.Add(new ResourceIdentityProvisioningDiagnostic(
                    ResourceIdentityProvisioningDiagnosticSeverity.Warning,
                    $"No resource identity provisioner is registered for provider '{request.Provider.Id}'.",
                    ProviderId: request.Provider.Id));
                continue;
            }

            var result = await provisioner.ProvisionAsync(request, cancellationToken);
            diagnostics.AddRange(result.ProvisioningDiagnostics);
        }

        return plan with { Diagnostics = diagnostics };
    }

    private IReadOnlyList<ResourcePermissionGrant> GetPermissionGrants(
        IReadOnlyList<ResourceIdentityProvisioningEntry> identities)
    {
        var identitySet = identities
            .Select(entry => entry.Identity)
            .ToArray();

        return declarations.GetPermissionGrants()
            .Where(grant => identitySet.Any(identity => Matches(identity, grant.Identity)))
            .ToArray();
    }

    private static bool Matches(ResourceIdentityReference left, ResourceIdentityReference right) =>
        string.Equals(left.ResourceId, right.ResourceId, StringComparison.OrdinalIgnoreCase) &&
        (string.IsNullOrWhiteSpace(right.Name) ||
         string.Equals(left.Name, right.Name, StringComparison.OrdinalIgnoreCase));

    private sealed record ResolvedIdentity(
        ResourceIdentityReference Identity,
        ResourceIdentityBinding Binding,
        ResourceIdentityProviderDefinition Provider);
}
