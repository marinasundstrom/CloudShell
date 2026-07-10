using CloudShell.Abstractions.ResourceManager;
using CloudShell.ResourceModel;

namespace CloudShell.ControlPlane.Providers;

public sealed class AspNetCoreProjectArtifactLayoutProvider : IDeploymentArtifactLayoutProvider
{
    public ResourceTypeId TypeId => AspNetCoreProjectResourceTypeProvider.ResourceTypeId;

    public ValueTask<IReadOnlyList<DeploymentArtifactLayoutDescriptor>> GetDeploymentArtifactLayoutsAsync(
        DeploymentArtifactLayoutQuery query,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyList<DeploymentArtifactLayoutDescriptor>>(
        [
            new(
                TypeId,
                "dotnetPublishedOutput",
                ".NET published output",
                "A package containing compiled output from dotnet publish.",
                ["zip"],
                DefaultPackageKind: "zip",
                DefaultEntryPath: ".",
                IsDefault: true),
            new(
                TypeId,
                "dotnetProjectSource",
                ".NET project source",
                "A package containing a .NET project directory.",
                ["zip", "tar.gz"],
                DefaultPackageKind: "zip",
                DefaultEntryPath: ".",
                EntryPathRequired: true)
        ]);
}

public sealed class PythonAppArtifactLayoutProvider : IDeploymentArtifactLayoutProvider
{
    public ResourceTypeId TypeId => PythonAppResourceTypeProvider.ResourceTypeId;

    public ValueTask<IReadOnlyList<DeploymentArtifactLayoutDescriptor>> GetDeploymentArtifactLayoutsAsync(
        DeploymentArtifactLayoutQuery query,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyList<DeploymentArtifactLayoutDescriptor>>(
        [
            new(
                TypeId,
                "pythonSourceDirectory",
                "Python source directory",
                "A package containing Python application source files.",
                ["zip", "tar.gz"],
                DefaultPackageKind: "zip",
                DefaultEntryPath: ".",
                IsDefault: true)
        ]);
}

public sealed class JavaAppArtifactLayoutProvider : IDeploymentArtifactLayoutProvider
{
    public ResourceTypeId TypeId => JavaAppResourceTypeProvider.ResourceTypeId;

    public ValueTask<IReadOnlyList<DeploymentArtifactLayoutDescriptor>> GetDeploymentArtifactLayoutsAsync(
        DeploymentArtifactLayoutQuery query,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyList<DeploymentArtifactLayoutDescriptor>>(
        [
            new(
                TypeId,
                "javaCompiledArtifact",
                "Java compiled artifact",
                "A package containing a built JAR or application distribution.",
                ["zip", "tar.gz"],
                DefaultPackageKind: "zip",
                DefaultEntryPath: ".",
                IsDefault: true),
            new(
                TypeId,
                "javaProjectSource",
                "Java project source",
                "A package containing a Maven or Gradle project directory.",
                ["zip", "tar.gz"],
                DefaultPackageKind: "zip",
                DefaultEntryPath: ".",
                EntryPathRequired: true)
        ]);
}

public sealed class JavaScriptAppArtifactLayoutProvider : IDeploymentArtifactLayoutProvider
{
    public ResourceTypeId TypeId => JavaScriptAppResourceTypeProvider.ResourceTypeId;

    public ValueTask<IReadOnlyList<DeploymentArtifactLayoutDescriptor>> GetDeploymentArtifactLayoutsAsync(
        DeploymentArtifactLayoutQuery query,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyList<DeploymentArtifactLayoutDescriptor>>(
        [
            new(
                TypeId,
                "javascriptSourceDirectory",
                "JavaScript source directory",
                "A package containing JavaScript application source files.",
                ["zip", "tar.gz"],
                DefaultPackageKind: "zip",
                DefaultEntryPath: ".",
                IsDefault: true)
        ]);
}

public sealed class GoAppArtifactLayoutProvider : IDeploymentArtifactLayoutProvider
{
    public ResourceTypeId TypeId => GoAppResourceTypeProvider.ResourceTypeId;

    public ValueTask<IReadOnlyList<DeploymentArtifactLayoutDescriptor>> GetDeploymentArtifactLayoutsAsync(
        DeploymentArtifactLayoutQuery query,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyList<DeploymentArtifactLayoutDescriptor>>(
        [
            new(
                TypeId,
                "goSourceDirectory",
                "Go source directory",
                "A package containing Go application source files.",
                ["zip", "tar.gz"],
                DefaultPackageKind: "zip",
                DefaultEntryPath: ".",
                IsDefault: true),
            new(
                TypeId,
                "goCompiledBinary",
                "Go compiled binary",
                "A package containing a compiled Go application binary.",
                ["zip", "tar.gz"],
                DefaultPackageKind: "zip",
                DefaultEntryPath: ".")
        ]);
}

public sealed class ApplicationArtifactValidationProvider : IDeploymentArtifactValidationProvider
{
    private static readonly HashSet<string> SupportedResourceTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        AspNetCoreProjectResourceTypeProvider.ResourceTypeId.ToString(),
        PythonAppResourceTypeProvider.ResourceTypeId.ToString(),
        JavaAppResourceTypeProvider.ResourceTypeId.ToString(),
        JavaScriptAppResourceTypeProvider.ResourceTypeId.ToString(),
        GoAppResourceTypeProvider.ResourceTypeId.ToString()
    };

    public string Id => "cloudshell.application-artifacts.validation";

    public bool CanValidate(DeploymentArtifactValidationContext context) =>
        SupportedResourceTypes.Contains(context.ResourceType);

    public ValueTask<ResourceDefinitionValidationResult> ValidateDeploymentArtifactAsync(
        DeploymentArtifactValidationContext context,
        Stream artifactContent,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(
            ApplicationArtifactResourceValidation.ValidateCommittedArtifact(
                context,
                artifactContent));
}
