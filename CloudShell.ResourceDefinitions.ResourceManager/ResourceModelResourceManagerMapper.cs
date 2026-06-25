using CloudShell.Abstractions.ResourceManager;
using ResourceManagerClass = CloudShell.Abstractions.ResourceManager.ResourceClass;
using ResourceManagerState = CloudShell.Abstractions.ResourceManager.ResourceState;
using ResourceModelResource = CloudShell.ResourceDefinitions.Resource;
using ResourceManagerResource = CloudShell.Abstractions.ResourceManager.Resource;

namespace CloudShell.ResourceDefinitions.ResourceManager;

public sealed record ResourceModelResourceManagerProjectionOptions(
    string DefaultProviderId = ResourceModelResourceProvider.DefaultProviderId,
    string DefaultRegion = ResourceModelResourceProvider.DefaultRegion,
    DateTimeOffset? DefaultLastUpdated = null,
    string BridgeProviderId = ResourceModelResourceProvider.DefaultProviderId,
    ResourceModelResourceManagerStateResolver? StateResolver = null);

public delegate ResourceManagerState? ResourceModelResourceManagerStateResolver(
    ResourceModelResource resource);

public static class ResourceModelResourceManagerAttributeNames
{
    public const string BridgeProviderId = "resourceModel.bridgeProviderId";
}

public static class ResourceModelResourceManagerMapper
{
    public static ResourceManagerResource ToResourceManagerResource(
        ResourceModelResource resource,
        ResourceModelResourceManagerProjectionOptions? options = null,
        IReadOnlyList<string>? dependencyIds = null)
    {
        ArgumentNullException.ThrowIfNull(resource);

        options ??= new ResourceModelResourceManagerProjectionOptions();
        var attributes = ToResourceManagerAttributes(resource);
        attributes[ResourceModelResourceManagerAttributeNames.BridgeProviderId] =
            options.BridgeProviderId;

        return new ResourceManagerResource(
            resource.EffectiveResourceId,
            resource.Name,
            resource.Type.TypeId.ToString(),
            resource.State.ProviderId ?? resource.Type.Definition.DefaultProviderId ?? options.DefaultProviderId,
            options.DefaultRegion,
            State: ToResourceManagerState(resource, options),
            Endpoints: [],
            resource.Version ?? resource.Revision.ToString(),
            resource.LastModifiedAt ?? resource.CreatedAt ?? options.DefaultLastUpdated ?? DateTimeOffset.UnixEpoch,
            dependencyIds ?? resource.State.StartupDependencyIds,
            TypeId: resource.Type.TypeId.ToString(),
            Actions: resource.Operations
                .Where(operation => operation.IsAvailable)
                .Select(ToResourceManagerAction)
                .ToArray(),
            ResourceClass: ToResourceManagerClass(resource.Class.ClassId),
            Attributes: attributes,
            Capabilities: resource.Capabilities
                .Select(capability => new ResourceCapability(capability.Id.ToString()))
                .ToArray(),
            Source: ResourceSource.User,
            ManagementMode: ResourceManagementMode.UserManaged,
            DisplayName: resource.State.DisplayName);
    }

    public static IReadOnlyList<ResourceModelDiagnostic> ToResourceModelDiagnostics(
        ResourceModelResource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);

        return resource.Diagnostics
            .Select(diagnostic => new ResourceModelDiagnostic(
                diagnostic.Code,
                ToResourceModelDiagnosticMessage(diagnostic),
                resource.EffectiveResourceId,
                resource.Type.TypeId.ToString(),
                ToResourceManagerClass(resource.Class.ClassId),
                ToResourceManagerClass(resource.Class.ClassId),
                "resource model"))
            .ToArray();
    }

    private static Dictionary<string, string> ToResourceManagerAttributes(
        ResourceModelResource resource)
    {
        var attributes = resource.Attributes
            .Where(attribute => attribute.Value is not null)
            .ToDictionary(
                attribute => attribute.Name.ToString(),
                attribute => attribute.Value!,
                StringComparer.OrdinalIgnoreCase);

        attributes[ResourceAttributeNames.ResourceGraphMembership] = ResourceGraphMembershipKinds.Declared;

        return attributes;
    }

    private static ResourceManagerClass ToResourceManagerClass(ResourceClassId classId) =>
        Enum.TryParse<ResourceManagerClass>(classId.ToString(), ignoreCase: true, out var resourceClass)
            ? resourceClass
            : ResourceManagerClass.Generic;

    private static ResourceManagerState? ToResourceManagerState(
        ResourceModelResource resource,
        ResourceModelResourceManagerProjectionOptions options) =>
        options.StateResolver?.Invoke(resource) ??
        (resource.Operations.Any(operation => IsLifecycleOperation(operation.Id))
            ? ResourceManagerState.Unknown
            : null);

    private static bool IsLifecycleOperation(ResourceOperationId operationId) =>
        operationId.ToString() is
            ResourceActionIds.Start or
            ResourceActionIds.Stop or
            ResourceActionIds.Pause or
            ResourceActionIds.Restart;

    private static ResourceAction ToResourceManagerAction(ResourceOperationResolution operation) =>
        operation.Id.ToString() switch
        {
            ResourceActionIds.Start => ResourceAction.Start,
            ResourceActionIds.Stop => ResourceAction.Stop,
            ResourceActionIds.Pause => ResourceAction.Pause,
            ResourceActionIds.Restart => ResourceAction.Restart,
            var id => new ResourceAction(
                id,
                ToDisplayName(id),
                Description: "Resolved Resource model operation.")
        };

    private static string ToDisplayName(string operationId) =>
        string.Join(
            " ",
            operationId
                .Replace('.', ' ')
                .Replace('-', ' ')
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(part => char.ToUpperInvariant(part[0]) + part[1..]));

    private static string ToResourceModelDiagnosticMessage(
        ResourceDefinitionDiagnostic diagnostic) =>
        string.IsNullOrWhiteSpace(diagnostic.Target)
            ? diagnostic.Message
            : $"{diagnostic.Message} Target: {diagnostic.Target}.";
}
