namespace CloudShell.Abstractions.Shell;

public static class ShellViewKeys
{
    public static string For<TComponent>() =>
        For(typeof(TComponent));

    public static string For(Type componentType)
    {
        ArgumentNullException.ThrowIfNull(componentType);

        return componentType.FullName ?? componentType.Name;
    }
}
