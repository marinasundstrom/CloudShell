using CoreShell;

namespace CloudShell.Hosting.Shell;

internal static class CoreShellBlazorContent
{
    public static CoreShellContentReference For<TComponent>() =>
        CoreShellContentReference.Create(
            typeof(TComponent).AssemblyQualifiedName
            ?? typeof(TComponent).FullName
            ?? typeof(TComponent).Name);
}

internal sealed class BlazorCoreShellContentResolver : ICoreShellContentResolver
{
    public Type ResolveContentType(CoreShellContentReference content) =>
        Type.GetType(content.Value, throwOnError: false)
        ?? throw new InvalidOperationException($"CoreShell content '{content}' is not registered as a Blazor component type.");
}
