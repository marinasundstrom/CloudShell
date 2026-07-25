namespace CloudShell.Hosting.Tests;

public sealed class AccessibilityContractTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void MainLayout_ProvidesSkipNavigationAndNamedLandmarks()
    {
        var source = ReadHostingFile("Components/Layout/MainLayout.razor");

        Assert.Contains("class=\"skip-link\" href=\"#main-content\"", source);
        Assert.Contains("id=\"main-content\" class=\"shell-main\" tabindex=\"-1\"", source);
        Assert.Contains("aria-label=\"@L[", source);
        Assert.Contains("Primary navigation", source);
    }

    [Fact]
    public void CompactDrawer_ContainsFocusAndMakesBackgroundInert()
    {
        var source = ReadHostingFile("wwwroot/shell.js");

        Assert.Contains("main.inert = isOpenDrawer;", source);
        Assert.Contains("sidebar.setAttribute(\"aria-modal\", \"true\")", source);
        Assert.Contains("function trapDrawerFocus(event)", source);
        Assert.Contains("event.shiftKey && activeElement === first", source);
        Assert.Contains("!event.shiftKey && activeElement === last", source);
    }

    [Fact]
    public void SharedStyles_PreserveKeyboardFocusAndReducedMotionPreferences()
    {
        var source = ReadHostingFile("wwwroot/app.css");

        Assert.Contains("):focus-visible {", source);
        Assert.Contains("outline: 3px solid var(--cloudshell-focus-ring);", source);
        Assert.Contains("@media (prefers-reduced-motion: reduce)", source);
        Assert.Contains("animation-iteration-count: 1 !important;", source);
        Assert.Contains("transition-duration: .01ms !important;", source);
    }

    [Fact]
    public void NotificationPopover_ExposesStateAndFocusRelationships()
    {
        var source = ReadHostingFile("Components/Layout/ShellNotifications.razor");

        Assert.Contains("aria-haspopup=\"dialog\"", source);
        Assert.Contains("aria-controls=\"shell-notifications-panel\"", source);
        Assert.Contains("aria-expanded=\"@IsPanelOpen.ToString().ToLowerInvariant()\"", source);
        Assert.Contains("role=\"dialog\"", source);
        Assert.Contains("aria-labelledby=\"shell-notifications-title\"", source);
        Assert.Contains("HandlePanelKeyDownAsync", source);
        Assert.Contains("\"shell-notifications-trigger\"", source);
    }

    [Fact]
    public void IconOnlyActions_KeepAccessibleText()
    {
        var themeSource = ReadHostingFile("Components/Layout/ThemeSelector.razor");
        var resourcesSource = ReadHostingFile("Components/Pages/Resources/Resources.razor");
        var updateSource = ReadHostingFile("Components/Pages/Resources/UpdateResource.razor");

        Assert.Contains("<span class=\"visually-hidden\">", themeSource);
        Assert.Contains("@L[\"Theme\"]", themeSource);
        Assert.Contains("L[\"More actions for {0}\", GetResourceLabel(resource)]", resourcesSource);
        Assert.Contains("builder.AddAttribute(sequence++, \"aria-label\", actionState.Label);", resourcesSource);
        Assert.Contains("L[\"More actions for {0}\", GetResourceLabel(resource)]", updateSource);
        Assert.Contains("builder.AddAttribute(sequence++, \"aria-label\", actionState.Label);", updateSource);
    }

    private static string ReadHostingFile(string relativePath) =>
        File.ReadAllText(Path.Combine(RepositoryRoot, "CloudShell.Hosting", relativePath));

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

        throw new DirectoryNotFoundException("Could not locate the CloudShell repository root.");
    }
}
