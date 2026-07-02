using System.Globalization;
using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.ControlPlane.Providers;

public sealed class LoadBalancerGraphValidator : IResourceDefinitionGraphValidator
{
    public ValueTask<ResourceDefinitionValidationResult> ValidateAsync(
        ResourceDefinitionGraphValidationContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var diagnostics = new List<ResourceDefinitionDiagnostic>();

        foreach (var resource in context.Resources.Where(resource =>
            resource.Type.TypeId == LoadBalancerResourceTypeProvider.ResourceTypeId))
        {
            ValidateReferences(resource, context, diagnostics);
            ValidateRoutes(resource, context, diagnostics);
        }

        return ValueTask.FromResult(
            ResourceDefinitionValidationResult.FromDiagnostics(diagnostics));
    }

    private static void ValidateReferences(
        Resource resource,
        ResourceDefinitionGraphValidationContext context,
        List<ResourceDefinitionDiagnostic> diagnostics)
    {
        foreach (var reference in resource.State.StartupDependencies)
        {
            if (!reference.TryGetDependsOnResourceId(out var resourceId))
            {
                continue;
            }

            var target = context.FindResource(resourceId);
            if (target is null)
            {
                continue;
            }

            if (IsHostReference(reference))
            {
                if (!IsHostType(target.Type.TypeId))
                {
                    diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                        ResourceDefinitionDiagnosticCodes.ResourceCapabilityReferenceInvalid,
                        $"Load balancer '{resource.EffectiveResourceId}' references resource type '{target.Type.TypeId}', expected a host resource.",
                        resource.EffectiveResourceId));
                }

                continue;
            }

