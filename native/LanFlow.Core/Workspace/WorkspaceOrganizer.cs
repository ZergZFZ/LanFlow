using LanFlow.Desktop.Models;

namespace LanFlow.Core.Workspace;

public enum GroupMoveStatus
{
    /// <summary>顺序已调整，结果中携带新顺序列表。</summary>
    Moved,

    /// <summary>源与目标相同，无需变化。</summary>
    NoChange,

    /// <summary>索引越界或集合为空，拒绝移动。</summary>
    InvalidIndex,
}

public sealed record GroupMoveResult(
    GroupMoveStatus Status,
    IReadOnlyList<Group> Groups,
    string? Error = null)
{
    public bool IsMoved => Status == GroupMoveStatus.Moved;
}

/// <summary>
/// 分组排序规则（P2/W1）：AppConfig.Groups 的集合顺序是唯一权威来源，不引入冗余 sortIndex。
/// 纯函数：输入现有分组序列与源/目标索引，输出新顺序副本；不修改输入集合，不做任何持久化。
/// targetIndex 采用 IList 移动语义：先移除源项，再插入到目标位置。
/// WPF/Linux UI 只负责把拖放事件换算成索引并应用返回的新顺序。
/// </summary>
public static class WorkspaceOrganizer
{
    public static GroupMoveResult MoveGroup(IReadOnlyList<Group> groups, int sourceIndex, int targetIndex)
    {
        ArgumentNullException.ThrowIfNull(groups);

        if (groups.Count == 0
            || sourceIndex < 0 || sourceIndex >= groups.Count
            || targetIndex < 0 || targetIndex >= groups.Count)
        {
            return new GroupMoveResult(
                GroupMoveStatus.InvalidIndex,
                groups,
                $"无效的分组位置（source={sourceIndex}, target={targetIndex}, count={groups.Count}），已保留原顺序。");
        }

        if (sourceIndex == targetIndex)
        {
            return new GroupMoveResult(GroupMoveStatus.NoChange, groups);
        }

        var reordered = groups.ToList();
        var moved = reordered[sourceIndex];
        reordered.RemoveAt(sourceIndex);
        reordered.Insert(targetIndex, moved);

        return new GroupMoveResult(GroupMoveStatus.Moved, reordered);
    }
}
