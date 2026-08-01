using LanFlow.Desktop.Models;

namespace LanFlow.Desktop.Services;

// 已管理项目工作区检索：只以 AppConfig 中用户已收纳的项目为输入，
// 不扫描开始菜单、用户目录或全盘文件，也不依赖 Everything 等外部服务。
// 匹配、排序规则集中在 Core，WPF 只负责输入、展示与动作转发。
public static class WorkspaceSearch
{
    // 返回带所属分组的匹配结果；空查询返回空集合。
    public static IReadOnlyList<SearchMatch> Search(AppConfig config, string? query)
    {
        var text = (query ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return [];
        }

        var matches = new List<SearchMatch>();
        foreach (var group in config.Groups)
        {
            foreach (var item in group.Items)
            {
                if (TryScore(item, text, out var score))
                {
                    matches.Add(new SearchMatch(group, item, score));
                }
            }
        }

        // 优先级：名称前缀 > 名称包含 > 路径/命令包含；同级按使用频次降序，再按名称稳定排序。
        return matches
            .OrderByDescending(match => match.Score)
            .ThenByDescending(match => match.Item.UseCount)
            .ThenBy(match => match.Item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static bool TryScore(LauncherItem item, string text, out int score)
    {
        if (item.Name.StartsWith(text, StringComparison.OrdinalIgnoreCase))
        {
            score = 4;
            return true;
        }

        if (item.Name.Contains(text, StringComparison.OrdinalIgnoreCase))
        {
            score = 3;
            return true;
        }

        if (item.Path.Contains(text, StringComparison.OrdinalIgnoreCase) ||
            item.Command.Contains(text, StringComparison.OrdinalIgnoreCase))
        {
            score = 2;
            return true;
        }

        score = 0;
        return false;
    }
}

// Score 仅用于排序，不参与记录相等性；调用方通常只使用 Group 与 Item。
public sealed record SearchMatch(Group Group, LauncherItem Item, int Score);
