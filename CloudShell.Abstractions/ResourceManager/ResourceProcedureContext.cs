namespace CloudShell.Abstractions.ResourceManager;

public sealed record ResourceProcedureContext(
    Resource Resource,
    ResourceRegistration? Registration,
    string? ResourceGroupId,
    IResourceRegistrationStore Registrations,
    IResourceManagerStore? ResourceManager = null,
    string? PreferredContainerEngineId = null);
