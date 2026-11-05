using System.IO;

namespace LanFlow.Desktop.Diagnostics;

/// <summary>
/// 诊断/崩溃日志统一写入器：超过大小上限时把当前文件轮转为 .1 备份再从头写入，
/// 避免长期驻留的托盘进程让日志无限增长；任何写入或轮转失败都静默忽略。
/// </summary>
public static class LogWriter
{
    public const long DefaultMaxBytes = 1024 * 1024;

    public static void Append(string path, string text, long maxBytes = DefaultMaxBytes)
    {
        try
        {
            RotateIfNeeded(path, maxBytes);
            File.AppendAllText(path, text);
        }
        catch
        {
            // 日志是尽力而为的辅助信息：磁盘满、文件被占用等一律忽略，绝不影响主流程。
        }
    }

    private static void RotateIfNeeded(string path, long maxBytes)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length < maxBytes)
            {
                return;
            }

            var rotated = path + ".1";
            File.Delete(rotated);
            File.Move(path, rotated);
        }
        catch
        {
            // 轮转失败（如文件被占用）时退化为放弃旧内容，优先保证文件不再无限增长。
            try { File.Delete(path); } catch { }
        }
    }
}
