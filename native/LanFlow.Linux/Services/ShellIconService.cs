using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    /// <summary>B5-1：图标缓存上限（对齐 Windows E1 LRU 256 项），超限淘汰最久未用。</summary>
    private const int CacheLimit = 256;

    // B3-6/B5-1：缓存为静态（设置窗口与主窗口可共享清空），LRU 约束容量
    private static readonly LruCache<string, IImage?> Cache = new(CacheLimit);

    public IImage? GetIcon(LauncherItem item)
    {
        var key = (item.Path ?? string.Empty) + "|" + (item.Icon ?? string.Empty);
        return Cache.GetOrAdd(key, () => Extract(item));
    }

    /// <summary>B3-6：清空图标缓存（设置页"清空缓存"按钮）。</summary>
    public static void Clear() => Cache.Clear();

    private static IImage? Extract(LauncherItem item)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(item.Icon) && File.Exists(item.Icon))
            {
                return LoadImage(item.Icon);
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

        if (Path.IsPathRooted(icon) && File.Exists(icon))
        {
            return LoadImage(icon);
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
            if (!Directory.Exists(root))
            {
                continue;
            }

            // 遍历所有主题子目录（hicolor / bloom / 其它），hicolor 优先。
            // Deepin/UOS 实际图标多在 bloom 等主题里，且多为 SVG，故不能只查 hicolor 的 png。
            foreach (var dir in Directory.EnumerateDirectories(root)
                         .OrderByDescending(d => Path.GetFileName(d).Equals("hicolor", StringComparison.OrdinalIgnoreCase)))
            {
                var hit = FindInDir(dir, icon);
                if (hit != null)
                {
                    return hit;
                }
            }

            // 根目录直接放置的图标（pixmaps、app-install 等）
            var rootHit = FindInDir(root, icon);
            if (rootHit != null)
            {
                return rootHit;
            }
        }

        return null;
    }

    private static IImage? FindInDir(string dir, string icon)
    {
        var candidates = new[]
        {
            Path.Combine(dir, "scalable", "apps", icon + ".svg"),
            Path.Combine(dir, "48x48", "apps", icon + ".png"),
            Path.Combine(dir, "64x64", "apps", icon + ".png"),
            Path.Combine(dir, "96x96", "apps", icon + ".png"),
            Path.Combine(dir, "128x128", "apps", icon + ".png"),
            Path.Combine(dir, "apps", icon + ".svg"),
            Path.Combine(dir, "apps", icon + ".png"),
            Path.Combine(dir, icon + ".png"),
            Path.Combine(dir, icon + ".svg"),
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                var image = LoadImage(candidate);
                if (image != null)
                {
                    return image;
                }

                // 加载失败（如 SVG 暂不支持）时继续找下一个候选，别提前空手而归
            }
        }

        return null;
    }

    private static IImage? LoadImage(string path)
    {
        try
        {
            if (path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
            {
                // B6-2：SVG 走轻量渲染器（自研，SkiaSharp 2.88.9，不抬依赖、不碰 glibc）。
                // 替代此前 D8 修复的整体禁用——UOS/Deepin 系统图标以 SVG 为主，禁用导致图标全灭。
                return SvgIconRenderer.Render(path, 128);
            }

            return new Bitmap(path);
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>B5-1：轻量 LRU 缓存（容量上限，命中提升使用序，超限淘汰最久未用）。GetOrAdd/Clear 线程安全。</summary>
internal sealed class LruCache<TKey, TValue> where TKey : notnull
{
    private readonly int _capacity;
    private readonly object _lock = new();
    private readonly Dictionary<TKey, LinkedListNode<(TKey Key, TValue Value)>> _map = new();
    private readonly LinkedList<(TKey Key, TValue Value)> _order = new();

    public LruCache(int capacity) => _capacity = Math.Max(1, capacity);

    public TValue GetOrAdd(TKey key, Func<TValue> factory)
    {
        lock (_lock)
        {
            if (_map.TryGetValue(key, out var existing))
            {
                _order.Remove(existing);
                _order.AddFirst(existing);
                return existing.Value.Value;
            }

            var value = factory();
            var node = _order.AddFirst((key, value));
            _map[key] = node;

            while (_map.Count > _capacity)
            {
                var last = _order.Last;
                _map.Remove(last.Value.Key);
                _order.RemoveLast();
            }

            return value;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _map.Clear();
            _order.Clear();
        }
    }
}
