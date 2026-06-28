namespace CloudShell.ResourceDefinitions.Tests;

public sealed class ReferenceProviderDocumentationTests
{
    [Fact]
    public void ResourceTypeProviderFolders_HaveProviderReadme()
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

        Assert.NotEmpty(providerDirectories);
        foreach (var providerDirectory in providerDirectories)
        {
            Assert.True(
                File.Exists(Path.Combine(providerDirectory, "README.md")),
                $"Resource provider folder '{Path.GetRelativePath(repositoryRoot, providerDirectory)}' should document porting status and provider shape in README.md.");
        }
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
