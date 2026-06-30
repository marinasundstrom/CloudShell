namespace CloudShell.ControlPlane.Providers;

public sealed class ExecutableApplicationResourceDefinitionBuilder(string name) :
    ResourceDefinitionBuilder<ExecutableApplicationResourceDefinitionBuilder>(name)
{
    private readonly List<VolumeMountDefinition> _volumeMounts = [];
    private string? _path;
    private string? _arguments;
    private string? _workingDirectory;

    protected override ResourceTypeId TypeId =>
        ExecutableApplicationResourceTypeProvider.ResourceTypeId;

    protected override string? ProviderId =>
        ExecutableApplicationResourceTypeProvider.ProviderId;

    public ExecutableApplicationResourceDefinitionBuilder WithRuntimeMonitoring() =>
        DeclareCapability(ResourceCommonCapabilityIds.Monitoring);

    public ExecutableApplicationResourceDefinitionBuilder WithExecutablePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        _path = path.Trim();
        SetScalarAttribute(ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath, _path);
        UpdateConfiguration();
        return this;
    }

    public ExecutableApplicationResourceDefinitionBuilder WithArguments(string? arguments)
    {
        _arguments = string.IsNullOrWhiteSpace(arguments) ? null : arguments.Trim();
        UpdateConfiguration();
        return this;
    }

    public ExecutableApplicationResourceDefinitionBuilder WithWorkingDirectory(string? workingDirectory)
    {
        _workingDirectory = string.IsNullOrWhiteSpace(workingDirectory) ? null : workingDirectory.Trim();
        UpdateConfiguration();
        return this;
    }

    public ExecutableApplicationResourceDefinitionBuilder WithCommand(
        string path,
        string? arguments = null,
        string? workingDirectory = null)
    {
        WithExecutablePath(path);
        WithArguments(arguments);
        WithWorkingDirectory(workingDirectory);
        return this;
    }

    public ExecutableApplicationResourceDefinitionBuilder MountVolume(
        IResourceDefinitionBuilder volume,
        string targetPath,
        bool readOnly = false)
    {
        ArgumentNullException.ThrowIfNull(volume);

        return MountVolume(volume.EffectiveResourceId, targetPath, readOnly);
    }

    public ExecutableApplicationResourceDefinitionBuilder MountVolume(
        string volumeResourceId,
        string targetPath,
        bool readOnly = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(volumeResourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);

        _volumeMounts.Add(new(
            volumeResourceId.Trim(),
            targetPath.Trim(),
            readOnly));
        return SetCapability(
            VolumeConsumerCapabilityProvider.CapabilityIdValue,
            new VolumeConsumerDefinition(_volumeMounts.ToArray()));
    }

    private void UpdateConfiguration()
    {
        if (string.IsNullOrWhiteSpace(_path))
        {
            return;
        }

        SetConfiguration(
            ExecutableApplicationResourceTypeProvider.ConfigurationSection,
            new ExecutableApplicationConfiguration(_path, _arguments, _workingDirectory));
    }
}

public static class ExecutableApplicationResourceDefinitionBuilderExtensions
{
    public static ExecutableApplicationResourceDefinitionBuilder AddExecutableApplication(
        this ResourceDefinitionGraphBuilder graph,
        string name)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var builder = new ExecutableApplicationResourceDefinitionBuilder(name);
        builder.WithRuntimeMonitoring();
        graph.Add(builder);
        return builder;
    }
}
