using System.Formats.Tar;
using System.IO.Compression;

if (args.Length < 2)
{
    Console.Error.WriteLine("usage: pack <srcDir> <outTarGz>");
    return 1;
}

var src = Path.GetFullPath(args[0]);
var outp = Path.GetFullPath(args[1]);

var dirs = Directory.GetDirectories(src, "*", SearchOption.AllDirectories)
    .OrderBy(d => d, StringComparer.Ordinal)
    .ToArray();
var files = Directory.GetFiles(src, "*", SearchOption.AllDirectories)
    .OrderBy(f => f, StringComparer.Ordinal)
    .ToArray();
if (files.Length == 0)
{
    Console.Error.WriteLine($"empty publish dir: {src}");
    return 2;
}

const UnixFileMode mode = (UnixFileMode)493; // 0755 rwxr-xr-x

static DateTime UtcSeconds(DateTime t) =>
    new DateTime(t.Year, t.Month, t.Day, t.Hour, t.Minute, t.Second, DateTimeKind.Utc);

Directory.CreateDirectory(Path.GetDirectoryName(outp)!);
using (var fs = File.Create(outp))
using (var gz = new GZipStream(fs, CompressionLevel.Optimal))
using (var tar = new TarWriter(gz, TarEntryFormat.Ustar, leaveOpen: false))
{
    // 目录条目（必须先写，dpkg 依赖显式目录条目创建父目录，否则报「没有那个文件或目录」）
    foreach (var d in dirs)
    {
        var rel = Path.GetRelativePath(src, d).Replace('\\', '/') + "/";
        var entry = new UstarTarEntry(TarEntryType.Directory, rel)
        {
            Mode = mode,
            ModificationTime = new DateTimeOffset(UtcSeconds(Directory.GetLastWriteTimeUtc(d))),
        };
        tar.WriteEntry(entry);
    }

    // 文件条目
    foreach (var f in files)
    {
        var rel = Path.GetRelativePath(src, f).Replace('\\', '/');
        var entry = new UstarTarEntry(TarEntryType.RegularFile, rel)
        {
            Mode = mode,
            ModificationTime = new DateTimeOffset(UtcSeconds(File.GetLastWriteTimeUtc(f))),
            DataStream = File.OpenRead(f),
        };
        tar.WriteEntry(entry);
        entry.DataStream.Dispose();
    }
}

// 回读校验
int count = 0;
using (var fs = File.OpenRead(outp))
using (var gz = new GZipStream(fs, CompressionMode.Decompress))
using (var tar = new TarReader(gz))
{
    while (tar.GetNextEntry() is { } e)
    {
        count++;
        if ((int)e.Mode != 493)
        {
            Console.Error.WriteLine($"bad mode on {e.Name}: {(int)e.Mode}");
        }
    }
}

Console.WriteLine($"PACKED {dirs.Length} dirs + {files.Length} files, verified {count} entries -> {outp}");
return 0;
