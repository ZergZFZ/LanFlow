using System.IO;
using System.Text;

namespace LanFlow.Desktop.Services;

/// <summary>
/// Linux 开机启动：在 ~/.config/autostart/lanflow.desktop 写入/删除自启动项。
/// </summary>
public sealed class StartupService
{
    private static string AutostartDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "autostart");

    private static string DesktopFile => Path.Combine(AutostartDir, "lanflow.desktop");

    public bool IsEnabled()
    {
        if (!File.Exists(DesktopFile))
        {
            return false;
        }

        foreach (var raw in File.ReadAllLines(DesktopFile))
        {
            var line = raw.Trim();
            if (line.StartsWith("Hidden", StringComparison.OrdinalIgnoreCase))
            {
                var value = line.Split('=', 2)[^1].Trim();
                if (value.Equals("true", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            if (line.StartsWith("X-GNOME-Autostart-enabled", StringComparison.OrdinalIgnoreCase))
            {
                var value = line.Split('=', 2)[^1].Trim();
                if (value.Equals("false", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
        }

        return true;
    }

    public bool SetEnabled(bool enabled)
    {
        try
        {
            if (!enabled)
            {
                if (File.Exists(DesktopFile))
                {
                    File.Delete(DesktopFile);
                }

                return !IsEnabled();
            }

            Directory.CreateDirectory(AutostartDir);
            var executable = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executable))
            {
                return false;
            }

            var builder = new StringBuilder();
            builder.AppendLine("[Desktop Entry]");
            builder.AppendLine("Type=Application");
            builder.AppendLine("Name=LanFlow");
            builder.AppendLine("Comment=LanFlow 启动器");
            builder.AppendLine($"Exec=\"{executable}\"");
            builder.AppendLine("Terminal=false");
            builder.AppendLine("X-GNOME-Autostart-enabled=true");
            builder.AppendLine("Hidden=false");
            File.WriteAllText(DesktopFile, builder.ToString());
            return IsEnabled();
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
