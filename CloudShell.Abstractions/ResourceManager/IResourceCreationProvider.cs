using System.Text.Json;

namespace CloudShell.Abstractions.ResourceManager;

public interface IResourceCreationProvider : IResourceProvider
{
    bool CanCreate(ResourceCreationRequest request);

    Task CreateAsync(
        ResourceCreationRequest request,
        ResourceCreationContext context,
        CancellationToken cancellationToken = default);
}

public sealed record ResourceCreationRequest(
    string ResourceType,
    string ResourceId,
    string Name,
    JsonElement Configuration,
    string? ResourceGroupId,
    ResourceClass? ResourceClass = null,
    IReadOnlyDictionary<string, string>? Attributes = null)
{
    private static readonly IReadOnlyDictionary<string, string> EmptyAttributes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, string> ResourceAttributes => Attributes ?? EmptyAttributes;
}

public sealed record ResourceCreationContext(
    IResourceRegistrationStore Registrations);
