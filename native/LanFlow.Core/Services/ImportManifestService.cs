using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using LanFlow.Desktop.Models;

namespace LanFlow.Desktop.Services;

public interface IShortcutTargetResolver
{
    string ResolveTargetPath(string path);
}

public sealed class ImportManifestException : Exception
{
    public ImportManifestException(string message) : base(message) { }
    public ImportManifestException(string message, Exception innerException) : base(message, innerException) { }
}

public sealed class ImportManifestService
{
    public const long MaxFileSizeBytes = 5L * 1024 * 1024;
    public const string SupportedSchemaVersion = "1.0";

    private static readonly StringComparer NameComparer = StringComparer.OrdinalIgnoreCase;
    private static readonly StringComparer PathComparer = StringComparer.OrdinalIgnoreCase;
    private readonly IShortcutTargetResolver? _shortcutResolver;

    public ImportManifestService(IShortcutTargetResolver? shortcutResolver = null) =>
        _shortcutResolver = shortcutResolver;

    public ImportPreview LoadPreview(string manifestPath, AppConfig currentConfig)
    {
        ArgumentNullException.ThrowIfNull(currentConfig);
        var manifest = LoadManifest(manifestPath);
        return BuildPreview(manifestPath, manifest, currentConfig);
    }

    public ImportMergeResult BuildMerge(AppConfig currentConfig, ImportPreview preview)
    {
        ArgumentNullException.ThrowIfNull(currentConfig);
        ArgumentNullException.ThrowIfNull(preview);

        var merged = CloneConfig(currentConfig);
        var importedGroups = 0;
        var importedItems = 0;
        // 一个可启动目标在配置中只保留一份；导入目标分组只决定放置位置，不能绕过全局去重。
        var existingPathKeys = merged.Groups
            .SelectMany(group => group.Items)
            .Select(item => NormalizeExistingPath(item.Path))
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToHashSet(PathComparer);

        foreach (var previewGroup in preview.Groups)
        {
            var selectedItems = previewGroup.Items.Where(item => item.CanSelect && item.IsSelected).ToList();
            if (selectedItems.Count == 0) continue;

            Group? targetGroup = null;
            if (!string.IsNullOrWhiteSpace(previewGroup.ExistingGroupId))
            {
                targetGroup = merged.Groups.FirstOrDefault(group => string.Equals(group.Id, previewGroup.ExistingGroupId, StringComparison.Ordinal));
            }

            if (targetGroup is null)
            {
                var nameMatches = merged.Groups
                    .Where(group => NameComparer.Equals(group.Name.Trim(), previewGroup.Name))
                    .Take(2)
                    .ToList();
                if (nameMatches.Count > 1)
                {
                    throw new ImportManifestException($"当前配置存在多个同名分组“{previewGroup.Name}”，请先重命名以消除歧义。");
                }
                targetGroup = nameMatches.SingleOrDefault();
            }

            var createdGroup = false;
            if (targetGroup is null)
            {
                targetGroup = new Group
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Name = previewGroup.Name,
                    Collapsed = false,
                    SortMode = "custom",
                };
                merged.Groups.Add(targetGroup);
                importedGroups++;
                createdGroup = true;
            }

            foreach (var item in selectedItems)
            {
                string normalizedPath;
                try
                {
                    normalizedPath = NormalizePath(item.ResolvedPath);
                    if ((!File.Exists(normalizedPath) && !Directory.Exists(normalizedPath)) || !existingPathKeys.Add(normalizedPath))
                    {
                        continue;
                    }
                }
                catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or IOException)
                {
                    continue;
                }

                targetGroup.Items.Add(new LauncherItem
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Name = item.Name,
                    Path = normalizedPath,
                    Icon = null,
                    Command = string.Empty,
                    Kind = "app",
                    Hotkey = string.Empty,
                    IsEnabled = true,
                    UseCount = 0,
                });
                importedItems++;
            }

            if (createdGroup && targetGroup.Items.Count == 0)
            {
                merged.Groups.Remove(targetGroup);
                importedGroups--;
            }
        }

        return new ImportMergeResult
        {
            Config = merged,
            ImportedGroupCount = importedGroups,
            ImportedItemCount = importedItems,
            SkippedItemCount = preview.TotalItemCount - importedItems,
        };
    }

    private static ImportManifest LoadManifest(string manifestPath)
    {
        if (string.IsNullOrWhiteSpace(manifestPath))
        {
            throw new ImportManifestException("请选择 import-manifest.json 文件。");
        }
        if (!File.Exists(manifestPath))
        {
            throw new ImportManifestException($"导入文件不存在：{manifestPath}");
        }

        byte[] bytes;
        try
        {
            var info = new FileInfo(manifestPath);
            if (info.Length > MaxFileSizeBytes)
            {
                throw new ImportManifestException($"导入文件超过 5 MB 限制（当前 {info.Length} 字节）。");
            }
            bytes = File.ReadAllBytes(manifestPath);
        }
        catch (ImportManifestException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ImportManifestException($"无法读取导入文件：{ex.Message}", ex);
        }

        string json;
        try
        {
            json = new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException ex)
        {
            throw new ImportManifestException("导入文件必须使用有效的 UTF-8 编码。", ex);
        }

        if (json.Length > 0 && json[0] == '\uFEFF')
        {
            json = json[1..];
        }

        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
            });
            return ParseManifest(document.RootElement);
        }
        catch (ImportManifestException)
        {
            throw;
        }
        catch (JsonException ex)
        {
            var line = (ex.LineNumber ?? 0) + 1;
            var column = (ex.BytePositionInLine ?? 0) + 1;
            throw new ImportManifestException($"JSON 语法错误（第 {line} 行，第 {column} 列）：{ex.Message}", ex);
        }
    }

    private static ImportManifest ParseManifest(JsonElement root)
    {
        RequireObject(root, "根对象");
        ValidateProperties(root, ["$schema", "schemaVersion", "groups"], "根对象");

        string? schema = null;
        if (root.TryGetProperty("$schema", out var schemaElement))
        {
            schema = RequireString(schemaElement, "$schema").Trim();
            if (schema.Length == 0) throw new ImportManifestException("字段 $schema 不能为空字符串。");
        }

        if (!root.TryGetProperty("schemaVersion", out var versionElement))
        {
            throw new ImportManifestException("缺少必填字段 schemaVersion。");
        }
        var version = RequireString(versionElement, "schemaVersion");
        if (!string.Equals(version, SupportedSchemaVersion, StringComparison.Ordinal))
        {
            throw new ImportManifestException($"不支持的 schemaVersion“{version}”；当前客户端仅支持 {SupportedSchemaVersion}。");
        }

        if (!root.TryGetProperty("groups", out var groupsElement))
        {
            throw new ImportManifestException("缺少必填字段 groups。");
        }
        if (groupsElement.ValueKind != JsonValueKind.Array)
        {
            throw new ImportManifestException("字段 groups 必须是数组。");
        }
        if (groupsElement.GetArrayLength() == 0)
        {
            throw new ImportManifestException("字段 groups 至少需要一个分组。");
        }

        var groups = new List<ImportManifestGroup>();
        var groupIndex = 0;
        foreach (var groupElement in groupsElement.EnumerateArray())
        {
            groupIndex++;
            var context = $"groups[{groupIndex - 1}]";
            RequireObject(groupElement, context);
            ValidateProperties(groupElement, ["name", "items"], context);
            var name = RequireTrimmedName(groupElement, "name", context, 80);

            if (!groupElement.TryGetProperty("items", out var itemsElement))
            {
                throw new ImportManifestException($"{context} 缺少必填字段 items。");
            }
            if (itemsElement.ValueKind != JsonValueKind.Array)
            {
                throw new ImportManifestException($"{context}.items 必须是数组。");
            }
            if (itemsElement.GetArrayLength() == 0)
            {
                throw new ImportManifestException($"{context}.items 至少需要一个项目。");
            }

            var items = new List<ImportManifestItem>();
            var itemIndex = 0;
            foreach (var itemElement in itemsElement.EnumerateArray())
            {
                itemIndex++;
                var itemContext = $"{context}.items[{itemIndex - 1}]";
                RequireObject(itemElement, itemContext);
                ValidateProperties(itemElement, ["name", "path"], itemContext);
                var itemName = RequireTrimmedName(itemElement, "name", itemContext, 160);
                if (!itemElement.TryGetProperty("path", out var pathElement))
                {
                    throw new ImportManifestException($"{itemContext} 缺少必填字段 path。");
                }
                var itemPath = RequireString(pathElement, $"{itemContext}.path").Trim();
                if (itemPath.Length == 0)
                {
                    throw new ImportManifestException($"{itemContext}.path 去除首尾空白后不能为空。");
                }
                items.Add(new ImportManifestItem { Name = itemName, Path = itemPath });
            }

            groups.Add(new ImportManifestGroup { Name = name, Items = items });
        }

        return new ImportManifest { Schema = schema, SchemaVersion = version, Groups = groups };
    }

    private ImportPreview BuildPreview(string sourcePath, ImportManifest manifest, AppConfig currentConfig)
    {
        var preview = new ImportPreview
        {
            SourceFilePath = Path.GetFullPath(sourcePath),
            SchemaVersion = manifest.SchemaVersion,
        };
        var existingGroups = currentConfig.Groups
            .GroupBy(group => group.Name.Trim(), NameComparer)
            .ToDictionary(group => group.Key, group => group.ToList(), NameComparer);
        var previewGroups = new Dictionary<string, ImportGroupPreview>(NameComparer);
        var manifestPathKeys = new HashSet<string>(PathComparer);
        var existingPathKeys = currentConfig.Groups
            .SelectMany(group => group.Items)
            .Select(item => NormalizeExistingPath(item.Path))
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToHashSet(PathComparer);

        foreach (var manifestGroup in manifest.Groups)
        {
            if (!previewGroups.TryGetValue(manifestGroup.Name, out var previewGroup))
            {
                existingGroups.TryGetValue(manifestGroup.Name, out var matchingGroups);
                if (matchingGroups is { Count: > 1 })
                {
                    throw new ImportManifestException($"当前配置存在多个同名分组“{manifestGroup.Name}”，请先重命名以消除歧义。");
                }
                var existingGroup = matchingGroups?.SingleOrDefault();
                previewGroup = new ImportGroupPreview
                {
                    Name = manifestGroup.Name,
                    Status = existingGroup is null ? ImportGroupStatus.NewGroup : ImportGroupStatus.MergeIntoExisting,
                    ExistingGroupId = existingGroup?.Id,
                };
                previewGroups.Add(manifestGroup.Name, previewGroup);
                preview.AttachGroup(previewGroup);
            }
            else
            {
                previewGroup.ManifestOccurrenceCount++;
                previewGroup.NotifyOccurrenceChanged();
            }

            foreach (var manifestItem in manifestGroup.Items)
            {
                var itemPreview = CreateItemPreview(manifestItem, manifestPathKeys, existingPathKeys);
                previewGroup.AttachItem(itemPreview);
                itemPreview.IsSelected = itemPreview.CanSelect;
            }
        }

        return preview;
    }

    private ImportItemPreview CreateItemPreview(
        ImportManifestItem manifestItem,
        HashSet<string> manifestPathKeys,
        HashSet<string> existingPathKeys)
    {
        string normalizedPath;
        try
        {
            normalizedPath = NormalizePath(manifestItem.Path);
            if (normalizedPath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) && _shortcutResolver is not null)
            {
                normalizedPath = NormalizePath(_shortcutResolver.ResolveTargetPath(normalizedPath));
            }
            if (!File.Exists(normalizedPath) && !Directory.Exists(normalizedPath))
            {
                return InvalidItem(manifestItem, normalizedPath, "路径不存在或当前不可访问。");
            }
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or IOException)
        {
            return InvalidItem(manifestItem, string.Empty, $"无法规范化路径：{ex.Message}");
        }

        if (!manifestPathKeys.Add(normalizedPath))
        {
            return new ImportItemPreview
            {
                Name = manifestItem.Name,
                OriginalPath = manifestItem.Path,
                ResolvedPath = normalizedPath,
                Status = ImportItemStatus.ManifestDuplicate,
                ErrorMessage = "导入清单中已出现相同路径。",
            };
        }

        if (existingPathKeys.Contains(normalizedPath))
        {
            return new ImportItemPreview
            {
                Name = manifestItem.Name,
                OriginalPath = manifestItem.Path,
                ResolvedPath = normalizedPath,
                Status = ImportItemStatus.Existing,
                ErrorMessage = "现有配置中已存在相同路径。",
            };
        }

        return new ImportItemPreview
        {
            Name = manifestItem.Name,
            OriginalPath = manifestItem.Path,
            ResolvedPath = normalizedPath,
            Status = ImportItemStatus.NewItem,
        };
    }

    private static ImportItemPreview InvalidItem(ImportManifestItem item, string resolvedPath, string reason) => new()
    {
        Name = item.Name,
        OriginalPath = item.Path,
        ResolvedPath = resolvedPath,
        Status = ImportItemStatus.InvalidPath,
        ErrorMessage = reason,
    };

    private static string NormalizePath(string rawPath)
    {
        var trimmed = rawPath.Trim();
        if (!Path.IsPathFullyQualified(trimmed))
        {
            throw new ArgumentException("必须使用 Windows 完整路径。", nameof(rawPath));
        }
        var fullPath = Path.GetFullPath(trimmed);
        return Path.TrimEndingDirectorySeparator(fullPath);
    }

    private string NormalizeExistingPath(string rawPath)
    {
        try
        {
            var normalized = NormalizePath(rawPath);
            if (normalized.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) && _shortcutResolver is not null)
            {
                normalized = NormalizePath(_shortcutResolver.ResolveTargetPath(normalized));
            }
            return normalized;
        }
        catch
        {
            return rawPath.Trim();
        }
    }

    private static AppConfig CloneConfig(AppConfig source) => new()
    {
        Settings = source.Settings.Clone(),
        Groups = new ObservableCollection<Group>(source.Groups.Select(group => new Group
        {
            Id = group.Id,
            Name = group.Name,
            Collapsed = group.Collapsed,
            SortMode = group.SortMode,
            Items = new ObservableCollection<LauncherItem>(group.Items.Select(item => new LauncherItem
            {
                Id = item.Id,
                Name = item.Name,
                Path = item.Path,
                Icon = item.Icon,
                Command = item.Command,
                Kind = item.Kind,
                Hotkey = item.Hotkey,
                IsEnabled = item.IsEnabled,
                UseCount = item.UseCount,
                IconImage = item.IconImage,
            })),
        })),
    };

    private static void RequireObject(JsonElement element, string context)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new ImportManifestException($"{context} 必须是对象。");
        }
    }

    private static void ValidateProperties(JsonElement element, IReadOnlyCollection<string> allowed, string context)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!seen.Add(property.Name))
            {
                throw new ImportManifestException($"{context} 中字段“{property.Name}”重复出现。");
            }
            if (!allowed.Contains(property.Name, StringComparer.Ordinal))
            {
                throw new ImportManifestException($"{context} 包含不支持的字段“{property.Name}”。");
            }
        }
    }

    private static string RequireTrimmedName(JsonElement element, string propertyName, string context, int maxLength)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            throw new ImportManifestException($"{context} 缺少必填字段 {propertyName}。");
        }
        var text = RequireString(value, $"{context}.{propertyName}").Trim();
        if (text.Length == 0)
        {
            throw new ImportManifestException($"{context}.{propertyName} 去除首尾空白后不能为空。");
        }
        if (text.EnumerateRunes().Count() > maxLength)
        {
            throw new ImportManifestException($"{context}.{propertyName} 不能超过 {maxLength} 个字符。");
        }
        return text;
    }

    private static string RequireString(JsonElement element, string context)
    {
        if (element.ValueKind != JsonValueKind.String)
        {
            throw new ImportManifestException($"字段 {context} 必须是字符串。");
        }
        return element.GetString() ?? string.Empty;
    }
}