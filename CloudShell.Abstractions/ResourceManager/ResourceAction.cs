using CloudShell.Abstractions.Authorization;

namespace CloudShell.Abstractions.ResourceManager;

public static class ResourceActionIds
{
    public const string Run = "run";
    public const string Stop = "stop";
    public const string Pause = "pause";
    public const string Restart = "restart";
}

public sealed record ResourceAction(
    string Id,
    string DisplayName,
    ResourceActionKind Kind = ResourceActionKind.Custom,
    string? Description = null,
    ResourceActionPresentation? Presentation = null,
    string? RequiredPermission = null)
{
    public ResourceActionPresentation EffectivePresentation =>
        Presentation ?? ResourceActionPresentation.ForKind(Kind);

    public bool RequiresConfirmation => EffectivePresentation.RequiresConfirmation;

    public static ResourceAction Run { get; } = new(
        ResourceActionIds.Run,
        "Run",
        ResourceActionKind.Run,
        Presentation: ResourceActionPresentation.ForKind(ResourceActionKind.Run));

    public static ResourceAction Stop { get; } = new(
        ResourceActionIds.Stop,
        "Stop",
        ResourceActionKind.Stop,
        "Stop the running resource.",
        ResourceActionPresentation.ForKind(ResourceActionKind.Stop));

    public static ResourceAction Pause { get; } = new(
        ResourceActionIds.Pause,
        "Pause",
        ResourceActionKind.Pause,
        "Pause the running resource.",
        ResourceActionPresentation.ForKind(ResourceActionKind.Pause));

    public static ResourceAction Restart { get; } = new(
        ResourceActionIds.Restart,
        "Restart",
        ResourceActionKind.Restart,
        "Restart the resource.",
        ResourceActionPresentation.ForKind(ResourceActionKind.Restart));
}

public enum ResourceActionKind
{
    Custom,
    Run,
    Stop,
    Pause,
    Restart
}

public static class ResourceActionPermissions
{
    public static string GetRequiredPermission(ResourceAction action) =>
        !string.IsNullOrWhiteSpace(action.RequiredPermission)
            ? action.RequiredPermission
            : GetDefaultRequiredPermission(action);

    private static string GetDefaultRequiredPermission(ResourceAction action) =>
        action.Kind switch
        {
            ResourceActionKind.Run or
            ResourceActionKind.Stop or
            ResourceActionKind.Pause or
            ResourceActionKind.Restart => CloudShellPermissions.Resources.Actions.Lifecycle,
            _ => CloudShellPermissions.Resources.Actions.Execute
        };
}

public sealed record ResourceActionPresentation(
    ResourceActionDisplayStyle DisplayStyle,
    ResourceActionIcon Icon,
    bool RequiresConfirmation = false)
{
    public static ResourceActionPresentation ForKind(ResourceActionKind kind) => kind switch
    {
        ResourceActionKind.Run => new(
            ResourceActionDisplayStyle.Inline,
            ResourceActionIcon.Run),
        ResourceActionKind.Stop => new(
            ResourceActionDisplayStyle.Inline,
            ResourceActionIcon.Stop,
            RequiresConfirmation: true),
        ResourceActionKind.Pause => new(
            ResourceActionDisplayStyle.Inline,
            ResourceActionIcon.Pause,
            RequiresConfirmation: true),
        ResourceActionKind.Restart => new(
            ResourceActionDisplayStyle.Overflow,
            ResourceActionIcon.Restart,
            RequiresConfirmation: true),
        _ => new(
            ResourceActionDisplayStyle.Overflow,
            ResourceActionIcon.Custom)
    };
}

public enum ResourceActionDisplayStyle
{
    Inline,
    Overflow
}

public enum ResourceActionIcon
{
    Custom,
    Run,
    Stop,
    Pause,
    Restart
}
