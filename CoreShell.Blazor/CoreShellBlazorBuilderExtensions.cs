using Microsoft.AspNetCore.Components;

namespace CoreShell.Blazor;

public static class CoreShellBlazorBuilderExtensions
{
    public static CoreShellSectionOutletBuilder AddSection<TComponent>(
        this CoreShellSectionOutletBuilder builder,
        CoreShellSectionId id,
        string title,
        int order,
        IReadOnlyDictionary<string, string>? attributes = null,
        CoreShellAuthorizationRequirements? authorization = null,
        CoreShellLayoutReference? layout = null)
        where TComponent : IComponent
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.AddSection(
            id,
            title,
            CoreShellBlazorContent.For<TComponent>(),
            order,
            authorization,
            attributes,
            layout);
    }
}
