namespace CloudShell.Abstractions.ResourceManager;

public sealed record ResourceProcedureContext(
    CloudResource Resource,
    ResourceRegistration? Registration,
    string? ResourceGroupId,
    IResourceRegistrationStore Registrations);
