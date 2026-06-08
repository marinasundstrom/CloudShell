namespace CloudShell.Abstractions.ResourceManager;

public sealed record ResourceAutoStartPolicy(
    bool? StartOnControlPlaneStart = null,
    bool? StartAsDependency = null,
    bool? StartAfterCreate = null);

public interface IResourceAutoStartPolicyProvider : IResourceProvider
{
    bool CanEvaluateAutoStartPolicy(ResourceDeclaration declaration);

    ResourceAutoStartPolicy GetAutoStartPolicy(ResourceDeclaration declaration);
}
