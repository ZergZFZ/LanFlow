using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

namespace FlickD.Desktop.Services.Ocr;

/// <summary>
/// 按 manifest 逐文件校验大小与 SHA-256。返回问题描述列表，空列表表示通过。
/// </summary>
public static class OcrManifestVerifier
{
    public static IReadOnlyList<string> Verify(string rootDirectory, OcrManifest? manifest)
    {
        var problems = new List<string>();
        if (manifest is null)
        {
            problems.Add("缺少 manifest.json 或内容无法解析");
            return problems;
        }

        if (manifest.Files.Count == 0)
        {
            problems.Add("manifest.json 未登记任何文件");
            return problems;
        }

        foreach (var file in manifest.Files)
        {
            if (string.IsNullOrWhiteSpace(file.Path))
            {
                problems.Add("manifest 中存在空文件路径");
                continue;
            }

            var fullPath = Path.Combine(rootDirectory, file.Path.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(fullPath))
            {
                problems.Add($"缺少文件：{file.Path}");
                continue;
            }

            var info = new FileInfo(fullPath);
            if (file.Size > 0 && info.Length != file.Size)
            {
                problems.Add($"大小不符：{file.Path}");
                continue;
            }

            var actual = ComputeSha256(fullPath);
            if (!string.Equals(actual, file.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                problems.Add($"哈希不符：{file.Path}");
            }
        }

        return problems;
    }

    public static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
