using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace CloudShell.ResourceHost.Pages;

public sealed class RegisterSampleResource : ComponentBase
{
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", "empty-state compact");
        builder.OpenElement(2, "strong");
        builder.AddContent(3, "Static sample provider");
        builder.CloseElement();
        builder.OpenElement(4, "span");
        builder.AddContent(
            5,
            "The sample resources are declared in Program.cs and surfaced by SampleResourceProvider.");
        builder.CloseElement();
        builder.CloseElement();
    }
}
