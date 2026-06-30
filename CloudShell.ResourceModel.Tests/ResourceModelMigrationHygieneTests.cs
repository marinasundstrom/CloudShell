namespace CloudShell.ResourceModel.Tests;

public sealed class ResourceModelMigrationHygieneTests
{
    [Fact]
    public void ActiveRepositoryFiles_DoNotReferenceDeletedLegacyProviderProjects()
    {
        var root = LocateRepositoryRoot();
        var activeFiles = EnumerateActiveSourceFiles(root);
        var forbiddenTerms = new[]
        {
            DeletedProvider("Applications"),
            DeletedProvider("Configuration"),
            DeletedProvider("Docker") + "/",
            DeletedProvider("Docker") + "\\",
            "CloudShell.Resource" + "Definitions",
            "Reference" + "Providers",
            "ResourceDeployment" + "Definition",
            "ResourceGroup" + "Template",
            "ResourceTemplate" + "Definition",
            "IResourceTemplate" + "Provider"
        };

        var matches = activeFiles
            .SelectMany(file => FindMatches(root, file, forbiddenTerms))
            .ToArray();

        Assert.Empty(matches);
    }

    [Fact]
    public void Repository_DoesNotContainDeletedLegacyProviderProjectFolders()
    {
        var root = LocateRepositoryRoot();
        var deletedProviderFolders = new[]
        {
            DeletedProvider("Applications"),
            DeletedProvider("Configuration"),
            DeletedProvider("Docker")
        };

        var existingFolders = deletedProviderFolders
            .Select(folder => Path.Combine(root, folder))
            .Where(Directory.Exists)
            .ToArray();

        Assert.Empty(existingFolders);
    }

    private static IEnumerable<string> EnumerateActiveSourceFiles(string root)
    {
        var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".cs",
            ".csproj",
            ".props",
            ".targets",
            ".slnx"
        };

        return Directory
            .EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(file => extensions.Contains(Path.GetExtension(file)))
            .Where(file => !IsExcludedPath(root, file));
    }

    private static IEnumerable<string> FindMatches(
        string root,
        string file,
        IReadOnlyList<string> forbiddenTerms)
    {
        var relativePath = Path.GetRelativePath(root, file);
        var text = File.ReadAllText(file);
        return forbiddenTerms
            .Where(term => text.Contains(term, StringComparison.Ordinal))
            .Select(term => $"{relativePath}: {term}");
    }

    private static string DeletedProvider(string providerName) =>
        "CloudShell.Providers." + providerName;

    private static bool IsExcludedPath(string root, string file)
    {
        var relativePath = Path.GetRelativePath(root, file);
        var segments = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Any(segment =>
            string.Equals(segment, ".git", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(segment, "bin", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(segment, "obj", StringComparison.OrdinalIgnoreCase));
    }

    private static string LocateRepositoryRoot()
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
