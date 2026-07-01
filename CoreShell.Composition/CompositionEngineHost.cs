namespace CoreShell.Composition;

public sealed class CompositionEngineHost
{
    private readonly List<CompositionModule> _modules;

    public CompositionEngineHost()
        : this([])
    {
    }

    public CompositionEngineHost(IEnumerable<CompositionModule> modules)
    {
        ArgumentNullException.ThrowIfNull(modules);

        _modules = modules.ToList();
        Registry = CompositionRegistry.FromModules(_modules);
    }

    public CompositionRegistry Registry { get; private set; }

    public IReadOnlyList<CompositionModule> Modules => _modules;

    public void Mount(CompositionModule module)
    {
        ArgumentNullException.ThrowIfNull(module);

        var candidateModules = _modules.Append(module).ToArray();
        var candidateRegistry = CompositionRegistry.FromModules(candidateModules);

        _modules.Add(module);
        Registry = candidateRegistry;
    }

    public bool Unmount(CompositionModuleId moduleId)
    {
        var index = _modules.FindIndex(module => module.Id == moduleId);
        if (index < 0)
        {
            return false;
        }

        var candidateModules = _modules
            .Where((_, moduleIndex) => moduleIndex != index)
            .ToArray();
        var candidateRegistry = CompositionRegistry.FromModules(candidateModules);

        _modules.RemoveAt(index);
        Registry = candidateRegistry;
        return true;
    }
}
