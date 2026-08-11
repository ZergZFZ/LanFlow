using System.Diagnostics;
using System.IO;

namespace LanFlow.Desktop.Services;

/// <summary>
/// Linux 启动逻辑：可执行文件直接运行；.desktop / 目录 / URL 用 xdg-open；
/// 命令项通过 sh -c 执行。
/// </summary>
public sealed class LauncherService
{
    public void Open(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("启动路径不能为空。", nameof(path));
        }

        var lower = path.ToLowerInvariant();
        if (lower.StartsWith("http://") || lower.StartsWith("https://") || lower.StartsWith("mailto:"))
        {
            Run("xdg-open", path);
            return;
        }

        if (Directory.Exists(path))
        {
            Run("xdg-open", path);
            return;
        }

        if (path.EndsWith(".desktop", StringComparison.OrdinalIgnoreCase))
        {
            // .desktop 不能直接 xdg-open：UOS 默认把它关联到文本编辑器，会打开文本文件。
            // 必须解析 Exec 字段后实际执行；解析失败才回退 xdg-open。
            var (_, exec, _) = ShellIconService.ParseDesktop(path);
            if (!string.IsNullOrWhiteSpace(exec))
            {
                LaunchCommand(exec);
                return;
            }

            Run("xdg-open", path);
            return;
        }

        if (File.Exists(path))
        {
            try
            {
                var startInfo = new ProcessStartInfo(path)
                {
                    UseShellExecute = false,
                    WorkingDirectory = Path.GetDirectoryName(path) ?? string.Empty,
                };
                Process.Start(startInfo);
                return;
            }
            catch
            {
                // 不是可执行文件时回退到 xdg-open
            }
        }

        Run("xdg-open", path);
    }

    public void LaunchCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return;
        }

        var escaped = command.Replace("\"", "\\\"");
        var startInfo = new ProcessStartInfo("bash", $"-c \"{escaped}\"")
        {
            UseShellExecute = false,
        };
        Process.Start(startInfo);
    }

    private static void Run(string fileName, string arguments)
    {
        var startInfo = new ProcessStartInfo(fileName, arguments)
        {
            UseShellExecute = false,
        };
        Process.Start(startInfo);
    }
}
