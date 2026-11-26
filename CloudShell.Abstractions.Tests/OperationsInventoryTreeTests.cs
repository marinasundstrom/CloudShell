using CloudShell.Abstractions.ResourceManager;
using CloudShell.Hosting.ResourceManager;

namespace CloudShell.Abstractions.Tests;

public sealed class OperationsInventoryTreeTests
{
    [Fact]
    public void ChildrenFollowParentAndCollapseDoesNotHideUnrelatedResources()
    {
        Resource[] resources = [Item("child", "parent"), Item("other"), Item("parent")];
        var rows = OperationsInventoryTree.Create(resources, new HashSet<string>(), "", r => r.Name);
        Assert.Equal(new[] { "other", "parent", "child" }, rows.Select(r => r.Resource.Id));
        Assert.Equal(1, rows.Last().Depth);
        Assert.Equal(1, rows[1].ChildCount);
        rows = OperationsInventoryTree.Create(resources, new HashSet<string> { "parent" }, "", r => r.Name);
        Assert.Equal(new[] { "other", "parent" }, rows.Select(r => r.Resource.Id));
    }

    [Fact]
    public void FilterRevealsMatchingDescendantAndItsAncestors()
    {
        Resource[] resources = [Item("root"), Item("middle", "root"), Item("needle", "middle"), Item("unrelated", "root")];
        var rows = OperationsInventoryTree.Create(resources, new HashSet<string> { "root", "middle" }, "needle", r => r.Name);
        Assert.Equal(new[] { "root", "middle", "needle" }, rows.Select(r => r.Resource.Id));
        Assert.All(rows, row => Assert.True(row.Expanded));
    }

    [Fact]
    public void MissingParentDoesNotExposeHiddenResourceOrHideVisibleChild()
    {
        var rows = OperationsInventoryTree.Create([Item("child", "hidden")], new HashSet<string>(), "", r => r.Name);
        Assert.Equal("child", Assert.Single(rows).Resource.Id);
        Assert.Equal(0, rows[0].Depth);
    }

    [Fact]
    public void CyclesRenderEachVisibleResourceOnce()
    {
        var rows = OperationsInventoryTree.Create([Item("a", "b"), Item("b", "a")], new HashSet<string>(), "", r => r.Name);
        Assert.Equal(2, rows.Count);
        Assert.Equal(2, rows.Select(row => row.Resource.Id).Distinct().Count());
    }

    private static Resource Item(string id, string? parent = null) => new(
        id, id, "Test", "Test", "local", ResourceState.Running, [], "1", DateTimeOffset.UnixEpoch, [], ParentResourceId: parent);
}
