using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Components;

public sealed record ResourceRelationshipNode(
    string Label,
    string? Href = null,
    string? Summary = null,
    string? Metadata = null,
    bool IsMissing = false,
    IReadOnlyList<ResourceRelationshipNodeAction>? Actions = null,
    Resource? Resource = null)
{
    public IReadOnlyList<ResourceRelationshipNodeAction> NodeActions => Actions ?? [];
}

public sealed record ResourceRelationshipNodeAction(
    string Label,
    string Href);
