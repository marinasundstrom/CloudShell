namespace CoreShell;

public sealed record CoreShellUiExtensionManifest(
    CoreShellModuleId Id,
    string DisplayName,
    string? Description = null,
    string? Version = null);

public interface ICoreShellUiExtension
{
    CoreShellUiExtensionManifest Manifest { get; }

    void Configure(CoreShellModuleBuilder builder);
}

public sealed class CoreShellUiExtensionRegistry(
    IEnumerable<ICoreShellUiExtension> extensions) : ICoreShellModuleProvider
{
    private readonly IReadOnlyList<ICoreShellUiExtension> _extensions =
        extensions?.ToArray() ?? throw new ArgumentNullException(nameof(extensions));

    public IReadOnlyList<ICoreShellUiExtension> Extensions => _extensions;

    public IEnumerable<CoreShellModule> GetModules()
    {
        foreach (var extension in _extensions)
        {
            yield return CoreShellModule.Create(
                extension.Manifest.Id,
                extension.Configure);
        }
    }
}
