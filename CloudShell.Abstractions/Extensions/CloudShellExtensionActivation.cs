namespace CloudShell.Abstractions.Extensions;

public enum CloudShellExtensionActivationPolicy
{
    UserManaged,
    Enabled,
    Disabled
}

public enum CloudShellExtensionActivationState
{
    Enabled,
    Disabled
}

public enum CloudShellExtensionStatusKind
{
    Enabled,
    Disabled,
    EnabledByHost,
    DisabledByHost,
    Blocked
}

public sealed record CloudShellExtensionActivationSetting(
    string ExtensionId,
    CloudShellExtensionActivationState State,
    DateTimeOffset UpdatedAt,
    string? UpdatedBy = null);

public sealed record CloudShellExtensionStatus(
    CloudShellExtensionRegistration Extension,
    CloudShellExtensionStatusKind Kind,
    string? Reason = null)
{
    public bool IsActive =>
        Kind is CloudShellExtensionStatusKind.Enabled or CloudShellExtensionStatusKind.EnabledByHost;

    public bool IsUserManaged => Extension.ActivationPolicy == CloudShellExtensionActivationPolicy.UserManaged;
}

public interface ICloudShellExtensionActivationStore
{
    IReadOnlyDictionary<string, CloudShellExtensionActivationState> GetActivationStates();

    CloudShellExtensionActivationState? GetActivationState(string extensionId);

    Task SetActivationStateAsync(
        string extensionId,
        CloudShellExtensionActivationState state,
        string? updatedBy = null,
        CancellationToken cancellationToken = default);
}

public sealed class InMemoryCloudShellExtensionActivationStore : ICloudShellExtensionActivationStore
{
    private readonly Dictionary<string, CloudShellExtensionActivationSetting> settings =
        new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, CloudShellExtensionActivationState> GetActivationStates() =>
        settings.ToDictionary(
            setting => setting.Key,
            setting => setting.Value.State,
            StringComparer.OrdinalIgnoreCase);

    public CloudShellExtensionActivationState? GetActivationState(string extensionId) =>
        settings.TryGetValue(extensionId, out var setting)
            ? setting.State
            : null;

    public Task SetActivationStateAsync(
        string extensionId,
        CloudShellExtensionActivationState state,
        string? updatedBy = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extensionId);

        settings[extensionId] = new CloudShellExtensionActivationSetting(
            extensionId,
            state,
            DateTimeOffset.UtcNow,
            updatedBy);

        return Task.CompletedTask;
    }
}
