using LanFlow.Desktop.Models;
using LanFlow.Desktop.Services;

namespace LanFlow.Core.Tests;

public sealed class ImportManifestServiceTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), "LanFlow.ImportTests", Guid.NewGuid().ToString("N"));

    public ImportManifestServiceTests() => Directory.CreateDirectory(_tempDirectory);

    [Fact]
    public void LoadPreview_WithValidManifest_DescribesChangesWithoutMutatingConfig()
    {
        var executable = CreateFile("工具.exe");
        var manifestPath = WriteManifest($$"""
        {
          "schemaVersion": "1.0",
          "groups": [
            {
              "name": "  开发工具  ",
              "items": [
                { "name": "  示例工具  ", "path": {{Json(executable)}} }
              ]
            }
          ]
        }
        """);
        var config = new AppConfig();
        var service = new ImportManifestService();

        var preview = service.LoadPreview(manifestPath, config);

        Assert.Equal("1.0", preview.SchemaVersion);
        var group = Assert.Single(preview.Groups);
        Assert.Equal("开发工具", group.Name);
        Assert.Equal(ImportGroupStatus.NewGroup, group.Status);
        var item = Assert.Single(group.Items);
        Assert.Equal("示例工具", item.Name);
        Assert.Equal(Path.GetFullPath(executable), item.ResolvedPath);
        Assert.Equal(item.ResolvedPath, item.DisplayPath);
        Assert.Equal(ImportItemStatus.NewItem, item.Status);
        Assert.True(item.IsSelected);
        Assert.Empty(config.Groups);
    }


    [Fact]
    public void LoadPreview_WithMalformedJson_ReportsLineAndColumnWithoutChangingConfig()
    {
        var config = new AppConfig();
        var manifestPath = WriteManifest("{\n  \"schemaVersion\": \"1.0\",\n  \"groups\": [\n}");

        var exception = Assert.Throws<ImportManifestException>(() => new ImportManifestService().LoadPreview(manifestPath, config));

        Assert.Contains("第 4 行", exception.Message);
        Assert.Contains("列", exception.Message);
        Assert.Empty(config.Groups);
    }

    [Theory]
    [MemberData(nameof(InvalidContracts))]
    public void LoadPreview_WithInvalidContract_RejectsWithSpecificMessage(string json, string expectedMessage)
    {
        var manifestPath = WriteManifest(json);

        var exception = Assert.Throws<ImportManifestException>(() => new ImportManifestService().LoadPreview(manifestPath, new AppConfig()));

        Assert.Contains(expectedMessage, exception.Message);
    }

    public static TheoryData<string, string> InvalidContracts => new()
    {
        { "{\"groups\":[{\"name\":\"组\",\"items\":[{\"name\":\"项\",\"path\":\"C:\\\\a.exe\"}]}]}", "schemaVersion" },
        { "{\"schemaVersion\":\"1.1\",\"groups\":[{\"name\":\"组\",\"items\":[{\"name\":\"项\",\"path\":\"C:\\\\a.exe\"}]}]}", "仅支持 1.0" },
        { "{\"schemaVersion\":\"1.0\",\"groups\":[],\"extra\":true}", "不支持的字段" },
        { "{\"schemaVersion\":\"1.0\",\"groups\":[]}", "至少需要一个分组" },
        { "{\"schemaVersion\":\"1.0\",\"groups\":[{\"name\":\"   \",\"items\":[{\"name\":\"项\",\"path\":\"C:\\\\a.exe\"}]}]}", "不能为空" },
        { "{\"schemaVersion\":\"1.0\",\"groups\":[{\"name\":\"组\",\"items\":[{\"name\":\"项\",\"path\":12}]}]}", "必须是字符串" },
        { "{\"schemaVersion\":\"1.0\",\"groups\":[{\"name\":\"组\",\"items\":[{\"name\":\"项\",\"path\":\"C:\\\\a.exe\",\"id\":\"x\"}]}]}", "不支持的字段" },
        { "{\"schemaVersion\":\"1.0\",\"groups\":[{\"name\":\"组\",\"items\":[]}]}", "至少需要一个项目" },
        { "{\"schemaVersion\":\"1.0\",\"schemaVersion\":\"1.0\",\"groups\":[{\"name\":\"组\",\"items\":[{\"name\":\"项\",\"path\":\"C:\\\\a.exe\"}]}]}", "重复出现" },
    };

    [Fact]
    public void LoadPreview_WithUtf8Bom_AcceptsManifest()
    {
        var executable = CreateFile("bom.exe");
        var manifestPath = Path.Combine(_tempDirectory, "bom-manifest.json");
        var json = $$"""
        {
          "schemaVersion": "1.0",
          "groups": [{ "name": "工具", "items": [{ "name": "BOM 工具", "path": {{Json(executable)}} }] }]
        }
        """;
        File.WriteAllText(manifestPath, json, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        var preview = new ImportManifestService().LoadPreview(manifestPath, new AppConfig());

        Assert.Equal(ImportItemStatus.NewItem, Assert.Single(Assert.Single(preview.Groups).Items).Status);
    }

    [Fact]
    public void LoadPreview_EnforcesNameLengthBoundariesAfterTrimming()
    {
        var executable = CreateFile("length.exe");
        var validManifest = WriteManifest($$"""
        {
          "schemaVersion": "1.0",
          "groups": [{ "name": "{{new string('组', 80)}}", "items": [{ "name": "{{new string('项', 160)}}", "path": {{Json(executable)}} }] }]
        }
        """);

        var validPreview = new ImportManifestService().LoadPreview(validManifest, new AppConfig());
        Assert.Equal(80, Assert.Single(validPreview.Groups).Name.Length);
        Assert.Equal(160, Assert.Single(Assert.Single(validPreview.Groups).Items).Name.Length);

        var longGroupManifest = WriteManifest($$"""
        { "schemaVersion": "1.0", "groups": [{ "name": "{{new string('组', 81)}}", "items": [{ "name": "项", "path": {{Json(executable)}} }] }] }
        """);
        var groupException = Assert.Throws<ImportManifestException>(() => new ImportManifestService().LoadPreview(longGroupManifest, new AppConfig()));
        Assert.Contains("不能超过 80", groupException.Message);

        var longItemManifest = WriteManifest($$"""
        { "schemaVersion": "1.0", "groups": [{ "name": "组", "items": [{ "name": "{{new string('项', 161)}}", "path": {{Json(executable)}} }] }] }
        """);
        var itemException = Assert.Throws<ImportManifestException>(() => new ImportManifestService().LoadPreview(longItemManifest, new AppConfig()));
        Assert.Contains("不能超过 160", itemException.Message);
    }


    [Fact]
    public void LoadPreview_UsesUnicodeScalarLengthLikeJsonSchema()
    {
        var executable = CreateFile("unicode-length.exe");
        var validName = string.Concat(Enumerable.Repeat("😀", 80));
        var validManifest = WriteManifest($$"""
        { "schemaVersion": "1.0", "groups": [{ "name": "{{validName}}", "items": [{ "name": "项目", "path": {{Json(executable)}} }] }] }
        """);

        var preview = new ImportManifestService().LoadPreview(validManifest, new AppConfig());
        Assert.Equal(validName, Assert.Single(preview.Groups).Name);

        var invalidName = string.Concat(Enumerable.Repeat("😀", 81));
        var invalidManifest = WriteManifest($$"""
        { "schemaVersion": "1.0", "groups": [{ "name": "{{invalidName}}", "items": [{ "name": "项目", "path": {{Json(executable)}} }] }] }
        """);

        var exception = Assert.Throws<ImportManifestException>(() => new ImportManifestService().LoadPreview(invalidManifest, new AppConfig()));
        Assert.Contains("不能超过 80", exception.Message);
    }

    [Fact]
    public void LoadPreview_WithAmbiguousExistingGroupName_RejectsInsteadOfChoosingOne()
    {
        var executable = CreateFile("ambiguous.exe");
        var manifestPath = WriteManifest($$"""
        { "schemaVersion": "1.0", "groups": [{ "name": "工具", "items": [{ "name": "项目", "path": {{Json(executable)}} }] }] }
        """);
        var config = new AppConfig
        {
            Groups =
            [
                new Group { Id = "first", Name = "工具" },
                new Group { Id = "second", Name = " 工具 " },
            ],
        };

        var exception = Assert.Throws<ImportManifestException>(() => new ImportManifestService().LoadPreview(manifestPath, config));

        Assert.Contains("多个同名分组", exception.Message);
        Assert.Equal(2, config.Groups.Count);
    }
    [Fact]
    public void LoadPreview_WithInvalidUtf8_RejectsFile()
    {
        var path = Path.Combine(_tempDirectory, "invalid.json");
        File.WriteAllBytes(path, [0xC3, 0x28]);

        var exception = Assert.Throws<ImportManifestException>(() => new ImportManifestService().LoadPreview(path, new AppConfig()));

        Assert.Contains("UTF-8", exception.Message);
    }

    [Fact]
    public void LoadPreview_WithOversizedFile_RejectsBeforeParsing()
    {
        var path = Path.Combine(_tempDirectory, "oversized.json");
        using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write))
        {
            stream.SetLength(ImportManifestService.MaxFileSizeBytes + 1);
        }

        var exception = Assert.Throws<ImportManifestException>(() => new ImportManifestService().LoadPreview(path, new AppConfig()));

        Assert.Contains("5 MB", exception.Message);
    }

    [Fact]
    public void LoadPreview_ClassifiesExistingDuplicatesInvalidPathsAndCrossGroupDuplicates()
    {
        var existingFile = CreateFile("Existing Tool.exe");
        var newFile = CreateFile("新 工具.exe");
        var directory = Directory.CreateDirectory(Path.Combine(_tempDirectory, "资料 目录")).FullName;
        var missing = Path.Combine(_tempDirectory, "missing.exe");
        var config = new AppConfig
        {
            Groups =
            [
                new Group
                {
                    Name = "tools",
                    Items = [new LauncherItem { Name = "已有", Path = existingFile }],
                },
            ],
        };
        var manifestPath = WriteManifest($$"""
        {
          "schemaVersion": "1.0",
          "groups": [
            {
              "name": " Tools ",
              "items": [
                { "name": "已有不同名", "path": {{Json(existingFile.Replace('\\', '/'))}} },
                { "name": "已有重复第二次", "path": {{Json(existingFile.ToUpperInvariant())}} },
                { "name": "新项目", "path": {{Json(newFile)}} },
                { "name": "目录", "path": {{Json(directory)}} },
                { "name": "相对", "path": "relative.exe" },
                { "name": "缺失", "path": {{Json(missing)}} }
              ]
            },
            {
              "name": "tools",
              "items": [
                { "name": "新项目重复", "path": {{Json(newFile)}} }
              ]
            },
            {
              "name": "另一个场景",
              "items": [
                { "name": "跨组重复", "path": {{Json(existingFile)}} }
              ]
            }
          ]
        }
        """);

        var preview = new ImportManifestService().LoadPreview(manifestPath, config);

        Assert.Equal(2, preview.Groups.Count);
        var tools = preview.Groups[0];
        Assert.Equal(ImportGroupStatus.MergeIntoExisting, tools.Status);
        Assert.Equal(2, tools.ManifestOccurrenceCount);
        Assert.Equal(ImportItemStatus.Existing, tools.Items[0].Status);
        Assert.Equal(ImportItemStatus.ManifestDuplicate, tools.Items[1].Status);
        Assert.Equal(ImportItemStatus.NewItem, tools.Items[2].Status);
        Assert.Equal(ImportItemStatus.NewItem, tools.Items[3].Status);
        Assert.Equal(ImportItemStatus.InvalidPath, tools.Items[4].Status);
        Assert.Equal(ImportItemStatus.InvalidPath, tools.Items[5].Status);
        Assert.Equal(ImportItemStatus.ManifestDuplicate, tools.Items[6].Status);
        Assert.Equal(ImportItemStatus.ManifestDuplicate, Assert.Single(preview.Groups[1].Items).Status);
        Assert.Equal(2, preview.SelectedItemCount);
        Assert.Equal(4, preview.DuplicateItemCount);
        Assert.Equal(2, preview.InvalidItemCount);
        Assert.Single(config.Groups);
        Assert.Single(config.Groups[0].Items);
    }

    [Fact]
    public void LoadPreview_WhenShortcutResolvesToPathInAnotherGroup_MarksItExisting()
    {
        var executable = CreateFile("shortcut-target.exe");
        var shortcut = CreateFile("shortcut-source.lnk");
        var config = new AppConfig
        {
            Groups =
            [
                new Group
                {
                    Name = "AI",
                    Items = [new LauncherItem { Name = "真实目标", Path = executable }],
                },
            ],
        };
        var manifestPath = WriteManifest($$"""
        { "schemaVersion": "1.0", "groups": [
          { "name": "桌面快捷方式（测试）", "items": [
            { "name": "桌面快捷方式", "path": {{Json(shortcut)}} }
          ] }
        ] }
        """);
        var service = new ImportManifestService(new FakeShortcutResolver(shortcut, executable));

        var preview = service.LoadPreview(manifestPath, config);

        var item = Assert.Single(Assert.Single(preview.Groups).Items);
        Assert.Equal(Path.GetFullPath(executable), item.ResolvedPath);
        Assert.Equal(ImportItemStatus.Existing, item.Status);
        Assert.False(item.CanSelect);
    }
    [Fact]
    public void LoadPreview_WhenPathAlreadyExistsInAnotherGroup_MarksItExistingAndDoesNotCreateDuplicateGroup()
    {
        var existingFile = CreateFile("global-existing.exe");
        var config = new AppConfig
        {
            Groups =
            [
                new Group
                {
                    Name = "AI",
                    Items = [new LauncherItem { Name = "已有项目", Path = existingFile }],
                },
            ],
        };
        var manifestPath = WriteManifest($$"""
        { "schemaVersion": "1.0", "groups": [
          { "name": "桌面快捷方式（测试）", "items": [
            { "name": "同一程序的桌面快捷方式", "path": {{Json(existingFile)}} }
          ] }
        ] }
        """);

        var service = new ImportManifestService();
        var preview = service.LoadPreview(manifestPath, config);
        var item = Assert.Single(Assert.Single(preview.Groups).Items);

        Assert.Equal(ImportGroupStatus.NewGroup, preview.Groups[0].Status);
        Assert.Equal(ImportItemStatus.Existing, item.Status);
        Assert.False(item.CanSelect);

        var result = service.BuildMerge(config, preview);

        Assert.Equal(0, result.ImportedItemCount);
        Assert.Equal(0, result.ImportedGroupCount);
        Assert.Equal(1, result.SkippedItemCount);
        Assert.Single(result.Config.Groups);
        Assert.Single(result.Config.Groups[0].Items);
    }

    [Fact]
    public void BuildMerge_WhenPathWasAddedToAnotherGroupAfterPreview_SkipsItGlobally()
    {
        var executable = CreateFile("global-race.exe");
        var manifestPath = WriteManifest($$"""
        { "schemaVersion": "1.0", "groups": [
          { "name": "桌面快捷方式（测试）", "items": [
            { "name": "新项目", "path": {{Json(executable)}} }
          ] }
        ] }
        """);
        var service = new ImportManifestService();
        var preview = service.LoadPreview(manifestPath, new AppConfig());
        var currentConfig = new AppConfig
        {
            Groups =
            [
                new Group
                {
                    Name = "开发工具",
                    Items = [new LauncherItem { Name = "已在别处分组", Path = executable }],
                },
            ],
        };

        var result = service.BuildMerge(currentConfig, preview);

        Assert.Equal(0, result.ImportedItemCount);
        Assert.Equal(0, result.ImportedGroupCount);
        Assert.Equal(1, result.SkippedItemCount);
        Assert.Single(result.Config.Groups);
        Assert.Single(result.Config.Groups[0].Items);
    }
    [Fact]
    public void LoadPreview_WithMissingUncPath_ClassifiesItAsInvalidPath()
    {
        const string uncPath = @"\\localhost\LanFlowImportMissing\不存在.exe";
        var manifestPath = WriteManifest($$"""
        { "schemaVersion": "1.0", "groups": [{ "name": "网络", "items": [{ "name": "UNC", "path": {{Json(uncPath)}} }] }] }
        """);

        var item = Assert.Single(Assert.Single(new ImportManifestService().LoadPreview(manifestPath, new AppConfig()).Groups).Items);

        Assert.Equal(ImportItemStatus.InvalidPath, item.Status);
        Assert.Contains("不存在", item.ErrorMessage);
        Assert.Equal(uncPath, item.ResolvedPath);
    }

    [Fact]
    public void LoadPreview_WithExistingShortcutAndNoResolver_ImportsShortcutFileItself()
    {
        var shortcut = CreateFile("普通快捷方式.lnk");
        var manifestPath = WriteManifest($$"""
        { "schemaVersion": "1.0", "groups": [{ "name": "快捷方式", "items": [{ "name": "快捷方式", "path": {{Json(shortcut)}} }] }] }
        """);

        var item = Assert.Single(Assert.Single(new ImportManifestService().LoadPreview(manifestPath, new AppConfig()).Groups).Items);

        Assert.Equal(ImportItemStatus.NewItem, item.Status);
        Assert.Equal(shortcut, item.ResolvedPath);
    }


    [Fact]
    public void LoadPreview_WithShortcut_UsesResolvedTargetPath()
    {
        var shortcut = CreateFile("Tool.lnk");
        var target = CreateFile("Tool.exe");
        var manifestPath = WriteManifest($$"""
        { "schemaVersion": "1.0", "groups": [
          { "name": "工具", "items": [{ "name": "快捷方式", "path": {{Json(shortcut)}} }] }
        ] }
        """);
        var resolver = new FakeShortcutResolver(shortcut, target);

        var preview = new ImportManifestService(resolver).LoadPreview(manifestPath, new AppConfig());

        var item = Assert.Single(Assert.Single(preview.Groups).Items);
        Assert.Equal(Path.GetFullPath(target), item.ResolvedPath);
        Assert.True(item.IsSelected);
    }

    [Fact]
    public void BuildMerge_AddsOnlySelectedItemsToACloneAndPreservesExistingData()
    {
        var existingFile = CreateFile("existing.exe");
        var mergedFile = CreateFile("merged.exe");
        var newGroupFile = CreateFile("new-group.exe");
        var deselectedFile = CreateFile("deselected.exe");
        var existingGroup = new Group
        {
            Id = "existing-group",
            Name = "工具",
            Collapsed = true,
            SortMode = "frequency",
            Items =
            [
                new LauncherItem
                {
                    Id = "existing-item",
                    Name = "已有",
                    Path = existingFile,
                    Icon = "icon",
                    Command = "command",
                    Kind = "command",
                    Hotkey = "Ctrl+1",
                    IsEnabled = false,
                    UseCount = 42,
                },
            ],
        };
        var config = new AppConfig
        {
            Groups = [existingGroup],
            Settings = new Settings { Theme = "light", Hotkey = "Ctrl+Shift+L", Opacity = 0.77 },
        };
        var manifestPath = WriteManifest($$"""
        {
          "schemaVersion": "1.0",
          "groups": [
            { "name": "工具", "items": [{ "name": "合并项", "path": {{Json(mergedFile)}} }] },
            { "name": "新分组", "items": [
              { "name": "保留项", "path": {{Json(newGroupFile)}} },
              { "name": "取消项", "path": {{Json(deselectedFile)}} }
            ] }
          ]
        }
        """);
        var service = new ImportManifestService();
        var preview = service.LoadPreview(manifestPath, config);
        preview.Groups[1].Items[1].IsSelected = false;

        var result = service.BuildMerge(config, preview);

        Assert.NotSame(config, result.Config);
        Assert.Equal(2, result.ImportedItemCount);
        Assert.Equal(1, result.ImportedGroupCount);
        Assert.Equal(1, result.SkippedItemCount);
        Assert.Single(config.Groups);
        Assert.Single(config.Groups[0].Items);
        Assert.Equal("light", result.Config.Settings.Theme);
        Assert.Equal("Ctrl+Shift+L", result.Config.Settings.Hotkey);
        var clonedExisting = result.Config.Groups[0];
        Assert.Equal("existing-group", clonedExisting.Id);
        Assert.True(clonedExisting.Collapsed);
        Assert.Equal("frequency", clonedExisting.SortMode);
        Assert.Equal(2, clonedExisting.Items.Count);
        Assert.Equal(42, clonedExisting.Items[0].UseCount);
        Assert.False(clonedExisting.Items[0].IsEnabled);
        var imported = clonedExisting.Items[1];
        Assert.Equal("合并项", imported.Name);
        Assert.Equal(mergedFile, imported.Path);
        Assert.True(imported.IsEnabled);
        Assert.Equal("app", imported.Kind);
        Assert.Equal(0, imported.UseCount);
        Assert.False(string.IsNullOrWhiteSpace(imported.Id));
        var newGroup = result.Config.Groups[1];
        Assert.Equal("新分组", newGroup.Name);
        Assert.Equal("custom", newGroup.SortMode);
        Assert.False(newGroup.Collapsed);
        Assert.Equal("保留项", Assert.Single(newGroup.Items).Name);
    }


    [Fact]
    public void BuildMerge_WhenPreviewIsSubmittedAgain_DoesNotCreateDuplicates()
    {
        var executable = CreateFile("idempotent.exe");
        var manifestPath = WriteManifest($$"""
        { "schemaVersion": "1.0", "groups": [{ "name": "工具", "items": [{ "name": "项目", "path": {{Json(executable)}} }] }] }
        """);
        var service = new ImportManifestService();
        var original = new AppConfig();
        var preview = service.LoadPreview(manifestPath, original);

        var first = service.BuildMerge(original, preview);
        var second = service.BuildMerge(first.Config, preview);

        Assert.Equal(1, first.ImportedItemCount);
        Assert.Equal(0, second.ImportedItemCount);
        Assert.Equal(0, second.ImportedGroupCount);
        Assert.Equal(1, second.SkippedItemCount);
        Assert.Single(Assert.Single(second.Config.Groups).Items);
    }

    [Fact]
    public void BuildMerge_WhenPathDisappearsAfterPreview_SkipsItAndDoesNotCreateEmptyGroup()
    {
        var executable = CreateFile("disappears.exe");
        var manifestPath = WriteManifest($$"""
        { "schemaVersion": "1.0", "groups": [{ "name": "工具", "items": [{ "name": "项目", "path": {{Json(executable)}} }] }] }
        """);
        var service = new ImportManifestService();
        var config = new AppConfig();
        var preview = service.LoadPreview(manifestPath, config);
        File.Delete(executable);

        var result = service.BuildMerge(config, preview);

        Assert.Equal(0, result.ImportedItemCount);
        Assert.Equal(0, result.ImportedGroupCount);
        Assert.Equal(1, result.SkippedItemCount);
        Assert.Empty(result.Config.Groups);
    }
    [Fact]
    public void PreviewSelection_UpdatesGroupAndSummaryAndPreventsEmptyNewGroups()
    {
        var first = CreateFile("first.exe");
        var second = CreateFile("second.exe");
        var manifestPath = WriteManifest($$"""
        { "schemaVersion": "1.0", "groups": [
          { "name": "新分组", "items": [
            { "name": "一", "path": {{Json(first)}} },
            { "name": "二", "path": {{Json(second)}} }
          ] }
        ] }
        """);
        var service = new ImportManifestService();
        var preview = service.LoadPreview(manifestPath, new AppConfig());
        var group = Assert.Single(preview.Groups);

        group.Items[0].IsSelected = false;
        Assert.Null(group.IsSelected);
        Assert.Equal(1, preview.SelectedItemCount);
        group.IsSelected = false;
        Assert.False(preview.CanConfirm);
        Assert.Equal(0, preview.SelectedNewGroupCount);

        var result = service.BuildMerge(new AppConfig(), preview);
        Assert.Empty(result.Config.Groups);
    }

    private sealed class FakeShortcutResolver(string source, string target) : IShortcutTargetResolver
    {
        public string ResolveTargetPath(string path) =>
            string.Equals(path, source, StringComparison.OrdinalIgnoreCase) ? target : path;
    }

    private string CreateFile(string name)
    {
        var path = Path.Combine(_tempDirectory, name);
        File.WriteAllText(path, string.Empty);
        return path;
    }

    private string WriteManifest(string json)
    {
        var path = Path.Combine(_tempDirectory, "import-manifest.json");
        File.WriteAllText(path, json);
        return path;
    }

    private static string Json(string value) => System.Text.Json.JsonSerializer.Serialize(value);

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, true);
        }
    }
}