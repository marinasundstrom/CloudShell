using System.Text.RegularExpressions;

namespace CloudShell.ControlPlane.Tests;

public sealed class ProviderExecutionBoundaryArchitectureTests
{
    private static readonly Regex ForbiddenOperationProviderCallPattern = new(
        """
        \.(InspectAsync|SetupAsync|ReconcileAsync|EnsureCreatedAsync|ExecuteLifecycleAsync)\s*\(
        |(?:^|[^\w])(?:_?runtimeController|_?runtimeHandler)\.ExecuteAsync\s*\(
        """,
        RegexOptions.Compiled | RegexOptions.IgnorePatternWhitespace);

    [Fact]
    public void OperationProviders_DoNotCallRuntimeExecutionSeamsDirectly()
    {
        var providersRoot = Path.Combine(FindRepositoryRoot(), "CloudShell.ControlPlane.Providers");
        var operationProviders = Directory
            .EnumerateFiles(providersRoot, "*OperationProvider.cs", SearchOption.AllDirectories)
            .Where(path => path.Split(Path.DirectorySeparatorChar).Contains("Operations"))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(operationProviders);

        var violations = operationProviders
            .SelectMany(path => FindViolations(providersRoot, path))
            .ToArray();

        Assert.True(
            violations.Length == 0,
            "Resource operation providers must dispatch provider execution instructions instead of calling runtime seams directly:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
    }

    private static IEnumerable<string> FindViolations(
        string providersRoot,
        string path)
    {
        var relativePath = Path.GetRelativePath(providersRoot, path);
        var lineNumber = 0;

        foreach (var line in File.ReadLines(path))
        {
            lineNumber++;
            if (ForbiddenOperationProviderCallPattern.IsMatch(line))
            {
                yield return $"{relativePath}:{lineNumber}: {line.Trim()}";
            }
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CloudShell.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the CloudShell repository root.");
    }
}
