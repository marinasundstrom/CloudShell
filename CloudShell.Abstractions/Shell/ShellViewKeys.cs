namespace CloudShell.Abstractions.Shell;

public static class ShellViewKeys
{
    public static string For<TComponent>() =>
        For(typeof(TComponent));

    public static string For<TComponent>(string extensionId) =>
        For(extensionId, typeof(TComponent));

    public static string For(Type componentType)
    {
        ArgumentNullException.ThrowIfNull(componentType);

        return componentType.FullName ?? componentType.Name;
    }

    public static string For(string extensionId, Type componentType) =>
        For(extensionId, For(componentType));

    public static string For(string extensionId, string viewId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extensionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(viewId);

        return $"{extensionId.Trim()}.{viewId.Trim().TrimStart('.')}";
    }
}
