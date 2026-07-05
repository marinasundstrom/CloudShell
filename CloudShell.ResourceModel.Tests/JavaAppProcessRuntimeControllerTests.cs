using System.Runtime.InteropServices;
using CloudShell.ControlPlane.Providers;
using ResourceGraphState = CloudShell.ResourceModel.ResourceState;

namespace CloudShell.ResourceModel.Tests;

public sealed class JavaAppProcessRuntimeControllerTests
{
    [Fact]
    public void CommandFactory_CreatesMavenBuildCommandWithWrapperWhenPresent()
    {
        using var project = TemporaryProjectDirectory.Create();
        project.WriteExecutable(
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? "mvnw.cmd"
                : "mvnw",
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? "@echo off\r\nexit /b 0\r\n"
                : "#!/usr/bin/env sh\nexit 0\n");
        var resource = CreateResource(
            project.Path,
            buildTool: JavaAppBuildTools.Maven,
            buildArguments: "clean package -DskipTests");

        var command = new JavaAppProcessCommandFactory()
            .CreateBuildStartInfo(resource, project.Path);

        Assert.NotNull(command);
        Assert.Equal(
            Path.Combine(
                project.Path,
                RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                    ? "mvnw.cmd"
                    : "mvnw"),
            command.FileName);
        Assert.Equal(project.Path, command.WorkingDirectory);
        Assert.Equal(
            ["clean", "package", "-DskipTests"],
            command.ArgumentList.ToArray());
        Assert.False(command.UseShellExecute);
        Assert.True(command.RedirectStandardOutput);
        Assert.True(command.RedirectStandardError);
    }

    [Fact]
    public void CommandFactory_CreatesGradleBuildCommandWithDefaultArguments()
    {
        var resource = CreateResource(
            "/repo/app",
            buildTool: JavaAppBuildTools.Gradle);

        var command = new JavaAppProcessCommandFactory()
            .CreateBuildStartInfo(resource, "/repo/app");

        Assert.NotNull(command);
        Assert.Equal("gradle", command.FileName);
        Assert.Equal(["build"], command.ArgumentList.ToArray());
    }

    [Fact]
    public async Task TypeProvider_RejectsUnsupportedBuildTool()
    {
        var resource = CreateResource(
            "src/app",
            buildTool: "ant");
        var provider = new JavaAppResourceTypeProvider();

        var result = await provider.ValidateAsync(
            resource,
            new ResourceProviderContext());

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(ResourceDefinitionDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("application.javaApp.buildToolUnsupported", diagnostic.Code);
        Assert.Equal(JavaAppResourceTypeProvider.Attributes.BuildTool, diagnostic.Target);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsBuildDiagnosticWhenGradleBuildFails()
    {
        await using var controller = new JavaAppProcessRuntimeController();
        using var project = TemporaryProjectDirectory.Create();
        project.WriteExecutable(
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? "gradlew.bat"
                : "gradlew",
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? "@echo off\r\necho build failed marker\r\nexit /b 1\r\n"
                : "#!/usr/bin/env sh\necho build failed marker\nexit 1\n");
        var resource = CreateResource(
            project.Path,
            buildTool: JavaAppBuildTools.Gradle);

        var diagnostics = await controller.ExecuteAsync(
            resource,
            JavaAppResourceTypeProvider.Operations.Start);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(ResourceDefinitionDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("application.javaApp.buildFailed", diagnostic.Code);
        Assert.Equal(JavaAppRuntimeStatus.Stopped, controller.GetStatus(resource));
        Assert.Contains(
            controller.ReadOutput(resource.EffectiveResourceId),
            setting => setting.Stream == "build" &&
                setting.Message.Contains("build failed marker", StringComparison.Ordinal));
    }

    private static Resource CreateResource(
        string projectPath,
        string name = "api",
        string artifactPath = "target/app.jar",
        string? buildTool = null,
        string? buildArguments = null)
    {
        var attributes = new Dictionary<ResourceAttributeId, ResourceAttributeValue>
        {
            [JavaAppResourceTypeProvider.Attributes.ProjectPath] = projectPath,
            [JavaAppResourceTypeProvider.Attributes.Command] = "java",
            [JavaAppResourceTypeProvider.Attributes.ArtifactPath] = artifactPath
        };

        if (buildTool is not null)
        {
            attributes[JavaAppResourceTypeProvider.Attributes.BuildTool] = buildTool;
        }

        if (buildArguments is not null)
        {
            attributes[JavaAppResourceTypeProvider.Attributes.BuildArguments] = buildArguments;
        }

        var resolver = new ResourceResolver(
            [JavaAppResourceTypeProvider.ClassDefinition],
            [new JavaAppResourceTypeProvider().TypeDefinition]);

        return resolver.Resolve(new ResourceGraphState(
            name,
            JavaAppResourceTypeProvider.ResourceTypeId,
            ProviderId: JavaAppResourceTypeProvider.ProviderId,
            Attributes: attributes));
    }

    private sealed class TemporaryProjectDirectory : IDisposable
    {
        private TemporaryProjectDirectory(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TemporaryProjectDirectory Create()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "cloudshell-java-app-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TemporaryProjectDirectory(path);
        }

        public void WriteExecutable(string fileName, string contents)
        {
            var path = System.IO.Path.Combine(Path, fileName);
            File.WriteAllText(path, contents);
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                File.SetUnixFileMode(
                    path,
                    UnixFileMode.UserRead |
                    UnixFileMode.UserWrite |
                    UnixFileMode.UserExecute);
            }
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
