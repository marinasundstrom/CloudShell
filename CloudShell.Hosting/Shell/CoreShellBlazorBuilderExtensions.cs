using CoreShell;

namespace CloudShell.Hosting.Shell;

public static class CoreShellBlazorBuilderExtensions
{
    public static CoreShellSectionOutletBuilder AddSection<TComponent>(
        this CoreShellSectionOutletBuilder builder,
        CoreShellSectionId id,
        string title,
        int order,
        IReadOnlyDictionary<string, string>? attributes = null)
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
