namespace CloudShell.ResourceModel;

public sealed record ResourceIdConventionContext(
    string Name,
    ResourceTypeId TypeId,
    string? ProviderId = null);

public interface IResourceIdConvention
{
    string CreateResourceId(ResourceIdConventionContext context);
}

public sealed class DefaultResourceIdConvention : IResourceIdConvention
{
    public static DefaultResourceIdConvention Instance { get; } = new();

    private DefaultResourceIdConvention()
    {
    }

    public string CreateResourceId(ResourceIdConventionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return $"{context.TypeId}:{context.Name}";
    }
}

internal static class ResourceIdConventionResolver
{
    public static string Resolve(
        IResourceIdConvention resourceIdConvention,
        string name,
        ResourceTypeId typeId,
        string? providerId)
    {
        var resourceId = resourceIdConvention.CreateResourceId(new(
            name,
            typeId,
            providerId));
        if (string.IsNullOrWhiteSpace(resourceId))
        {
            throw new InvalidOperationException(
                $"Resource id convention '{resourceIdConvention.GetType().FullName}' returned an empty resource id for resource '{name}'.");
        }

        return resourceId.Trim();
    }
}
