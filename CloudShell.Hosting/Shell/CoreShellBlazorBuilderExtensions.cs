using CoreShell;
using CoreShell.Blazor;
using Microsoft.AspNetCore.Components;

namespace CloudShell.Hosting.Shell;

public static class CoreShellBlazorBuilderExtensions
{
    public static CoreShellSectionOutletBuilder AddSection<TComponent>(
        this CoreShellSectionOutletBuilder builder,
        CoreShellSectionId id,
        string title,
        int order,
        IReadOnlyDictionary<string, string>? attributes = null)
        where TComponent : IComponent
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.AddSection(
            id,
            title,
            CoreShellBlazorContent.For<TComponent>(),
            order,
            attributes: attributes);
    }
}
