using CloudShell.Abstractions.Logs;

namespace CloudShell.Abstractions.ResourceManager;

public sealed record ResourceProcedureContext(
    Resource Resource,
    ResourceRegistration? Registration,
    string? ResourceGroupId,
    IResourceRegistrationStore Registrations,
    IResourceManagerStore? ResourceManager = null,
    string? PreferredContainerHostId = null,
    string? TriggeredBy = null,
    string? Cause = null,
    IResourceEventSink? ResourceEvents = null)
{
    public void AppendProviderEvent(
        string providerId,
        string eventName,
        string message,
        string level = "Information")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        var effectiveMessage = string.IsNullOrWhiteSpace(Cause)
            ? message.Trim()
            : $"{message.Trim().TrimEnd('.')} Cause: {Cause.Trim().TrimEnd('.')}.";

        ResourceEvents?.Append(new ResourceEvent(
            Resource.Id,
            ResourceEventTypes.Events.Provider.ForEvent(providerId, eventName),
            effectiveMessage,
            DateTimeOffset.UtcNow,
            TriggeredBy,
            level));
    }
}
