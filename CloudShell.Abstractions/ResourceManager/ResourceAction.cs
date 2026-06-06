namespace CloudShell.Abstractions.ResourceManager;

public sealed record ResourceAction(
    string Id,
    string DisplayName,
    ResourceActionKind Kind = ResourceActionKind.Custom,
    string? Description = null,
    ResourceActionPresentation? Presentation = null)
{
    public ResourceActionPresentation EffectivePresentation =>
        Presentation ?? ResourceActionPresentation.ForKind(Kind);

    public bool RequiresConfirmation => EffectivePresentation.RequiresConfirmation;

    public static ResourceAction Run { get; } = new(
        "run",
        "Run",
        ResourceActionKind.Run,
        Presentation: ResourceActionPresentation.ForKind(ResourceActionKind.Run));

    public static ResourceAction Stop { get; } = new(
        "stop",
        "Stop",
        ResourceActionKind.Stop,
        "Stop the running resource.",
        ResourceActionPresentation.ForKind(ResourceActionKind.Stop));

    public static ResourceAction Pause { get; } = new(
        "pause",
        "Pause",
        ResourceActionKind.Pause,
        "Pause the running resource.",
        ResourceActionPresentation.ForKind(ResourceActionKind.Pause));

    public static ResourceAction Restart { get; } = new(
        "restart",
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
