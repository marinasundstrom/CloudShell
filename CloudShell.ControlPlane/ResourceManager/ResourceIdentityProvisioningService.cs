using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.ControlPlane.ResourceManager;

public sealed record ResourceIdentityProvisioningPlan(
    IReadOnlyList<ResourceIdentityProvisioningRequest> Requests,
    IReadOnlyList<ResourceIdentityProvisioningDiagnostic> Diagnostics);

public sealed class ResourceIdentityProvisioningService(
    ResourceDeclarationStore declarations,
    IResourceRegistrationStore registrations,
    ResourceIdentityProviderCatalog identityProviders,
    IEnumerable<IResourceIdentityProvisioner> provisioners,
    IEnumerable<IResourceIdentityProvisioningStatusProvider>? statusProviders = null)
{
    private readonly IReadOnlyList<IResourceIdentityProvisioner> provisioners = provisioners.ToArray();
    private readonly IReadOnlyList<IResourceIdentityProvisioningStatusProvider> statusProviders =
        (statusProviders ?? Array.Empty<IResourceIdentityProvisioningStatusProvider>()).ToArray();

    public ResourceIdentityProvisioningPlan CreatePlan(string? resourceId = null)
    {
        resourceId = NormalizeOptional(resourceId);
        var identities = new List<ResolvedIdentity>();
        var diagnostics = new List<ResourceIdentityProvisioningDiagnostic>();
        var effectiveProviders = declarations.CreateIdentityProviderCatalog(identityProviders);
        var declarationsById = declarations.GetDeclarations()
            .ToDictionary(
                declaration => declaration.ResourceId,
                StringComparer.OrdinalIgnoreCase);
        var registrationsById = registrations.GetRegistrations()
            .ToDictionary(
                registration => registration.ResourceId,
                StringComparer.OrdinalIgnoreCase);

        foreach (var currentResourceId in declarationsById.Keys
                     .Concat(registrationsById.Keys)
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .Order(StringComparer.OrdinalIgnoreCase))
        {
            if (resourceId is not null &&
                !string.Equals(currentResourceId, resourceId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var binding = declarationsById.GetValueOrDefault(currentResourceId)?.IdentityBinding ??
                registrationsById.GetValueOrDefault(currentResourceId)?.IdentityBinding;
            if (binding is null)
            {
                continue;
            }

            var identity = ResourceIdentityReference.ForResource(currentResourceId, binding.Name);
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

    public async Task<ResourceIdentityProvisioningResult> ProvisionResourceAsync(
        string resourceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);

        var plan = CreatePlan(resourceId);
        var diagnostics = plan.Diagnostics.ToList();
        if (plan.Requests.Count == 0)
        {
            if (diagnostics.Count == 0)
            {
                diagnostics.Add(new ResourceIdentityProvisioningDiagnostic(
                    ResourceIdentityProvisioningDiagnosticSeverity.Warning,
                    $"Resource '{ResourceDisplayLabels.GetName(resourceId)}' does not declare a resource identity.",
                    ResourceIdentityReference.ForResource(resourceId)));
            }

            return new ResourceIdentityProvisioningResult(
                string.Empty,
                diagnostics);
        }

        var request = plan.Requests.Single();
        var provisioner = provisioners.FirstOrDefault(provisioner =>
            provisioner.CanProvision(request.Provider));
        if (provisioner is null)
        {
            diagnostics.Add(new ResourceIdentityProvisioningDiagnostic(
                ResourceIdentityProvisioningDiagnosticSeverity.Warning,
                $"No resource identity provisioner is registered for provider '{request.Provider.Id}'.",
                ProviderId: request.Provider.Id));
            return new ResourceIdentityProvisioningResult(
                request.Provider.Id,
                diagnostics);
        }

        var result = await provisioner.ProvisionAsync(request, cancellationToken);
        diagnostics.AddRange(result.ProvisioningDiagnostics);
        return new ResourceIdentityProvisioningResult(
            request.Provider.Id,
            diagnostics);
    }

    public async Task<ResourceIdentityProvisioningStatusResult> GetResourceStatusAsync(
        string resourceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);

        var plan = CreatePlan(resourceId);
        var diagnostics = plan.Diagnostics.ToList();
        if (plan.Requests.Count == 0)
        {
            if (diagnostics.Count == 0)
            {
                diagnostics.Add(new ResourceIdentityProvisioningDiagnostic(
                    ResourceIdentityProvisioningDiagnosticSeverity.Warning,
                    $"Resource '{ResourceDisplayLabels.GetName(resourceId)}' does not declare a resource identity.",
                    ResourceIdentityReference.ForResource(resourceId)));
            }

            return new ResourceIdentityProvisioningStatusResult(
                string.Empty,
                [],
                diagnostics);
        }

        var request = plan.Requests.Single();
        var statusProvider = statusProviders.FirstOrDefault(provider =>
            provider.CanGetProvisioningStatus(request.Provider));
        if (statusProvider is null)
        {
            diagnostics.Add(new ResourceIdentityProvisioningDiagnostic(
                ResourceIdentityProvisioningDiagnosticSeverity.Warning,
                $"No resource identity provisioning status provider is registered for provider '{request.Provider.Id}'.",
                ProviderId: request.Provider.Id));
            return new ResourceIdentityProvisioningStatusResult(
                request.Provider.Id,
                request.Identities
                    .Select(entry => new ResourceIdentityProvisioningStatus(
                        entry.Identity,
                        ResourceIdentityProvisioningState.Unknown,
                        "Provisioning status is not available for this identity provider."))
                    .ToArray(),
                diagnostics);
        }

        var result = await statusProvider.GetProvisioningStatusAsync(
            request,
            cancellationToken);
        diagnostics.AddRange(result.ProvisioningDiagnostics);
        return result with { Diagnostics = diagnostics };
    }

    private IReadOnlyList<ResourcePermissionGrant> GetPermissionGrants(
        IReadOnlyList<ResourceIdentityProvisioningEntry> identities)
    {
        var identitySet = identities
            .Select(entry => entry.Identity)
            .ToArray();

        return declarations.GetPermissionGrants()
            .Where(grant =>
                grant.ResourceIdentity is { } grantIdentity &&
                identitySet.Any(identity => Matches(identity, grantIdentity)))
            .ToArray();
    }

    private static bool Matches(ResourceIdentityReference left, ResourceIdentityReference right) =>
        string.Equals(left.ResourceId, right.ResourceId, StringComparison.OrdinalIgnoreCase) &&
        (string.IsNullOrWhiteSpace(right.Name) ||
         string.Equals(left.Name, right.Name, StringComparison.OrdinalIgnoreCase));

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record ResolvedIdentity(
        ResourceIdentityReference Identity,
        ResourceIdentityBinding Binding,
        ResourceIdentityProviderDefinition Provider);
}
