using Microsoft.Win32;
using System.IO;

namespace LanFlow.Desktop.Services;

public sealed class StartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "LanFlow";

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
        return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);
    }

    public bool SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (key is null) return false;
            if (enabled)
            {
                var executable = Environment.ProcessPath;
                if (string.IsNullOrWhiteSpace(executable)) return false;
                key.SetValue(ValueName, $"\"{executable}\" --silent");
                // 注册表按“值写入顺序”枚举 Run 项，后写的值最后启动；
                // 保存开机启动时把本项排到最前，避免每次开机 LanFlow 都最后启动。
                MoveLanFlowRunValueToFront(key);
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            return IsEnabled() == enabled;
        }
        catch (UnauthorizedAccessException) { return false; }
        catch (System.Security.SecurityException) { return false; }
        catch (IOException) { return false; }
    }

    /// <summary>
    /// 幂等地把 LanFlow 的 Run 值排到键内最前（已在最前时不写注册表）。
    /// 供应用启动时调用，防止其他程序安装后追加值导致顺序漂移。
    /// </summary>
    public bool EnsureRunValueOrdered()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            return key is not null && MoveLanFlowRunValueToFront(key);
        }
        catch (UnauthorizedAccessException) { return false; }
        catch (System.Security.SecurityException) { return false; }
        catch (IOException) { return false; }
    }

    /// <summary>
    /// 重建 Run 键值顺序：目标值移到最前，其余保持原有相对顺序。
    /// 纯逻辑，供单测覆盖。
    /// </summary>
    public static (string Name, RegistryValueKind Kind, object Data)[] ReorderToFront(
        (string Name, RegistryValueKind Kind, object Data)[] entries,
        string frontName)
    {
        var front = new List<(string Name, RegistryValueKind Kind, object Data)>();
        var rest = new List<(string Name, RegistryValueKind Kind, object Data)>();
        foreach (var entry in entries)
        {
            if (string.Equals(entry.Name, frontName, StringComparison.Ordinal))
            {
                front.Add(entry);
            }
            else
            {
                rest.Add(entry);
            }
        }

        return [.. front, .. rest];
    }

    private static bool MoveLanFlowRunValueToFront(RegistryKey key)
    {
        var names = key.GetValueNames();
        if (names.Length <= 1 || string.Equals(names[0], ValueName, StringComparison.Ordinal))
        {
            return true;
        }

        var entries = new (string Name, RegistryValueKind Kind, object Data)[names.Length];
        for (var i = 0; i < names.Length; i++)
        {
            entries[i] = (names[i], key.GetValueKind(names[i]), key.GetValue(names[i])!);
        }

        // 值枚举顺序 = 写入顺序；先清空再按新顺序写回。
        foreach (var entry in entries)
        {
            key.DeleteValue(entry.Name, throwOnMissingValue: false);
        }

        foreach (var entry in ReorderToFront(entries, ValueName))
        {
            switch (entry.Kind)
            {
                case RegistryValueKind.ExpandString:
                    key.SetValue(entry.Name, (string)entry.Data, RegistryValueKind.ExpandString);
                    break;
                case RegistryValueKind.MultiString:
                    key.SetValue(entry.Name, (string[])entry.Data);
                    break;
                case RegistryValueKind.DWord:
                    key.SetValue(entry.Name, (int)entry.Data);
                    break;
                case RegistryValueKind.QWord:
                    key.SetValue(entry.Name, (long)entry.Data);
                    break;
                default:
                    key.SetValue(entry.Name, entry.Data);
                    break;
            }
        }

        return true;
    }
}
