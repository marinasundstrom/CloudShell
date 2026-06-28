namespace CloudShell.ResourceDefinitions.Tests;

public sealed class ReferenceProviderDocumentationTests
{
    private static readonly string[] RequiredProviderReadmeHeadings =
    [
        "## Overview",
        "## Ported",
        "## Switch-over status",
        "## Remaining"
    ];

    [Fact]
    public void ResourceTypeProviderFolders_HaveProviderReadme()
    {
        var (repositoryRoot, providerDirectories) = GetResourceTypeProviderDirectories();

        Assert.NotEmpty(providerDirectories);
        foreach (var providerDirectory in providerDirectories)
        {
            Assert.True(
                File.Exists(Path.Combine(providerDirectory, "README.md")),
                $"Resource provider folder '{Path.GetRelativePath(repositoryRoot, providerDirectory)}' should document porting status and provider shape in README.md.");
        }
    }

    [Fact]
    public void ResourceTypeProviderReadmes_HavePortingStatusSections()
    {
        var (repositoryRoot, providerDirectories) = GetResourceTypeProviderDirectories();

        Assert.NotEmpty(providerDirectories);
        foreach (var providerDirectory in providerDirectories)
        {
            var readmePath = Path.Combine(providerDirectory, "README.md");
            var markdown = File.ReadAllText(readmePath);
            foreach (var heading in RequiredProviderReadmeHeadings)
            {
                Assert.True(
                    markdown.Contains(heading, StringComparison.Ordinal),
                    $"Resource provider README '{Path.GetRelativePath(repositoryRoot, readmePath)}' should contain a '{heading}' section.");
            }
        }
    }

    private static (string RepositoryRoot, IReadOnlyList<string> ProviderDirectories)
        GetResourceTypeProviderDirectories()
    {
        var repositoryRoot = FindRepositoryRoot();
        var providerRoot = Path.Combine(
            repositoryRoot,
            "CloudShell.ResourceDefinitions.ReferenceProviders");
        var providerDirectories = Directory
            .EnumerateFiles(providerRoot, "*ResourceTypeProvider.cs", SearchOption.AllDirectories)
            .Select(Path.GetDirectoryName)
            .Where(directory => !string.IsNullOrWhiteSpace(directory))
            .Select(directory => directory!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return (repositoryRoot, providerDirectories);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CloudShell.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the CloudShell repository root.");
    }
}
