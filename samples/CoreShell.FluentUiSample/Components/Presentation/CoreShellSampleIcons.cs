using Microsoft.FluentUI.AspNetCore.Components;
using Icons = Microsoft.FluentUI.AspNetCore.Components.Icons;

namespace CoreShell.FluentUiSample.Components.Presentation;

internal static class CoreShellSampleIcons
{
    public static Icon FromName(string? name) =>
        name?.Trim().ToLowerInvariant() switch
        {
            "data-pie" => new Icons.Regular.Size20.DataPie(),
            "extension" => new Icons.Regular.Size20.PuzzlePiece(),
            "home" => new Icons.Regular.Size20.Home(),
            "pulse" => new Icons.Regular.Size20.Pulse(),
            "settings" => new Icons.Regular.Size20.Settings(),
            "task-list" => new Icons.Regular.Size20.TaskListSquareLtr(),
            "theme" => new Icons.Regular.Size20.DarkTheme(),
            "warning" => new Icons.Regular.Size20.Warning(),
            _ => new Icons.Regular.Size20.PanelLeft()
        };
}
