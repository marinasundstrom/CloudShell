namespace CloudShell.Hosting.ResourceManager;

internal static class OperationsContributions
{
    public static bool Applies(IReadOnlyDictionary<string, string> attributes, OperationsWorkspaceContext context)
    {
        var scope = attributes.GetValueOrDefault(OperationsWorkspaceIds.Scope, "both");
        if (scope != "both" && scope != (context.IsSystem ? "system" : "resource")) return false;
        var type = attributes.GetValueOrDefault(OperationsWorkspaceIds.ResourceType);
        return type is null || string.Equals(type, context.Resource?.EffectiveTypeId, StringComparison.OrdinalIgnoreCase);
    }
}
