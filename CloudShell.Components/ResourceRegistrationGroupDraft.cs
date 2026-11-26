using CloudShell.Abstractions.ControlPlane;
using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Components;

/// <summary>Optional, non-secret group draft. Resolve only when the provider submits creation.</summary>
public sealed class ResourceRegistrationGroupDraft
{
    public bool CreateNew { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string? CreatedGroupId { get; set; }
    public string? CreatedGroupName { get; set; }

    public string? Validate() => CreateNew && string.IsNullOrWhiteSpace(Name) ? "New resource group name is required." : null;

    public async Task<string?> ResolveAsync(string? selectedGroupId,
        Func<CreateResourceGroupCommand, Task<ResourceGroup>> createGroup)
    {
        if (!CreateNew) return string.IsNullOrWhiteSpace(selectedGroupId) ? null : selectedGroupId;
        if (Validate() is { } error) throw new InvalidOperationException(error);
        var group = await createGroup(new CreateResourceGroupCommand(Name.Trim(), Description.Trim()));
        CreatedGroupId = group.Id;
        CreatedGroupName = group.Name;
        CreateNew = false;
        return group.Id;
    }
}
