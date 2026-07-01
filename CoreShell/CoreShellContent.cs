namespace CoreShell;

public readonly record struct CoreShellContentReference(string Value)
{
    public static CoreShellContentReference Create(string value) =>
        new(CoreShellIdFormatting.Normalize(value));

    public override string ToString() => Value;
}

public interface ICoreShellContentResolver
{
    Type ResolveContentType(CoreShellContentReference content);
}

public interface ICoreShellModuleProvider
{
    IEnumerable<CoreShellModule> GetModules();
}
