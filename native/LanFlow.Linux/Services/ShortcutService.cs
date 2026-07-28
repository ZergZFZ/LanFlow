using System.IO;
using LanFlow.Desktop.Models;

namespace LanFlow.Desktop.Services;

/// <summary>
/// Linux 快捷方式（.desktop）解析：将 .desktop 解析为实际启动命令，并去除扩展名显示名。
/// </summary>
public sealed class ShortcutService
{
    public string ResolveTargetPath(string path)
    {
        if (!path.EndsWith(".desktop", StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
        {
            return path;
        }

        var (_, exec, _) = ShellIconService.ParseDesktop(path);
        return string.IsNullOrWhiteSpace(exec) ? path : exec;
    }

    public string DisplayName(string name) =>
        name.EndsWith(".desktop", StringComparison.OrdinalIgnoreCase) ? name[..^8] : name;
}
