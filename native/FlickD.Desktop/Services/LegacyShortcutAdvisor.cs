using System;
using System.Collections.Generic;
using System.IO;

namespace FlickD.Desktop.Services;

/// <summary>
/// 更名后的旧快捷方式提示：只读扫描任务栏与开始菜单，
/// 发现仍指向 `LanFlow.exe` 的 .lnk 时引导用户重新固定 `FlickD.exe`。
/// 纯逻辑实现，不依赖 WPF。
/// </summary>
public static class LegacyShortcutAdvisor
{
    internal const string LegacyLauncherName = "LanFlow.exe";
    internal const string NoticeFileName = "legacy-shortcut-notice.flag";

    /// <summary>提示去重标记文件：`%APPDATA%\FlickD\legacy-shortcut-notice.flag`。</summary>
    public static string MarkerPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "FlickD",
        NoticeFileName);

    /// <summary>返回需要扫描的三处快捷方式目录：任务栏固定项、用户开始菜单、公共开始菜单。</summary>
    internal static IEnumerable<string> GetShortcutDirectories()
    {
        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Microsoft",
            "Internet Explorer",
            "Quick Launch",
            "User Pinned",
            "TaskBar");
        yield return Environment.GetFolderPath(Environment.SpecialFolder.StartMenu);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu);
    }

    /// <summary>判断目标路径的文件名是否为旧主程序名（大小写不敏感）。</summary>
    internal static bool IsLegacyLauncher(string? resolvedTargetPath)
    {
        if (string.IsNullOrWhiteSpace(resolvedTargetPath))
        {
            return false;
        }

        var fileName = Path.GetFileName(resolvedTargetPath);
        return string.Equals(fileName, LegacyLauncherName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>扫描系统默认的三处快捷方式目录，返回仍指向旧主程序的 .lnk 完整路径。</summary>
    public static IReadOnlyList<string> FindLegacyShortcuts(IShortcutTargetResolver resolver) =>
        FindLegacyShortcuts(resolver, GetShortcutDirectories());

    /// <summary>扫描指定目录（含子目录），返回仍指向旧主程序的 .lnk 完整路径；任何 IO/权限/COM 异常都跳过。</summary>
    internal static IReadOnlyList<string> FindLegacyShortcuts(
        IShortcutTargetResolver resolver,
        IEnumerable<string> directories)
    {
        if (resolver is null)
        {
            return Array.Empty<string>();
        }

        var matches = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            // 快捷方式目录常含无权访问的子项，跳过而不是中断整次扫描。
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
        };

        foreach (var directory in directories)
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                continue;
            }

            try
            {
                foreach (var shortcutPath in Directory.EnumerateFiles(directory, "*.lnk", options))
                {
                    string targetPath;
                    try
                    {
                        targetPath = resolver.ResolveTargetPath(shortcutPath);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
                    {
                        // 单个快捷方式解析失败不影响其余项。
                        continue;
                    }

                    if (IsLegacyLauncher(targetPath) && seen.Add(shortcutPath))
                    {
                        matches.Add(shortcutPath);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 目录枚举中途失败时放弃该目录，继续下一处。
            }
        }

        return matches;
    }

    /// <summary>
    /// 命中旧快捷方式且尚未提示过时返回提示文案并写入标记文件；否则返回 null。
    /// 未命中时不写标记，避免用户以后才创建旧固定项时漏提示。
    /// </summary>
    public static string? TryCreateNotice(IShortcutTargetResolver resolver) =>
        TryCreateNotice(resolver, GetShortcutDirectories(), MarkerPath);

    internal static string? TryCreateNotice(
        IShortcutTargetResolver resolver,
        IEnumerable<string> directories,
        string markerPath)
    {
        try
        {
            if (File.Exists(markerPath))
            {
                return null;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        var shortcuts = FindLegacyShortcuts(resolver, directories);
        if (shortcuts.Count == 0)
        {
            return null;
        }

        var notice =
            $"检测到 {shortcuts.Count} 个仍指向 {LegacyLauncherName} 的旧快捷方式（任务栏或开始菜单）。\n\n" +
            "FlickD 2.0 已将主程序更名为 FlickD.exe，这些旧快捷方式将在 2.1.0 移除兼容壳后失效。\n\n" +
            "请在任务栏/开始菜单中取消固定，然后重新固定 FlickD。";

        try
        {
            var markerDirectory = Path.GetDirectoryName(markerPath);
            if (!string.IsNullOrWhiteSpace(markerDirectory))
            {
                Directory.CreateDirectory(markerDirectory);
            }

            File.WriteAllText(
                markerPath,
                $"{DateTimeOffset.Now:O}{Environment.NewLine}{shortcuts.Count}{Environment.NewLine}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 标记写入失败时仍返回文案：最多下次启动再提示一次。
        }

        return notice;
    }
}
