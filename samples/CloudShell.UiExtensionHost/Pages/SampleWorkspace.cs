using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace CloudShell.UiExtensionHost.Pages;

public sealed class SampleWorkspace : ComponentBase
{
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "section");
        builder.AddAttribute(1, "class", "page-heading");
        builder.OpenElement(2, "div");
        builder.OpenElement(3, "span");
        builder.AddAttribute(4, "class", "eyebrow");
        builder.AddContent(5, "UI extension");
        builder.CloseElement();
        builder.OpenElement(6, "h1");
        builder.AddContent(7, "Sample workspace");
        builder.CloseElement();
        builder.OpenElement(8, "p");
        builder.AddContent(
            9,
            "This page is contributed by the sample host application and runs without mapping the CloudShell control-plane API.");
        builder.CloseElement();
        builder.CloseElement();
        builder.CloseElement();

        builder.OpenElement(10, "section");
        builder.AddAttribute(11, "class", "dashboard-grid");
        builder.OpenElement(12, "article");
        builder.AddAttribute(13, "class", "panel panel-wide");
        builder.OpenElement(14, "div");
        builder.AddAttribute(15, "class", "panel-header");
        builder.OpenElement(16, "div");
        builder.OpenElement(17, "h2");
        builder.AddContent(18, "Embedded shell surface");
        builder.CloseElement();
        builder.OpenElement(19, "p");
        builder.AddContent(
            20,
            "The extension contributes navigation, routing, and a component from the sample assembly.");
        builder.CloseElement();
        builder.CloseElement();
        builder.CloseElement();

        builder.OpenElement(21, "div");
        builder.AddAttribute(22, "class", "activity-list");
        builder.OpenElement(23, "div");
        builder.AddAttribute(24, "class", "activity-row");
        builder.OpenElement(25, "span");
        builder.AddAttribute(26, "class", "resource-kind-icon");
        builder.AddContent(27, "U");
        builder.CloseElement();
        builder.OpenElement(28, "div");
        builder.OpenElement(29, "strong");
        builder.AddContent(30, "CloudShell UI");
        builder.CloseElement();
        builder.OpenElement(31, "span");
        builder.AddContent(32, "Hosted without persistence, resource stores, or control-plane endpoints.");
        builder.CloseElement();
        builder.CloseElement();
        builder.CloseElement();

        builder.OpenElement(33, "div");
        builder.AddAttribute(34, "class", "activity-row");
        builder.OpenElement(35, "span");
        builder.AddAttribute(36, "class", "resource-kind-icon");
        builder.AddContent(37, "E");
        builder.CloseElement();
        builder.OpenElement(38, "div");
        builder.OpenElement(39, "strong");
        builder.AddContent(40, "Sample extension");
        builder.CloseElement();
        builder.OpenElement(41, "span");
        builder.AddContent(42, "Registered through the same extension model used by full CloudShell hosts.");
        builder.CloseElement();
        builder.CloseElement();
        builder.CloseElement();

        builder.CloseElement();
        builder.CloseElement();
        builder.CloseElement();
    }
}
