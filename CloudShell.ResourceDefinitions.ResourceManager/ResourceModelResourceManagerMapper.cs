using CloudShell.Abstractions.ResourceManager;
using ResourceManagerClass = CloudShell.Abstractions.ResourceManager.ResourceClass;
using ResourceModelResource = CloudShell.ResourceDefinitions.Resource;
using ResourceManagerResource = CloudShell.Abstractions.ResourceManager.Resource;

namespace CloudShell.ResourceDefinitions.ResourceManager;

public sealed record ResourceModelResourceManagerProjectionOptions(
    string DefaultProviderId = ResourceModelResourceProvider.DefaultProviderId,
    string DefaultRegion = ResourceModelResourceProvider.DefaultRegion,
    DateTimeOffset? DefaultLastUpdated = null);

public static class ResourceModelResourceManagerMapper
{
    public static ResourceManagerResource ToResourceManagerResource(
        ResourceModelResource resource,
        ResourceModelResourceManagerProjectionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(resource);

        options ??= new ResourceModelResourceManagerProjectionOptions();
        var attributes = ToResourceManagerAttributes(resource);

        return new ResourceManagerResource(
            resource.EffectiveResourceId,
            resource.Name,
            resource.Type.TypeId.ToString(),
            resource.State.ProviderId ?? resource.Type.Definition.DefaultProviderId ?? options.DefaultProviderId,
            options.DefaultRegion,
            State: null,
            Endpoints: [],
            resource.Version ?? resource.Revision.ToString(),
            resource.LastModifiedAt ?? resource.CreatedAt ?? options.DefaultLastUpdated ?? DateTimeOffset.UnixEpoch,
            resource.State.ResourceDependencies,
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

    private static IReadOnlyDictionary<string, string> ToResourceManagerAttributes(
        ResourceModelResource resource)
    {
        var attributes = resource.Attributes.ToDictionary(
            attribute => attribute.Name.ToString(),
            attribute => attribute.Value,
            StringComparer.OrdinalIgnoreCase);

        attributes[ResourceAttributeNames.ResourceGraphMembership] = ResourceGraphMembershipKinds.Declared;

        return attributes;
    }

    private static ResourceManagerClass ToResourceManagerClass(ResourceClassId classId) =>
        Enum.TryParse<ResourceManagerClass>(classId.ToString(), ignoreCase: true, out var resourceClass)
            ? resourceClass
            : ResourceManagerClass.Generic;

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
}
