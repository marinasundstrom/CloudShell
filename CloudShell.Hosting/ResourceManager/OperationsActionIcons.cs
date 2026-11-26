using CloudShell.Abstractions.ResourceManager;
using Microsoft.FluentUI.AspNetCore.Components;
using Icons = Microsoft.FluentUI.AspNetCore.Components.Icons;

namespace CloudShell.Hosting.ResourceManager;

internal static class OperationsActionIcons
{
    public static Icon From(ResourceActionIcon icon) => icon switch
    {
        ResourceActionIcon.Start => new Icons.Regular.Size16.PlayCircle(),
        ResourceActionIcon.Stop => new Icons.Regular.Size16.RecordStop(),
        ResourceActionIcon.Pause => new Icons.Regular.Size16.PauseCircle(),
        ResourceActionIcon.Restart => new Icons.Regular.Size16.ArrowSync(),
        _ => new Icons.Regular.Size16.Settings()
    };
}
