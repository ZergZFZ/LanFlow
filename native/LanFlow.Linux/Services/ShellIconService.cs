using System.Collections.Concurrent;
using System.IO;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using LanFlow.Desktop.Models;

namespace LanFlow.Desktop.Services;

/// <summary>
/// Linux 图标解析：优先使用项目自定义图标；.desktop 文件解析其 Icon 字段并在
/// 系统图标主题目录中查找；无法解析时返回 null，由界面绘制文字占位。
/// </summary>
public sealed class ShellIconService
{
    private readonly ConcurrentDictionary<string, IImage?> _cache = new(StringComparer.OrdinalIgnoreCase);

    public IImage? GetIcon(LauncherItem item)
    {
        var key = (item.Path ?? string.Empty) + "|" + (item.Icon ?? string.Empty);
        return _cache.GetOrAdd(key, _ => Extract(item));
    }

    private static IImage? Extract(LauncherItem item)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(item.Icon) && File.Exists(item.Icon) && !item.Icon.EndsWith(".svg", true, null))
            {
                return new Bitmap(item.Icon);
            }

            if (string.IsNullOrWhiteSpace(item.Path))
            {
                return null;
            }

            if (item.Path.EndsWith(".desktop", StringComparison.OrdinalIgnoreCase))
            {
                var (_, _, icon) = ParseDesktop(item.Path);
                return ResolveIcon(icon);
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    public static (string Name, string Exec, string? Icon) ParseDesktop(string path)
    {
        var name = string.Empty;
        var exec = string.Empty;
        string? icon = null;

        if (!File.Exists(path))
        {
            return (name, exec, icon);
        }

        var inEntry = false;
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.StartsWith("[", StringComparison.OrdinalIgnoreCase) && line.EndsWith("]", StringComparison.OrdinalIgnoreCase))
            {
                inEntry = line.Equals("[Desktop Entry]", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inEntry || string.IsNullOrEmpty(line) || line.StartsWith("#", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var eq = line.IndexOf('=');
            if (eq < 0)
            {
                continue;
            }

            var key = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim();
            if (key.Equals("Name", StringComparison.OrdinalIgnoreCase))
            {
                name = value;
            }
            else if (key.Equals("Exec", StringComparison.OrdinalIgnoreCase))
            {
                exec = StripFieldCodes(value);
            }
            else if (key.Equals("Icon", StringComparison.OrdinalIgnoreCase))
            {
                icon = value;
            }
        }

        return (name, exec, icon);
    }

    private static string StripFieldCodes(string exec)
    {
        var parts = exec.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var filtered = parts.Where(p => !p.StartsWith("%", StringComparison.OrdinalIgnoreCase)).ToArray();
        return string.Join(' ', filtered);
    }

    private static IImage? ResolveIcon(string? icon)
    {
        if (string.IsNullOrWhiteSpace(icon))
        {
            return null;
        }

        if (Path.IsPathRooted(icon) && File.Exists(icon) && !icon.EndsWith(".svg", true, null))
        {
            return new Bitmap(icon);
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var roots = new[]
        {
            Path.Combine(home, ".local", "share", "icons"),
            "/usr/share/icons",
            "/usr/share/pixmaps",
            "/usr/share/app-install/icons",
        };

        foreach (var root in roots)
        {
            var candidates = new[]
            {
                Path.Combine(root, "hicolor", "48x48", "apps", icon + ".png"),
                Path.Combine(root, "hicolor", "64x64", "apps", icon + ".png"),
                Path.Combine(root, "hicolor", "96x96", "apps", icon + ".png"),
                Path.Combine(root, "hicolor", "128x128", "apps", icon + ".png"),
                Path.Combine(root, "apps", icon + ".png"),
                Path.Combine(root, icon + ".png"),
            };

            foreach (var candidate in candidates)
            {
                if (File.Exists(candidate) && !candidate.EndsWith(".svg", true, null))
                {
                    return new Bitmap(candidate);
                }
            }
        }

        return null;
    }
}
