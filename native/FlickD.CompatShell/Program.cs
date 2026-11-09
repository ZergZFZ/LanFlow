using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace FlickD.CompatShell;

/// <summary>
/// 2.0.0 过渡期兼容壳，产物名为 LanFlow.exe。
/// 1.6.x 更新器只认 LanFlow.exe，因此 full 包内保留同名启动器。职责仅两个：
/// 1) 启动同目录的 FlickD.exe 并透传参数；
/// 2) 在子进程存活期间保持自身存活，并在子进程提前退出时多留 6 秒，
///    使旧更新器 5 秒后的启动校验通过，避免它误判启动失败而回滚到旧版本。
/// 不承载任何业务逻辑，计划在 2.1.0 移除。
/// </summary>
internal static class Program
{
    private const string TargetExeName = "FlickD.exe";
    private const int PostExitGraceMilliseconds = 6000;
    private const uint MB_OK = 0x00000000;
    private const uint MB_ICONERROR = 0x00000010;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    private static int Main(string[] args)
    {
        var baseDirectory = AppContext.BaseDirectory;
        var target = Path.Combine(baseDirectory, TargetExeName);

        if (!File.Exists(target))
        {
            MessageBoxW(
                IntPtr.Zero,
                "未找到 FlickD.exe，发布包可能不完整。请重新下载并完整解压后运行。",
                "FlickD",
                MB_OK | MB_ICONERROR);
            return 1;
        }

        try
        {
            var startInfo = new ProcessStartInfo(target)
            {
                UseShellExecute = false,
                WorkingDirectory = baseDirectory,
            };
            foreach (var arg in args)
            {
                startInfo.ArgumentList.Add(arg);
            }

            using var child = Process.Start(startInfo);
            if (child is null)
            {
                return 1;
            }

            child.WaitForExit();
            Thread.Sleep(PostExitGraceMilliseconds);
            return child.ExitCode;
        }
        catch (Exception)
        {
            MessageBoxW(
                IntPtr.Zero,
                "启动 FlickD.exe 失败。请从发布包根目录重新运行 FlickD.exe。",
                "FlickD",
                MB_OK | MB_ICONERROR);
            return 1;
        }
    }
}
