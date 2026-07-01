using CoreShell.Composition;

namespace CoreShell.Composition.Tests;

public sealed class CompositionIdTests
{
    [Fact]
    public void Create_CreatesRootIds()
    {
        Assert.Equal("composition-module.reporting", CompositionModuleId.Create("reporting").Value);
        Assert.Equal("menu.main", MenuId.Create("main").Value);
        Assert.Equal("page.workspace", PageId.Create("workspace").Value);
    }

    [Fact]
    public void Create_CreatesMenuChildIds()
    {
        var menu = MenuId.Create("main");
        var group = MenuGroupId.Create(menu, "observability");

        Assert.Equal("menu-group.main.observability", group.Value);
        Assert.Equal("menu-item.main.resources", MenuItemId.Create(menu, "resources").Value);
        Assert.Equal("menu-item.main.observability.traces", MenuItemId.Create(group, "traces").Value);
    }

    [Fact]
    public void Create_CreatesContentChildIds()
    {
        var page = PageId.Create("workspace");
        var outlet = SectionOutletId.Create(page, "main");
        var section = SectionId.Create(outlet, "overview");

        Assert.Equal("section-outlet.workspace.main", outlet.Value);
        Assert.Equal("section.workspace.main.overview", section.Value);
        Assert.Equal("section.workspace.details", SectionId.Create(page, "details").Value);
        Assert.Equal("section.workspace.main.overview.summary", SectionId.Create(section, "summary").Value);
        Assert.Equal(
            "section-outlet.workspace.main.overview.actions",
            SectionOutletId.Create(section, "actions").Value);
    }

    [Fact]
    public void Create_TrimsIdentifierBoundaries()
    {
        var page = PageId.Create(" workspace ");

        var section = SectionId.Create(page, ".overview.");

        Assert.Equal("section.workspace.overview", section.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("...")]
    public void Create_RejectsEmptyIdentifiers(string identifier)
    {
        var exception = Assert.Throws<ArgumentException>(() => PageId.Create(identifier));

        Assert.Contains("cannot be empty", exception.Message);
    }
}
