using Microsoft.AspNetCore.Components;

namespace CloudShell.Hosting.Components.Pages.Resources;

public sealed record GeneratedResourceViewSection(
    string Id,
    string Title,
    int Order,
    RenderFragment Content);
