using System.Diagnostics;
using System.IO;

namespace LanFlow.Desktop.Services;

public sealed class LauncherService
{
    public void Open(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("启动路径不能为空。", nameof(path));
        }

        if (!File.Exists(path) && !Directory.Exists(path))
        {
            throw new FileNotFoundException("找不到要启动的文件或目录。", path);
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true,
        });
    }
}
