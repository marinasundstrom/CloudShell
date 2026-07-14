namespace CloudShell.ControlPlane.Providers;

public sealed class DeviceRegistryStartExecutionHandler(
    IDeviceRegistryRuntimeController? runtimeController = null)
    : DeviceRegistryLifecycleExecutionHandler(
        DeviceRegistryResourceTypeProvider.Operations.Start,
        ProviderExecutionInstructionTypes.DeviceRegistryStart,
        runtimeController);

public sealed class DeviceRegistryStopExecutionHandler(
    IDeviceRegistryRuntimeController? runtimeController = null)
    : DeviceRegistryLifecycleExecutionHandler(
        DeviceRegistryResourceTypeProvider.Operations.Stop,
        ProviderExecutionInstructionTypes.DeviceRegistryStop,
        runtimeController);

public sealed class DeviceRegistryRestartExecutionHandler(
    IDeviceRegistryRuntimeController? runtimeController = null)
    : DeviceRegistryLifecycleExecutionHandler(
        DeviceRegistryResourceTypeProvider.Operations.Restart,
        ProviderExecutionInstructionTypes.DeviceRegistryRestart,
        runtimeController);

public abstract class DeviceRegistryLifecycleExecutionHandler(
    ResourceOperationId operationId,
    string instructionType,
    IDeviceRegistryRuntimeController? runtimeController = null) : IProviderExecutionHandler
{
    private readonly ResourceOperationId _operationId = operationId;
    private readonly IDeviceRegistryRuntimeController _runtimeController =
        runtimeController ?? new NoopDeviceRegistryRuntimeController();

    public string InstructionType { get; } = instructionType;

    public IReadOnlyList<string> Capabilities =>
    [
        ProviderExecutionCapabilities.Processes
    ];

    public async ValueTask<ProviderExecutionResult> ExecuteAsync(
        ProviderExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        var context = request.TryCreateProjectionExecutionContext();
        if (context is null)
        {
            return ProviderExecutionResult.Unavailable(
                request,
                ProviderExecutionDiagnosticCodes.ResourceSnapshotMissing,
                $"Provider execution instruction '{request.InstructionType}' requires a resource snapshot for '{request.TargetResourceId}'.");
        }

        var diagnostics = await _runtimeController.ExecuteAsync(
            context.Resource,
            _operationId,
            cancellationToken);

        return diagnostics.Any(diagnostic =>
            diagnostic.Severity == ResourceDefinitionDiagnosticSeverity.Error)
            ? ProviderExecutionResult.Failed(request, diagnostics)
            : ProviderExecutionResult.Succeeded(request, diagnostics: diagnostics);
    }
}
