using CoreShell;
using CoreShell.Composition;

namespace CloudShell.Hosting.Shell;

internal sealed class CoreShellCompositionModuleFactory(
    ICoreShellContentResolver contentResolver)
{
    private static readonly CompositionModuleId ProjectedModuleId =
        CompositionModuleId.Create("cloudshell.coreshell.projected");

    public CompositionModule CreateModule(IEnumerable<CoreShellModule> modules)
    {
        ArgumentNullException.ThrowIfNull(modules);

        var projector = new CoreShellCompositionProjector(contentResolver);
        var projectedModules = modules
            .Select(projector.CreateModule)
            .ToArray();

        return new CompositionModule(
            ProjectedModuleId,
            projectedModules.SelectMany(module => module.Pages).ToArray(),
            projectedModules.SelectMany(module => module.Menus).ToArray(),
            projectedModules.SelectMany(module => module.SectionOutlets).ToArray(),
            projectedModules.SelectMany(module => module.Sections).ToArray());
    }
}
