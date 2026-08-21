using LanFlow.Core.Workspace;
using LanFlow.Desktop.Models;

namespace LanFlow.Core.Tests;

public sealed class WorkspaceOrganizerTests
{
    private static Group Group(string id) => new() { Id = id, Name = "分组-" + id };

    private static List<Group> CreateGroups(params string[] ids) => ids.Select(Group).ToList();

    [Fact]
    public void MoveForward_ReordersWithoutLoss()
    {
        var groups = CreateGroups("a", "b", "c");

        var result = WorkspaceOrganizer.MoveGroup(groups, 0, 2);

        Assert.True(result.IsMoved);
        Assert.Equal(["b", "c", "a"], result.Groups.Select(g => g.Id));
    }

    [Fact]
    public void MoveBackward_ReordersWithoutLoss()
    {
        var groups = CreateGroups("a", "b", "c");

        var result = WorkspaceOrganizer.MoveGroup(groups, 2, 0);

        Assert.True(result.IsMoved);
        Assert.Equal(["c", "a", "b"], result.Groups.Select(g => g.Id));
    }

    [Fact]
    public void MoveToMiddle_UsesListMoveSemantics()
    {
        // targetIndex 为移除源项后的插入位置：[A,B,C,D] 把 B(1) 移到 3 → [A,C,D,B]
        var groups = CreateGroups("a", "b", "c", "d");

        var result = WorkspaceOrganizer.MoveGroup(groups, 1, 3);

        Assert.True(result.IsMoved);
        Assert.Equal(["a", "c", "d", "b"], result.Groups.Select(g => g.Id));
    }

    [Fact]
    public void SameIndex_ReturnsNoChangeWithSameInstance()
    {
        var groups = CreateGroups("a", "b");

        var result = WorkspaceOrganizer.MoveGroup(groups, 1, 1);

        Assert.Equal(GroupMoveStatus.NoChange, result.Status);
        Assert.Same(groups, result.Groups);
        Assert.Equal(["a", "b"], result.Groups.Select(g => g.Id));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(3, 0)]
    [InlineData(0, 3)]
    public void OutOfRangeIndices_AreRejected(int sourceIndex, int targetIndex)
    {
        var groups = CreateGroups("a", "b", "c");

        var result = WorkspaceOrganizer.MoveGroup(groups, sourceIndex, targetIndex);

        Assert.Equal(GroupMoveStatus.InvalidIndex, result.Status);
        Assert.NotNull(result.Error);
        Assert.Same(groups, result.Groups);
        Assert.Equal(["a", "b", "c"], result.Groups.Select(g => g.Id));
    }

    [Fact]
    public void EmptyCollection_IsRejected()
    {
        var groups = new List<Group>();

        var result = WorkspaceOrganizer.MoveGroup(groups, 0, 0);

        Assert.Equal(GroupMoveStatus.InvalidIndex, result.Status);
    }

    [Fact]
    public void SingleGroup_SameIndexIsNoChange()
    {
        var groups = CreateGroups("only");

        var result = WorkspaceOrganizer.MoveGroup(groups, 0, 0);

        Assert.Equal(GroupMoveStatus.NoChange, result.Status);
    }

    [Fact]
    public void MovedResult_DoesNotMutateInputCollection()
    {
        var groups = CreateGroups("a", "b", "c");

        var result = WorkspaceOrganizer.MoveGroup(groups, 0, 2);

        Assert.True(result.IsMoved);
        Assert.Equal(["a", "b", "c"], groups.Select(g => g.Id));
        Assert.NotSame(groups, result.Groups);
    }

    [Fact]
    public void MovedResult_PreservesGroupIdentityAndItems()
    {
        var group = new Group { Id = "keep", Name = "办公" };
        group.Items.Add(new LauncherItem { Id = "item-1", Name = "记事本" });
        var groups = new List<Group> { Group("x"), group, Group("y") };

        var result = WorkspaceOrganizer.MoveGroup(groups, 1, 0);

        Assert.True(result.IsMoved);
        Assert.Same(group, result.Groups[0]);
        Assert.Equal("办公", result.Groups[0].Name);
        Assert.Equal("item-1", Assert.Single(result.Groups[0].Items).Id);
    }

    [Fact]
    public void NullGroups_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => WorkspaceOrganizer.MoveGroup(null!, 0, 0));
    }
}