            if (IsInvalidBackendTarget(target))
            {
                diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                    ResourceDefinitionDiagnosticCodes.ResourceCapabilityReferenceInvalid,
                    $"Load balancer '{resource.EffectiveResourceId}' cannot use resource type '{target.Type.TypeId}' as a backend target.",
                    resource.EffectiveResourceId));
            }
        }
    }

    private static void ValidateRoutes(
        Resource resource,
        ResourceDefinitionGraphValidationContext context,
        List<ResourceDefinitionDiagnostic> diagnostics)
    {
        var entrypoints = resource.Attributes
            .GetObject<LoadBalancerEntrypointValue[]>(
                LoadBalancerResourceTypeProvider.Attributes.Entrypoints) ?? [];
        var routes = resource.Attributes
            .GetObject<LoadBalancerRouteValue[]>(
                LoadBalancerResourceTypeProvider.Attributes.Routes) ?? [];
        ValidateDuplicateEntrypointNames(resource, entrypoints, diagnostics);
        ValidateDuplicateRouteIds(resource, routes, diagnostics);

        var entrypointNames = entrypoints
            .Select(entrypoint => entrypoint.Name.Trim())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var entrypointsByName = entrypoints
            .Where(entrypoint => !string.IsNullOrWhiteSpace(entrypoint.Name))
            .GroupBy(entrypoint => entrypoint.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.OrdinalIgnoreCase);

        foreach (var route in routes)
        {
            ValidateRouteTarget(resource, context, route, diagnostics);

            var entrypointName = route.EntrypointName.Trim();
            if (string.IsNullOrWhiteSpace(entrypointName))
            {
                continue;
            }

            if (!entrypointNames.Contains(entrypointName))
            {
                diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                    ResourceDefinitionDiagnosticCodes.ResourceCapabilityReferenceInvalid,
                    $"Load balancer '{resource.EffectiveResourceId}' route '{route.Id}' references missing entrypoint '{entrypointName}'.",
                    resource.EffectiveResourceId));
                continue;
            }

            if (entrypointsByName.TryGetValue(entrypointName, out var entrypoint) &&
                !IsRouteCompatibleWithEntrypoint(route, entrypoint))
            {
                diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                    ResourceDefinitionDiagnosticCodes.ResourceCapabilityReferenceInvalid,
                    $"Load balancer '{resource.EffectiveResourceId}' route '{route.Id}' is a {route.Kind.Trim().ToLowerInvariant()} route but entrypoint '{entrypoint.Name.Trim()}' uses protocol '{entrypoint.Protocol}'.",
                    resource.EffectiveResourceId));
            }
        }

        ValidateRouteConflicts(resource, routes, diagnostics);
    }

    private static void ValidateRouteTarget(
        Resource resource,
        ResourceDefinitionGraphValidationContext context,
        LoadBalancerRouteValue route,
        List<ResourceDefinitionDiagnostic> diagnostics)
    {
        var reference = route.Target.Resource;

        if (reference.Relationship != ResourceReferenceRelationships.Reference)
        {
            diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                ResourceDefinitionDiagnosticCodes.ResourceCapabilityReferenceInvalid,
                $"Load balancer '{resource.EffectiveResourceId}' route '{route.Id}' target '{reference.Value}' uses relationship '{reference.Relationship}', expected '{ResourceReferenceRelationships.Reference}'.",
                resource.EffectiveResourceId));
            return;
        }

        if (!reference.TryGetResourceId(out var resourceId))
        {
            diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                ResourceDefinitionDiagnosticCodes.ResourceCapabilityReferenceInvalid,
                $"Load balancer '{resource.EffectiveResourceId}' route '{route.Id}' target '{reference.Value}' uses addressing mode '{reference.AddressingMode}', expected '{ResourceReferenceAddressingModes.ResourceId}'.",
                resource.EffectiveResourceId));
            return;
        }

        var target = context.FindResource(resourceId);
        if (target is null)
        {
            diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                ResourceDefinitionDiagnosticCodes.ResourceReferenceMissing,
                $"Load balancer '{resource.EffectiveResourceId}' route '{route.Id}' references missing target resource '{resourceId}'.",
                resource.EffectiveResourceId));
            return;
        }

        if (reference.TypeId.HasValue &&
            target.Type.TypeId != reference.TypeId.Value)
        {
            diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                ResourceDefinitionDiagnosticCodes.ResourceReferenceTypeMismatch,
                $"Load balancer '{resource.EffectiveResourceId}' route '{route.Id}' references resource '{target.EffectiveResourceId}' with type '{target.Type.TypeId}', expected '{reference.TypeId.Value}'.",
                resource.EffectiveResourceId));
        }

        if (IsInvalidBackendTarget(target))
        {
            diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                ResourceDefinitionDiagnosticCodes.ResourceCapabilityReferenceInvalid,
                $"Load balancer '{resource.EffectiveResourceId}' cannot use resource type '{target.Type.TypeId}' as a backend target.",
                resource.EffectiveResourceId));
        }

        ValidateRouteTargetEndpoint(resource, target, route, diagnostics);
    }

    private static void ValidateRouteTargetEndpoint(
        Resource resource,
        Resource target,
        LoadBalancerRouteValue route,
        List<ResourceDefinitionDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(route.Target.EndpointName) ||
            GetDeclaredEndpointRequests(target) is not { } endpointRequests)
        {
            return;
        }

        var endpointName = route.Target.EndpointName.Trim();
        if (endpointRequests.Any(endpoint =>
                !string.IsNullOrWhiteSpace(endpoint.Name) &&
                string.Equals(endpoint.Name.Trim(), endpointName, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        diagnostics.Add(ResourceDefinitionDiagnostic.Error(
            ResourceDefinitionDiagnosticCodes.ResourceCapabilityReferenceInvalid,
            $"Load balancer '{resource.EffectiveResourceId}' route '{route.Id}' target endpoint '{endpointName}' could not be found on resource '{target.EffectiveResourceId}'.",
            resource.EffectiveResourceId));
    }

    private static IReadOnlyList<NetworkingEndpointRequestValue>? GetDeclaredEndpointRequests(
        Resource target)
    {
        if (target.Type.TypeId == AspNetCoreProjectResourceTypeProvider.ResourceTypeId)
        {
            return target.Attributes.GetObject<NetworkingEndpointRequestValue[]>(
                AspNetCoreProjectResourceTypeProvider.Attributes.EndpointRequests) ?? [];
        }

        if (target.Type.TypeId == JavaScriptAppResourceTypeProvider.ResourceTypeId)
        {
            return target.Attributes.GetObject<NetworkingEndpointRequestValue[]>(
                JavaScriptAppResourceTypeProvider.Attributes.EndpointRequests) ?? [];
        }

        if (target.Type.TypeId == JavaAppResourceTypeProvider.ResourceTypeId)
        {
            return target.Attributes.GetObject<NetworkingEndpointRequestValue[]>(
                JavaAppResourceTypeProvider.Attributes.EndpointRequests) ?? [];
        }

        if (target.Type.TypeId == ContainerApplicationResourceTypeProvider.ResourceTypeId)
        {
            return target.Attributes.GetObject<NetworkingEndpointRequestValue[]>(
                ContainerApplicationResourceTypeProvider.Attributes.EndpointRequests) ?? [];
        }

        if (target.Type.TypeId == SqlServerResourceTypeProvider.ResourceTypeId)
        {
            return target.Attributes.GetObject<NetworkingEndpointRequestValue[]>(
                SqlServerResourceTypeProvider.Attributes.EndpointRequests) ?? [];
        }

        return null;
    }

    private static void ValidateDuplicateEntrypointNames(
        Resource resource,
        IReadOnlyList<LoadBalancerEntrypointValue> entrypoints,
        List<ResourceDefinitionDiagnostic> diagnostics)
    {
        foreach (var duplicate in entrypoints
            .Select(entrypoint => entrypoint.Name.Trim())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .GroupBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1))
        {
            diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                ResourceDefinitionDiagnosticCodes.AttributeValueInvalid,
                $"Load balancer '{resource.EffectiveResourceId}' has multiple entrypoints named '{duplicate.Key}'.",
                resource.EffectiveResourceId));
        }
    }

    private static void ValidateDuplicateRouteIds(
        Resource resource,
        IReadOnlyList<LoadBalancerRouteValue> routes,
        List<ResourceDefinitionDiagnostic> diagnostics)
    {
        foreach (var duplicate in routes
            .Select(route => route.Id.Trim())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .GroupBy(id => id, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1))
        {
            diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                ResourceDefinitionDiagnosticCodes.AttributeValueInvalid,
                $"Load balancer '{resource.EffectiveResourceId}' has multiple routes with id '{duplicate.Key}'.",
                resource.EffectiveResourceId));
        }
    }

    private static void ValidateRouteConflicts(
        Resource resource,
        IReadOnlyList<LoadBalancerRouteValue> routes,
        List<ResourceDefinitionDiagnostic> diagnostics)
    {
        foreach (var duplicate in routes
            .Where(route => !string.IsNullOrWhiteSpace(route.Id))
            .GroupBy(CreateRouteConflictKey, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1))
        {
            var routeIds = string.Join(", ", duplicate.Select(route => route.Id.Trim()));
            diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                ResourceDefinitionDiagnosticCodes.AttributeValueInvalid,
                $"Load balancer '{resource.EffectiveResourceId}' has conflicting route match '{duplicate.Key}' on routes: {routeIds}.",
                resource.EffectiveResourceId));
        }
    }

    private static bool IsRouteCompatibleWithEntrypoint(
        LoadBalancerRouteValue route,
        LoadBalancerEntrypointValue entrypoint)
    {
        if (!Enum.TryParse<LoadBalancerRouteKind>(route.Kind.Trim(), ignoreCase: true, out var kind) ||
            !Enum.TryParse<ResourceEndpointProtocol>(entrypoint.Protocol.Trim(), ignoreCase: true, out var protocol))
        {
            return true;
        }

        return kind switch
        {
            LoadBalancerRouteKind.Http => protocol is ResourceEndpointProtocol.Http or ResourceEndpointProtocol.Https,
            LoadBalancerRouteKind.Tcp => protocol == ResourceEndpointProtocol.Tcp,
            _ => true
        };
    }

    private static string CreateRouteConflictKey(LoadBalancerRouteValue route)
    {
        var kind = route.Kind.Trim();
        var entrypointName = route.EntrypointName.Trim();
        var host = NormalizeNullable(route.Match.Host)?.ToLowerInvariant() ?? "*";
        var pathPrefix = NormalizeNullable(route.Match.PathPrefix) ?? "/";
        var port = route.Match.Port?.ToString(CultureInfo.InvariantCulture) ?? "*";
        return string.Equals(kind, "Tcp", StringComparison.OrdinalIgnoreCase)
            ? $"{kind}:{entrypointName}:{port}"
            : $"{kind}:{entrypointName}:{host}:{pathPrefix}";
    }

    private static string? NormalizeNullable(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool IsHostReference(ResourceReference reference) =>
        IsHostType(reference.TypeId);

    private static bool IsHostType(ResourceTypeId? typeId) =>
        typeId == ContainerHostResourceTypeProvider.ResourceTypeId ||
        typeId == DockerHostResourceTypeProvider.ResourceTypeId;

    private static bool IsInvalidBackendTarget(Resource target) =>
        IsNetworkProviderType(target.Type.TypeId) ||
        target.Class.ClassId == ContainerHostResourceTypeProvider.ClassId;

    private static bool IsNetworkProviderType(ResourceTypeId typeId) =>
        typeId == LoadBalancerResourceTypeProvider.ResourceTypeId ||
        typeId == NetworkResourceTypeProvider.ResourceTypeId ||
        typeId == VirtualNetworkResourceTypeProvider.ResourceTypeId ||
        typeId == LocalHostNetworkResourceTypeProvider.ResourceTypeId ||
        typeId == MacOSHostNetworkResourceTypeProvider.ResourceTypeId ||
        typeId == DnsZoneResourceTypeProvider.ResourceTypeId ||
        typeId == NameMappingResourceTypeProvider.ResourceTypeId;
}
