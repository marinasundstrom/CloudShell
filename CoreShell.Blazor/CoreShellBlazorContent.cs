using Microsoft.AspNetCore.Components;

namespace CoreShell.Blazor;

public static class CoreShellBlazorContent
{
    public static CoreShellContentReference For<TComponent>()
        where TComponent : IComponent =>
        For(typeof(TComponent));

    public static CoreShellContentReference For(Type componentType)
    {
        ArgumentNullException.ThrowIfNull(componentType);
        EnsureComponentType(componentType, nameof(componentType));

        return CoreShellContentReference.Create(GetTypeReference(componentType));
    }

    internal static Type ResolveComponentType(string typeName, string referenceKind, string referenceValue)
    {
        var componentType = Type.GetType(typeName, throwOnError: false)
            ?? throw new InvalidOperationException(
                $"CoreShell {referenceKind} '{referenceValue}' is not registered as a Blazor component type.");

        EnsureComponentType(componentType, referenceKind);
        return componentType;
    }

    internal static void EnsureComponentType(Type componentType, string parameterName)
    {
        if (!typeof(IComponent).IsAssignableFrom(componentType))
        {
            throw new ArgumentException(
                $"CoreShell Blazor content types must implement {typeof(IComponent).FullName}.",
                parameterName);
        }
    }

    private static string GetTypeReference(Type componentType) =>
        componentType.AssemblyQualifiedName
        ?? componentType.FullName
        ?? componentType.Name;
}

public static class CoreShellBlazorLayout
{
    public static CoreShellLayoutReference For<TComponent>()
        where TComponent : IComponent =>
        For(typeof(TComponent));

    public static CoreShellLayoutReference For(Type componentType)
    {
        ArgumentNullException.ThrowIfNull(componentType);
        CoreShellBlazorContent.EnsureComponentType(componentType, nameof(componentType));

        return CoreShellLayoutReference.Create(
            componentType.AssemblyQualifiedName
            ?? componentType.FullName
            ?? componentType.Name);
    }
}

public sealed class BlazorCoreShellContentResolver : ICoreShellContentResolver
{
    public Type ResolveContentType(CoreShellContentReference content) =>
        CoreShellBlazorContent.ResolveComponentType(content.Value, "content", content.ToString());
}

public sealed class BlazorCoreShellLayoutResolver : ICoreShellLayoutResolver
{
    public Type ResolveLayoutType(CoreShellLayoutReference layout) =>
        CoreShellBlazorContent.ResolveComponentType(layout.Value, "layout", layout.ToString());
}
