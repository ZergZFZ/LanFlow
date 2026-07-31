# LanFlow Phase 4 Config Location and Hover Delay Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 提供配置文件位置查看、打开、完整迁移和恢复默认能力，并把分组悬停切换延迟变成 0–500ms 的可预览设置。

**Architecture:** Core 新增配置文档序列化、位置解析和迁移服务，确保 locator 与目标配置原子更新；Desktop 只负责文件夹选择、确认提示和状态展示。悬停延迟作为持久化 Settings 字段进入克隆、归一化、ViewModel 和 `GroupSwitchCoordinator`，更新延迟时取消旧计时。

**Tech Stack:** C# 12、.NET 8、System.Text.Json、System.IO、WPF OpenFolderDialog、ProcessStartInfo、xUnit。

## Global Constraints

- 默认目录 `%APPDATA%\LanFlow`。
- locator 固定为 `%APPDATA%\LanFlow\config-location.json`。
- locator 只保存 `configDirectory`。
- 使用默认目录时删除 locator。
- 迁移只处理 `config.json`，源配置保留。
- 目标已有配置必须先确认，覆盖前生成时间戳备份。
- locator 只有在目标配置写入和反序列化验证成功后才能更新。
- 迁移成功重启后生效，运行中不替换 `ConfigStore`。
- 悬停延迟默认 100ms，范围 0–500ms，步进 10ms。
- 点击切换模式禁用延迟控件但保留数值。

---

### Task 1: 提取一致的配置 JSON 序列化

**Files:**
- Create: `native/LanFlow.Core/Services/ConfigDocumentSerializer.cs`
- Modify: `native/LanFlow.Core/Services/ConfigStore.cs`
- Modify: `native/LanFlow.Core.Tests/ConfigStoreTests.cs`

**Interfaces:**
- Produces: `ConfigDocumentSerializer.Serialize(AppConfig)`, `Deserialize(Stream)`。
- Consumes: `AppConfig` 和现有 System.Text.Json 设置。

- [ ] **Step 1: 添加序列化往返测试**

```csharp
[Fact]
public void ConfigDocumentSerializer_RoundTripsUtf8IndentedDocument()
{
    var config = new AppConfig
    {
        Groups = [new Group { Id = "group-a", Name = "常用" }],
        Settings = new Settings { Hotkey = "Ctrl+Shift+L" },
    };

    byte[] bytes = ConfigDocumentSerializer.Serialize(config);
    using var stream = new MemoryStream(bytes);
    var restored = ConfigDocumentSerializer.Deserialize(stream);

    Assert.Equal("group-a", Assert.Single(restored.Groups).Id);
    Assert.Equal("Ctrl+Shift+L", restored.Settings.Hotkey);
    Assert.Equal((byte)'{', bytes[0]);
}
```

- [ ] **Step 2: 运行测试确认类型不存在**

```powershell
dotnet test native\LanFlow.Core.Tests\LanFlow.Core.Tests.csproj -c Release --filter ConfigDocumentSerializer_RoundTripsUtf8IndentedDocument
```

Expected：编译失败。

- [ ] **Step 3: 实现序列化器**

```csharp
using System.Text.Json;
using LanFlow.Desktop.Models;

namespace LanFlow.Desktop.Services;

public static class ConfigDocumentSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public static byte[] Serialize(AppConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return JsonSerializer.SerializeToUtf8Bytes(config, Options);
    }

    public static AppConfig Deserialize(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return JsonSerializer.Deserialize<AppConfig>(stream, Options) ?? new AppConfig();
    }
}
```

- [ ] **Step 4: ConfigStore 复用序列化器**

`Load()` 使用 `ConfigDocumentSerializer.Deserialize(stream)`；`Save()` 使用 `Serialize(config)` 写入现有 `.tmp` 原子替换流程。保留 `Normalize` 和异常回退行为。

- [ ] **Step 5: 运行 ConfigStore 测试**

```powershell
dotnet test native\LanFlow.Core.Tests\LanFlow.Core.Tests.csproj -c Release --filter ConfigStoreTests
```

Expected：0 failed。

### Task 2: 实现配置位置解析服务

**Files:**
- Create: `native/LanFlow.Core/Services/ConfigLocationService.cs`
- Create: `native/LanFlow.Core.Tests/ConfigLocationServiceTests.cs`

**Interfaces:**
- Produces: `ConfigLocationResolution Resolve()`, `SetCustomDirectory(string)`, `UseDefaultDirectory()`。
- Produces record: `ConfigLocationResolution(string DirectoryPath, string ConfigPath, bool IsDefault, string? Warning)`。

- [ ] **Step 1: 写无 locator、有效 locator、损坏 locator 测试**

```csharp
public sealed class ConfigLocationServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "LanFlow.ConfigLocationTests", Guid.NewGuid().ToString("N"));

    public ConfigLocationServiceTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void Resolve_UsesDefaultDirectoryWhenLocatorIsMissing()
    {
        var service = new ConfigLocationService(_root);

        var result = service.Resolve();

        Assert.True(result.IsDefault);
        Assert.Equal(Path.Combine(_root, "LanFlow", "config.json"), result.ConfigPath);
    }

    [Fact]
    public void Resolve_UsesValidCustomDirectory()
    {
        string custom = Path.Combine(_root, "custom");
        Directory.CreateDirectory(custom);
        var service = new ConfigLocationService(_root);
        service.SetCustomDirectory(custom);

        var result = service.Resolve();

        Assert.False(result.IsDefault);
        Assert.Equal(Path.Combine(custom, "config.json"), result.ConfigPath);
    }

    [Fact]
    public void Resolve_FallsBackWhenLocatorJsonIsInvalid()
    {
        var service = new ConfigLocationService(_root);
        Directory.CreateDirectory(service.DefaultDirectory);
        File.WriteAllText(service.LocatorPath, "not-json");

        var result = service.Resolve();

        Assert.True(result.IsDefault);
        Assert.NotNull(result.Warning);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
```

- [ ] **Step 2: 运行测试确认类型不存在**

```powershell
dotnet test native\LanFlow.Core.Tests\LanFlow.Core.Tests.csproj -c Release --filter ConfigLocationServiceTests
```

Expected：编译失败。

- [ ] **Step 3: 实现完整位置记录和 locator 解析**

创建 `ConfigLocationService.cs`：

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LanFlow.Desktop.Services;

public sealed record ConfigLocationResolution(
    string DirectoryPath,
    string ConfigPath,
    bool IsDefault,
    string? Warning);

public sealed class ConfigLocationService
{
    private static readonly JsonSerializerOptions LocatorJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    private sealed record LocatorDocument(
        [property: JsonPropertyName("configDirectory")] string ConfigDirectory);

    public ConfigLocationService(string? applicationDataRoot = null)
    {
        string root = applicationDataRoot
            ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        DefaultDirectory = NormalizeDirectory(Path.Combine(root, "LanFlow"));
        LocatorPath = Path.Combine(DefaultDirectory, "config-location.json");
    }

    public string DefaultDirectory { get; }
    public string LocatorPath { get; }

    public ConfigLocationResolution Resolve()
    {
        if (!File.Exists(LocatorPath))
        {
            return CreateDefault(warning: null);
        }

        try
        {
            using var stream = File.OpenRead(LocatorPath);
            LocatorDocument? document = JsonSerializer.Deserialize<LocatorDocument>(stream, LocatorJsonOptions);
            if (document is null || string.IsNullOrWhiteSpace(document.ConfigDirectory))
            {
                return CreateDefault("locator-empty");
            }

            string directory = NormalizeDirectory(document.ConfigDirectory);
            if (!Directory.Exists(directory))
            {
                return CreateDefault("locator-directory-missing");
            }

            bool isDefault = PathsEqual(directory, DefaultDirectory);
            string selected = isDefault ? DefaultDirectory : directory;
            return new ConfigLocationResolution(
                selected,
                Path.Combine(selected, "config.json"),
                isDefault,
                Warning: null);
        }
        catch (Exception ex) when (ex is JsonException
            or IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException)
        {
            return CreateDefault("locator-invalid");
        }
    }

    public void SetCustomDirectory(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new ArgumentException("Configuration directory is required.", nameof(directory));
        }

        string normalized = NormalizeDirectory(directory);
        if (!Directory.Exists(normalized))
        {
            throw new DirectoryNotFoundException(normalized);
        }

        if (PathsEqual(normalized, DefaultDirectory))
        {
            UseDefaultDirectory();
            return;
        }

        Directory.CreateDirectory(DefaultDirectory);
        string tempPath = LocatorPath + ".tmp";
        try
        {
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(
                new LocatorDocument(normalized),
                LocatorJsonOptions);
            File.WriteAllBytes(tempPath, bytes);

            using (var validationStream = File.OpenRead(tempPath))
            {
                LocatorDocument? validated = JsonSerializer.Deserialize<LocatorDocument>(
                    validationStream,
                    LocatorJsonOptions);
                if (validated is null || !PathsEqual(validated.ConfigDirectory, normalized))
                {
                    throw new InvalidDataException("Locator validation failed.");
                }
            }

            File.Move(tempPath, LocatorPath, overwrite: true);
        }
        finally
        {
            DeleteIfExists(tempPath);
        }
    }

    public void UseDefaultDirectory()
    {
        DeleteIfExists(LocatorPath + ".tmp");
        if (File.Exists(LocatorPath))
        {
            File.Delete(LocatorPath);
        }
    }

    internal static string NormalizeDirectory(string directory) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));

    internal static bool PathsEqual(string left, string right) =>
        StringComparer.OrdinalIgnoreCase.Equals(
            NormalizeDirectory(left),
            NormalizeDirectory(right));

    private ConfigLocationResolution CreateDefault(string? warning) =>
        new(
            DefaultDirectory,
            Path.Combine(DefaultDirectory, "config.json"),
            IsDefault: true,
            warning);

    private static void DeleteIfExists(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
```

`Warning` 只使用稳定英文代码 `locator-empty`、`locator-directory-missing`、`locator-invalid`；Desktop 再映射为中文提示或日志，不把 UI 文案放入 Core。

- [ ] **Step 4: 补齐 locator 原子写和回退测试**

在 `ConfigLocationServiceTests` 增加以下精确断言：

```csharp
[Fact]
public void SetCustomDirectory_WritesCamelCaseLocatorAndLeavesNoTempFile()
{
    string custom = Path.Combine(_root, "custom");
    Directory.CreateDirectory(custom);
    var service = new ConfigLocationService(_root);

    service.SetCustomDirectory(custom);

    string json = File.ReadAllText(service.LocatorPath);
    Assert.Contains("\"configDirectory\"", json);
    Assert.DoesNotContain("\"ConfigDirectory\"", json);
    Assert.False(File.Exists(service.LocatorPath + ".tmp"));
}

[Fact]
public void UseDefaultDirectory_RemovesLocator()
{
    string custom = Path.Combine(_root, "custom");
    Directory.CreateDirectory(custom);
    var service = new ConfigLocationService(_root);
    service.SetCustomDirectory(custom);

    service.UseDefaultDirectory();

    Assert.False(File.Exists(service.LocatorPath));
    Assert.True(service.Resolve().IsDefault);
}

[Fact]
public void Resolve_FallsBackWhenCustomDirectoryNoLongerExists()
{
    string custom = Path.Combine(_root, "custom");
    Directory.CreateDirectory(custom);
    var service = new ConfigLocationService(_root);
    service.SetCustomDirectory(custom);
    Directory.Delete(custom);

    ConfigLocationResolution result = service.Resolve();

    Assert.True(result.IsDefault);
    Assert.Equal("locator-directory-missing", result.Warning);
}
```
- [ ] **Step 5: 运行位置服务测试**

```powershell
dotnet test native\LanFlow.Core.Tests\LanFlow.Core.Tests.csproj -c Release --filter ConfigLocationServiceTests
```

Expected：0 failed，测试结束无 `.tmp` 残留。

### Task 3: 实现配置迁移服务

**Files:**
- Create: `native/LanFlow.Core/Services/ConfigMigrationService.cs`
- Create: `native/LanFlow.Core.Tests/ConfigMigrationServiceTests.cs`

**Interfaces:**
- Consumes: `ConfigLocationService`, `ConfigDocumentSerializer`, 当前完整 `AppConfig`。
- Produces: `ConfigMigrationResult Migrate(AppConfig config, string currentDirectory, string targetDirectory, bool overwriteExisting)`。

- [ ] **Step 1: 定义结果类型和失败测试**

```csharp
public enum ConfigMigrationStatus
{
    Success,
    NoChange,
    TargetContainsConfig,
    InvalidTarget,
    WriteFailed,
    ValidationFailed,
}

public sealed record ConfigMigrationResult(
    ConfigMigrationStatus Status,
    string TargetConfigPath,
    string? BackupPath,
    string? Error);
```

在 `ConfigMigrationServiceTests` 添加以下首批失败测试：

```csharp
[Fact]
public void Migrate_WritesValidatedConfigThenUpdatesLocator()
{
    var result = _migration.Migrate(_config, _current, _target, overwriteExisting: false);

    Assert.Equal(ConfigMigrationStatus.Success, result.Status);
    Assert.True(File.Exists(Path.Combine(_target, "config.json")));
    Assert.Equal(_target, _location.Resolve().DirectoryPath);
    Assert.True(File.Exists(Path.Combine(_current, "config.json")));
}

[Fact]
public void Migrate_DoesNotUpdateLocatorWhenTargetAlreadyContainsConfig()
{
    Directory.CreateDirectory(_target);
    File.WriteAllText(Path.Combine(_target, "config.json"), "{}");

    var result = _migration.Migrate(_config, _current, _target, overwriteExisting: false);

    Assert.Equal(ConfigMigrationStatus.TargetContainsConfig, result.Status);
    Assert.NotEqual(_target, _location.Resolve().DirectoryPath);
}
```

同时增加：

```csharp
[Fact]
public void Migrate_SameDirectoryReturnsNoChangeWithoutWriting()
{
    ConfigMigrationResult result = _migration.Migrate(
        _config,
        _current,
        _current,
        overwriteExisting: false);

    Assert.Equal(ConfigMigrationStatus.NoChange, result.Status);
    Assert.Null(result.BackupPath);
}

[Fact]
public void Migrate_TargetReplacementFailureKeepsPreviousLocator()
{
    string before = _location.Resolve().DirectoryPath;
    Directory.CreateDirectory(_target);
    Directory.CreateDirectory(Path.Combine(_target, "config.json"));

    ConfigMigrationResult result = _migration.Migrate(
        _config,
        _current,
        _target,
        overwriteExisting: true);

    Assert.Equal(ConfigMigrationStatus.WriteFailed, result.Status);
    Assert.Equal(before, _location.Resolve().DirectoryPath);
    Assert.False(File.Exists(Path.Combine(_target, "config.json.tmp")));
}
```

- [ ] **Step 2: 运行测试确认类型不存在**

```powershell
dotnet test native\LanFlow.Core.Tests\LanFlow.Core.Tests.csproj -c Release --filter ConfigMigrationServiceTests
```

Expected：编译失败。

- [ ] **Step 3: 实现完整迁移状态映射和原子顺序**

创建 `ConfigMigrationService.cs`：

```csharp
using System.Text.Json;
using LanFlow.Desktop.Models;

namespace LanFlow.Desktop.Services;

public enum ConfigMigrationStatus
{
    Success,
    NoChange,
    TargetContainsConfig,
    InvalidTarget,
    WriteFailed,
    ValidationFailed,
}

public sealed record ConfigMigrationResult(
    ConfigMigrationStatus Status,
    string TargetConfigPath,
    string? BackupPath,
    string? Error);

public sealed class ConfigMigrationService
{
    private readonly ConfigLocationService _locationService;
    private readonly Func<DateTimeOffset> _utcNow;

    public ConfigMigrationService(
        ConfigLocationService locationService,
        Func<DateTimeOffset>? utcNow = null)
    {
        _locationService = locationService ?? throw new ArgumentNullException(nameof(locationService));
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    public ConfigMigrationResult Migrate(
        AppConfig config,
        string currentDirectory,
        string targetDirectory,
        bool overwriteExisting)
    {
        ArgumentNullException.ThrowIfNull(config);

        string current;
        string target;
        try
        {
            current = ConfigLocationService.NormalizeDirectory(currentDirectory);
            target = ConfigLocationService.NormalizeDirectory(targetDirectory);
        }
        catch (Exception ex) when (ex is ArgumentException
            or NotSupportedException
            or IOException
            or UnauthorizedAccessException)
        {
            return new ConfigMigrationResult(
                ConfigMigrationStatus.InvalidTarget,
                targetDirectory ?? string.Empty,
                BackupPath: null,
                ex.Message);
        }

        string targetConfig = Path.Combine(target, "config.json");
        if (ConfigLocationService.PathsEqual(current, target))
        {
            return new ConfigMigrationResult(
                ConfigMigrationStatus.NoChange,
                targetConfig,
                BackupPath: null,
                Error: null);
        }

        string tempPath = targetConfig + ".tmp";
        string? backupPath = null;
        try
        {
            try
            {
                Directory.CreateDirectory(target);
                if (File.Exists(targetConfig) && !overwriteExisting)
                {
                    return new ConfigMigrationResult(
                        ConfigMigrationStatus.TargetContainsConfig,
                        targetConfig,
                        BackupPath: null,
                        Error: null);
                }

                File.WriteAllBytes(tempPath, ConfigDocumentSerializer.Serialize(config));
            }
            catch (Exception ex) when (ex is IOException
                or UnauthorizedAccessException
                or NotSupportedException
                or JsonException)
            {
                return new ConfigMigrationResult(
                    ConfigMigrationStatus.WriteFailed,
                    targetConfig,
                    BackupPath: null,
                    ex.Message);
            }

            try
            {
                using var validationStream = File.OpenRead(tempPath);
                _ = ConfigDocumentSerializer.Deserialize(validationStream);
            }
            catch (Exception ex) when (ex is JsonException
                or IOException
                or UnauthorizedAccessException
                or NotSupportedException)
            {
                return new ConfigMigrationResult(
                    ConfigMigrationStatus.ValidationFailed,
                    targetConfig,
                    BackupPath: null,
                    ex.Message);
            }

            try
            {
                if (File.Exists(targetConfig))
                {
                    string stamp = _utcNow().ToString("yyyyMMdd-HHmmssfff");
                    backupPath = Path.Combine(
                        target,
                        $"config.backup-{stamp}-{Guid.NewGuid():N}.json");
                    File.Copy(targetConfig, backupPath, overwrite: false);
                }

                File.Move(tempPath, targetConfig, overwrite: true);
                if (ConfigLocationService.PathsEqual(target, _locationService.DefaultDirectory))
                {
                    _locationService.UseDefaultDirectory();
                }
                else
                {
                    _locationService.SetCustomDirectory(target);
                }
            }
            catch (Exception ex) when (ex is IOException
                or UnauthorizedAccessException
                or ArgumentException
                or NotSupportedException)
            {
                return new ConfigMigrationResult(
                    ConfigMigrationStatus.WriteFailed,
                    targetConfig,
                    backupPath,
                    ex.Message);
            }

            return new ConfigMigrationResult(
                ConfigMigrationStatus.Success,
                targetConfig,
                backupPath,
                Error: null);
        }
        finally
        {
            DeleteTempFile(tempPath);
        }
    }

    public ConfigMigrationResult RestoreDefault(
        AppConfig config,
        string currentDirectory,
        bool overwriteExisting) =>
        Migrate(
            config,
            currentDirectory,
            _locationService.DefaultDirectory,
            overwriteExisting);

    private static void DeleteTempFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
```

固定错误映射：

- `Path.GetFullPath`/路径规范化异常 → `InvalidTarget`；
- 创建目录、序列化或写 `.tmp` 异常 → `WriteFailed`；
- 打开或反序列化 `.tmp` 异常 → `ValidationFailed`；
- 备份、替换目标配置或更新 locator 异常 → `WriteFailed`；
- `finally` 始终尝试清理 `.tmp`；
- locator 更新失败时，`ConfigLocationService` 的临时写不会覆盖旧 locator；目标配置和已生成备份保留，便于用户手动恢复。

- [ ] **Step 4: 补齐覆盖、默认目录和失败回滚测试**

在 `ConfigMigrationServiceTests` 增加：

```csharp
[Fact]
public void Migrate_OverwriteCreatesTimestampedBackupAndPreservesSource()
{
    Directory.CreateDirectory(_target);
    string targetConfig = Path.Combine(_target, "config.json");
    File.WriteAllText(targetConfig, "{\"settings\":{\"hotkey\":\"old\"},\"groups\":[]}");

    ConfigMigrationResult result = _migration.Migrate(
        _config,
        _current,
        _target,
        overwriteExisting: true);

    Assert.Equal(ConfigMigrationStatus.Success, result.Status);
    Assert.NotNull(result.BackupPath);
    Assert.Matches(@"config\.backup-\d{17}-[0-9a-f]{32}\.json$", result.BackupPath!);
    Assert.True(File.Exists(result.BackupPath));
    Assert.True(File.Exists(Path.Combine(_current, "config.json")));
}

[Fact]
public void RestoreDefault_DeletesLocatorOnlyAfterValidatedWrite()
{
    ConfigMigrationResult result = _migration.RestoreDefault(
        _config,
        _current,
        overwriteExisting: true);

    Assert.Equal(ConfigMigrationStatus.Success, result.Status);
    Assert.True(_location.Resolve().IsDefault);
    Assert.False(File.Exists(_location.LocatorPath));
    Assert.True(File.Exists(Path.Combine(_location.DefaultDirectory, "config.json")));
}

[Fact]
public void Migrate_InvalidTargetDoesNotChangeLocator()
{
    string before = _location.Resolve().DirectoryPath;

    ConfigMigrationResult result = _migration.Migrate(
        _config,
        _current,
        "bad\0path",
        overwriteExisting: false);

    Assert.Equal(ConfigMigrationStatus.InvalidTarget, result.Status);
    Assert.Equal(before, _location.Resolve().DirectoryPath);
}
```

保留 Step 1 的 `TargetContainsConfig` 测试，并额外断言所有成功或失败路径结束后 `targetConfig + ".tmp"` 不存在。
- [ ] **Step 5: 运行迁移测试**

```powershell
dotnet test native\LanFlow.Core.Tests\LanFlow.Core.Tests.csproj -c Release --filter "ConfigMigrationServiceTests|ConfigLocationServiceTests|ConfigStoreTests"
```

Expected：0 failed。

### Task 4: 启动时使用解析后的配置目录

**Files:**
- Modify: `native/LanFlow.Desktop/App.xaml.cs`
- Modify: `native/LanFlow.Desktop/MainWindow.xaml.cs`
- Modify: `native/LanFlow.Desktop.Tests/MainWindowArchitectureContractTests.cs`

**Interfaces:**
- Produces fields: `_configLocationService`、`_configMigrationService`、`_configStore`。
- Produces: `App.WriteDiagnosticLog(string source, string detail)`，复用现有 `crash.log` 路径和容错写入。
- Consumes: `ConfigLocationService.Resolve()`。

- [ ] **Step 1: 添加启动架构合同**

`MainWindowArchitectureContractTests` 增加：

```csharp
Assert.Contains("new ConfigLocationService()", code);
Assert.Contains(".Resolve()", code);
Assert.Contains("new ConfigStore(\"Alt+Space\", location.DirectoryPath)", code);
Assert.Contains("private readonly ConfigMigrationService _configMigrationService", code);
Assert.Contains("App.WriteDiagnosticLog(\"ConfigLocation\", location.Warning)", code);
```

- [ ] **Step 2: 公开非阻塞诊断日志入口**

在 `App.xaml.cs` 保留 `CrashLogPath` 和现有崩溃日志行为，新增：

```csharp
internal static void WriteDiagnosticLog(string source, string detail)
{
    try
    {
        string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {source}: {detail}";
        File.AppendAllText(CrashLogPath, line + Environment.NewLine + Environment.NewLine);
    }
    catch
    {
    }
}
```

把现有 `WriteCrashLog` 改为调用该入口：

```csharp
private static void WriteCrashLog(string source, Exception? ex) =>
    WriteDiagnosticLog(source, ex?.ToString() ?? "Unknown error");
```

- [ ] **Step 3: 改造 MainWindow 构造入口**

字段：

```csharp
private readonly ConfigLocationService _configLocationService;
private readonly ConfigMigrationService _configMigrationService;
private readonly ConfigStore _configStore;
```

在 `InitializeComponent()` 后、构造 `MainViewModel` 前按固定顺序执行：

```csharp
_configLocationService = new ConfigLocationService();
ConfigLocationResolution location = _configLocationService.Resolve();
if (location.Warning is not null)
{
    App.WriteDiagnosticLog("ConfigLocation", location.Warning);
}

_configStore = new ConfigStore("Alt+Space", location.DirectoryPath);
_configMigrationService = new ConfigMigrationService(_configLocationService);
_viewModel = new MainViewModel(_configStore);
```

locator 回退警告只写入 `%LOCALAPPDATA%\LanFlow\crash.log`，不弹消息框，不阻塞启动。

- [ ] **Step 4: 运行架构测试和 Core 测试**

```powershell
dotnet test native\LanFlow.Desktop.Tests\LanFlow.Desktop.Tests.csproj -c Release --filter MainWindowArchitectureContractTests
dotnet test native\LanFlow.Core.Tests\LanFlow.Core.Tests.csproj -c Release
```

Expected：0 failed。

### Task 5: 在性能与缓存页加入配置位置操作

**Files:**
- Create: `native/LanFlow.Desktop/Presentation/SettingsMaintenanceService.cs`
- Modify: `native/LanFlow.Desktop/Views/SettingsWindow.xaml`
- Modify: `native/LanFlow.Desktop/Views/SettingsWindow.xaml.cs`
- Modify: `native/LanFlow.Desktop/MainWindow.xaml.cs`
- Modify: `native/LanFlow.Desktop.Tests/SettingsWindowContractTests.cs`
- Modify: `native/LanFlow.Desktop.Tests/MainWindowArchitectureContractTests.cs`

**Interfaces:**
- Produces: `CurrentLocation`、`OpenConfigDirectory()`、`SelectConfigDirectory(Window)`、`MigrateTo(...)`、`RestoreDefault(...)`。
- Consumes: `ConfigMigrationService`、当前 `AppConfig` 提供函数、`App.WriteDiagnosticLog(...)`。

- [ ] **Step 1: 添加设置页和注入合同测试**

`SettingsWindowContractTests` 增加：

```csharp
Assert.Contains("x:Name=\"ConfigPathBox\"", xaml);
Assert.Contains("x:Name=\"OpenConfigDirectoryButton\"", xaml);
Assert.Contains("x:Name=\"ChangeConfigDirectoryButton\"", xaml);
Assert.Contains("x:Name=\"RestoreDefaultConfigDirectoryButton\"", xaml);
Assert.Contains("x:Name=\"ConfigMigrationStatusText\"", xaml);
Assert.Contains("SettingsMaintenanceService maintenanceService", codeBehind);
Assert.Contains("RunConfigMigration", codeBehind);
```

`MainWindowArchitectureContractTests` 将旧构造断言更新为：

```csharp
Assert.Contains("new SettingsMaintenanceService(", code);
Assert.Contains("() => _viewModel.Config", code);
Assert.Contains("new SettingsWindow(session, _iconService.Clear, maintenance)", code);
```

- [ ] **Step 2: 创建 Desktop 维护服务**

```csharp
public sealed class SettingsMaintenanceService
{
    private readonly ConfigLocationService _locationService;
    private readonly ConfigMigrationService _migrationService;
    private readonly Func<AppConfig> _currentConfig;

    public SettingsMaintenanceService(
        ConfigLocationService locationService,
        ConfigMigrationService migrationService,
        Func<AppConfig> currentConfig)
    {
        _locationService = locationService ?? throw new ArgumentNullException(nameof(locationService));
        _migrationService = migrationService ?? throw new ArgumentNullException(nameof(migrationService));
        _currentConfig = currentConfig ?? throw new ArgumentNullException(nameof(currentConfig));
    }

    public ConfigLocationResolution CurrentLocation => _locationService.Resolve();

    public string? SelectConfigDirectory(Window owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "\u9009\u62E9 LanFlow \u914D\u7F6E\u6587\u4EF6\u5939",
            Multiselect = false,
        };
        return dialog.ShowDialog(owner) == true ? dialog.FolderName : null;
    }

    public void OpenConfigDirectory()
    {
        string path = CurrentLocation.ConfigPath;
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"")
        {
            UseShellExecute = true,
        });
    }

    public ConfigMigrationResult MigrateTo(string target, bool overwriteExisting) =>
        _migrationService.Migrate(
            _currentConfig(),
            CurrentLocation.DirectoryPath,
            target,
            overwriteExisting);

    public ConfigMigrationResult RestoreDefault(bool overwriteExisting) =>
        _migrationService.RestoreDefault(
            _currentConfig(),
            CurrentLocation.DirectoryPath,
            overwriteExisting);
}
```

- [ ] **Step 3: 增加性能页配置位置区块**

```xml
<TextBox x:Name="ConfigPathBox"
         IsReadOnly="True"
         TextTrimming="CharacterEllipsis"
         ToolTip="{Binding Text, RelativeSource={RelativeSource Self}}" />
<StackPanel Orientation="Horizontal" Margin="0,10,0,0">
    <Button x:Name="OpenConfigDirectoryButton" Style="{StaticResource CommandButtonStyle}" Content="打开目录" Click="OpenConfigDirectory_Click" />
    <Button x:Name="ChangeConfigDirectoryButton" Margin="8,0,0,0" Style="{StaticResource PrimaryButtonStyle}" Content="更换位置" Click="ChangeConfigDirectory_Click" />
    <Button x:Name="RestoreDefaultConfigDirectoryButton" Margin="8,0,0,0" Style="{StaticResource CommandButtonStyle}" Content="恢复默认位置" Click="RestoreDefaultConfigDirectory_Click" />
</StackPanel>
<TextBlock x:Name="ConfigMigrationStatusText"
           Margin="0,8,0,0"
           Foreground="{DynamicResource SecondaryTextBrush}"
           TextWrapping="Wrap" />
```

“更换位置”使用现有 `PrimaryButtonStyle`；“打开目录”和“恢复默认位置”使用现有 `CommandButtonStyle`。

- [ ] **Step 4: 注入维护服务并初始化位置显示**

`SettingsWindow` 增加必填字段和构造参数：

```csharp
private readonly SettingsMaintenanceService _maintenanceService;

public SettingsWindow(
    SettingsPreviewSession session,
    Action? clearIconCache,
    SettingsMaintenanceService maintenanceService)
{
    _viewModel = new SettingsWindowViewModel(session ?? throw new ArgumentNullException(nameof(session)));
    _clearIconCache = clearIconCache;
    _maintenanceService = maintenanceService ?? throw new ArgumentNullException(nameof(maintenanceService));

    InitializeComponent();
    DataContext = _viewModel;
    InitializePreviewThrottles();
    LoadControls();
    ShowCategory(_viewModel.SelectedCategory.Id);
    _isLoading = false;
}
```

`LoadControls()` 末尾调用：

```csharp
RefreshConfigLocation();
```

新增：

```csharp
private void RefreshConfigLocation()
{
    ConfigLocationResolution location = _maintenanceService.CurrentLocation;
    ConfigPathBox.Text = location.ConfigPath;
    RestoreDefaultConfigDirectoryButton.IsEnabled = !location.IsDefault;
}
```

- [ ] **Step 5: 连接打开、选择、恢复默认和覆盖确认**

`SettingsWindow.xaml.cs` 增加以下固定事件流：

```csharp
private void OpenConfigDirectory_Click(object sender, RoutedEventArgs e)
{
    try
    {
        _maintenanceService.OpenConfigDirectory();
    }
    catch (Exception ex)
    {
        App.WriteDiagnosticLog("ConfigDirectoryOpen", ex.ToString());
        ConfigMigrationStatusText.Text = "\u65E0\u6CD5\u6253\u5F00\u914D\u7F6E\u76EE\u5F55\uFF0C\u8BF7\u67E5\u770B\u65E5\u5FD7\u3002";
    }
}

private void ChangeConfigDirectory_Click(object sender, RoutedEventArgs e)
{
    string? target = _maintenanceService.SelectConfigDirectory(this);
    if (target is null)
    {
        return;
    }

    RunConfigMigration(overwrite => _maintenanceService.MigrateTo(target, overwrite));
}

private void RestoreDefaultConfigDirectory_Click(object sender, RoutedEventArgs e) =>
    RunConfigMigration(_maintenanceService.RestoreDefault);
```

覆盖确认与结果映射集中在一个方法，避免“更换位置”和“恢复默认”分叉：

```csharp
private void RunConfigMigration(Func<bool, ConfigMigrationResult> operation)
{
    ConfigMigrationResult result = operation(false);
    if (result.Status == ConfigMigrationStatus.TargetContainsConfig)
    {
        MessageBoxResult decision = MessageBox.Show(
            this,
            "\u76EE\u6807\u4F4D\u7F6E\u5DF2\u6709 config.json\u3002\n\u7EE7\u7EED\u5C06\u5148\u5907\u4EFD\u65E7\u6587\u4EF6\uFF0C\u518D\u5199\u5165\u5F53\u524D\u914D\u7F6E\u3002",
            "\u786E\u8BA4\u8986\u76D6\u914D\u7F6E",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (decision != MessageBoxResult.Yes)
        {
            ConfigMigrationStatusText.Text = "\u5DF2\u53D6\u6D88\u914D\u7F6E\u8FC1\u79FB\u3002";
            return;
        }

        result = operation(true);
    }

    ShowConfigMigrationResult(result);
}

private void ShowConfigMigrationResult(ConfigMigrationResult result)
{
    ConfigMigrationStatusText.Text = result.Status switch
    {
        ConfigMigrationStatus.Success => "\u8FC1\u79FB\u6210\u529F\uFF0C\u91CD\u542F LanFlow \u540E\u751F\u6548\u3002",
        ConfigMigrationStatus.NoChange => "\u6240\u9009\u4F4D\u7F6E\u4E0E\u5F53\u524D\u4F4D\u7F6E\u76F8\u540C\u3002",
        ConfigMigrationStatus.InvalidTarget => "\u76EE\u6807\u8DEF\u5F84\u65E0\u6548\uFF0C\u914D\u7F6E\u672A\u66F4\u6539\u3002",
        ConfigMigrationStatus.ValidationFailed => "\u65B0\u914D\u7F6E\u9A8C\u8BC1\u5931\u8D25\uFF0C\u914D\u7F6E\u672A\u5207\u6362\u3002",
        ConfigMigrationStatus.WriteFailed => "\u914D\u7F6E\u5199\u5165\u5931\u8D25\uFF0C\u8BF7\u67E5\u770B\u65E5\u5FD7\u3002",
        _ => "\u914D\u7F6E\u8FC1\u79FB\u672A\u5B8C\u6210\u3002",
    };

    if (result.Error is not null)
    {
        App.WriteDiagnosticLog("ConfigMigration", result.Error);
    }

    if (result.Status == ConfigMigrationStatus.Success)
    {
        RefreshConfigLocation();
    }
}
```

- [ ] **Step 6: MainWindow 构造 SettingsMaintenanceService 并注入设置窗口**

在 `OpenSettings_Click` 创建设置窗口前固定构造：

```csharp
var maintenance = new SettingsMaintenanceService(
    _configLocationService,
    _configMigrationService,
    () => _viewModel.Config);
var settingsWindow = new SettingsWindow(session, _iconService.Clear, maintenance)
{
    Owner = this,
};
```

`MainViewModel.Config` 已是公开属性，维护服务固定通过 `() => _viewModel.Config` 获取完整配置，不增加重复状态或反射访问。

- [ ] **Step 7: 运行合同测试和构建**

```powershell
dotnet test native\LanFlow.Desktop.Tests\LanFlow.Desktop.Tests.csproj -c Release --filter "SettingsWindowContractTests|MainWindowArchitectureContractTests"
dotnet build native\LanFlow.Desktop\LanFlow.Desktop.csproj -c Release
```

Expected：0 failed，Build succeeded。

### Task 6: 增加悬停延迟配置模型

**Files:**
- Modify: `native/LanFlow.Core/Models/AppConfig.cs`
- Modify: `native/LanFlow.Core/Services/SettingsNormalizer.cs`
- Modify: `native/LanFlow.Core/Services/ConfigStore.cs`
- Modify: `native/LanFlow.Core/ViewModels/MainViewModel.cs`
- Modify: `native/LanFlow.Core.Tests/ConfigStoreTests.cs`
- Modify: `native/LanFlow.Core.Tests/SettingsCloneTests.cs`

**Interfaces:**
- Produces: `Settings.GroupHoverDelayMs`。

- [ ] **Step 1: 增加克隆和归一化失败测试**

```csharp
[Theory]
[InlineData(-1, 0)]
[InlineData(100, 100)]
[InlineData(900, 500)]
public void Load_ClampsGroupHoverDelay(int input, int expected)
{
    File.WriteAllText(Path.Combine(_tempDirectory, "config.json"),
        $$"""{ "settings": { "groupHoverDelayMs": {{input}} }, "groups": [] }""");

    Assert.Equal(expected, new ConfigStore("Alt+Space", _tempDirectory).Load().Settings.GroupHoverDelayMs);
}
```

`SettingsCloneTests` 源值设为 240，并断言 clone 相等。

- [ ] **Step 2: 增加模型字段和 Clone**

```csharp
[JsonPropertyName("groupHoverDelayMs")]
public int GroupHoverDelayMs { get; set; } = 100;
```

`Clone()` 增加：

```csharp
GroupHoverDelayMs = GroupHoverDelayMs,
```

- [ ] **Step 3: 增加统一归一化**

在 `SettingsNormalizer.ClampPreviewValues`：

```csharp
settings.GroupHoverDelayMs = Math.Clamp(settings.GroupHoverDelayMs, 0, 500);
```

`ConfigStore.Normalize` 和 `MainViewModel` 使用该统一结果，不额外定义不同范围。

- [ ] **Step 4: 运行 Core 测试**

```powershell
dotnet test native\LanFlow.Core.Tests\LanFlow.Core.Tests.csproj -c Release --filter "ConfigStoreTests|SettingsCloneTests"
```

Expected：0 failed。

### Task 7: 让 GroupSwitchCoordinator 支持运行时延迟

**Files:**
- Modify: `native/LanFlow.Desktop/Presentation/GroupSwitchCoordinator.cs`
- Modify: `native/LanFlow.Desktop.Tests/GroupSwitchCoordinatorTests.cs`

**Interfaces:**
- Produces: `void UpdateIntentDelay(TimeSpan delay)`。

- [ ] **Step 1: 增加更新延迟测试**

```csharp
[Fact]
public void UpdateIntentDelay_CancelsPendingHoverAndUsesNewDelay()
{
    var clock = new ManualTimerScheduler();
    using var coordinator = new GroupSwitchCoordinator(clock, TimeSpan.FromMilliseconds(200));
    var fired = new List<GroupSwitchRequestedEventArgs>();
    coordinator.SwitchRequested += (_, e) => fired.Add(e);

    coordinator.BeginHover(Group("A"));
    clock.AdvanceBy(TimeSpan.FromMilliseconds(100));
    coordinator.UpdateIntentDelay(TimeSpan.FromMilliseconds(50));
    clock.AdvanceBy(TimeSpan.FromMilliseconds(100));
    Assert.Empty(fired);

    coordinator.BeginHover(Group("B"));
    clock.AdvanceBy(TimeSpan.FromMilliseconds(49));
    Assert.Empty(fired);
    clock.AdvanceBy(TimeSpan.FromMilliseconds(1));
    Assert.Equal("B", Assert.Single(fired).Group.Id);
}

[Fact]
public void ZeroDelay_FiresOnTheSchedulerWithoutWaiting()
{
    var clock = new ManualTimerScheduler();
    using var coordinator = new GroupSwitchCoordinator(clock, TimeSpan.Zero);
    var fired = new List<GroupSwitchRequestedEventArgs>();
    coordinator.SwitchRequested += (_, e) => fired.Add(e);

    coordinator.BeginHover(Group("A"));
    clock.AdvanceBy(TimeSpan.Zero);

    Assert.Equal("A", Assert.Single(fired).Group.Id);
}
```

- [ ] **Step 2: 实现可更新延迟**

把字段改为：

```csharp
private TimeSpan _intentDelay;
```

新增：

```csharp
public void UpdateIntentDelay(TimeSpan delay)
{
    if (delay < TimeSpan.Zero)
    {
        throw new ArgumentOutOfRangeException(nameof(delay));
    }

    lock (_gate)
    {
        ThrowIfDisposed();
        _intentDelay = delay;
        CancelHoverCore();
        CancelDragHoverCore();
    }
}
```

现有 BeginHover/BeginDragHover 在 lock 内读取当前 `_intentDelay` 安排新任务。

- [ ] **Step 3: 运行分组协调器测试**

```powershell
dotnet test native\LanFlow.Desktop.Tests\LanFlow.Desktop.Tests.csproj -c Release --filter GroupSwitchCoordinatorTests
```

Expected：0 failed。

### Task 8: 设置页和主窗口实时预览悬停延迟

**Files:**
- Modify: `native/LanFlow.Desktop/Presentation/SettingsWindowViewModel.cs`
- Modify: `native/LanFlow.Desktop/Views/SettingsWindow.xaml`
- Modify: `native/LanFlow.Desktop/Views/SettingsWindow.xaml.cs`
- Modify: `native/LanFlow.Desktop/MainWindow.xaml.cs`
- Modify: `native/LanFlow.Desktop/Presentation/MainWindowSettingsCoordinator.cs`
- Modify: `native/LanFlow.Desktop.Tests/SettingsWindowViewModelTests.cs`
- Modify: `native/LanFlow.Desktop.Tests/SettingsWindowContractTests.cs`
- Modify: `native/LanFlow.Desktop.Tests/MainWindowArchitectureContractTests.cs`

**Interfaces:**
- Produces: `IsGroupHoverDelayEnabled`，continuous key `groupHoverDelayMs`。
- Produces: `MainWindowSettingsCoordinator` 的 `Action<Settings> applyGroupSwitchParameters`。
- Consumes: `_groupSwitchCoordinator.UpdateIntentDelay(...)`。

- [ ] **Step 1: 添加 ViewModel 测试**

```csharp
[Fact]
public void HoverDelay_IsEnabledOnlyForHoverModeAndUpdatesWorkingSettings()
{
    var vm = CreateViewModel(new Settings { GroupSwitchMode = SettingsOptionValues.GroupSwitchClick });
    Assert.False(vm.IsGroupHoverDelayEnabled);

    vm.Update(settings => settings.GroupSwitchMode = SettingsOptionValues.GroupSwitchHover);
    vm.UpdateContinuousSetting("groupHoverDelayMs", 240);

    Assert.True(vm.IsGroupHoverDelayEnabled);
    Assert.Equal(240, vm.Working.GroupHoverDelayMs);
}
```

- [ ] **Step 2: 扩展 SettingsWindowViewModel**

```csharp
public bool IsGroupHoverDelayEnabled =>
    string.Equals(Working.GroupSwitchMode, SettingsOptionValues.GroupSwitchHover, StringComparison.Ordinal);
```

`UpdateContinuousSetting` 增加：

```csharp
case "groupHoverDelayMs": settings.GroupHoverDelayMs = (int)Math.Round(value); break;
```

`NotifySettingsStateChanged()` 增加：

```csharp
OnPropertyChanged(nameof(IsGroupHoverDelayEnabled));
```

分组分类 keys 固定改为：

```csharp
["groupLayout", "groupSwitchMode", "groupHoverDelayMs", "groupLabelSize", "groupLabelFontSize", "groupNavigationWidth"]
```

- [ ] **Step 3: 增加带整数值显示的 XAML 控件**

在“切换方式”之后、“标签尺寸”之前加入：

```xml
<Grid Margin="0,12,0,0">
    <Grid.ColumnDefinitions>
        <ColumnDefinition Width="*" />
        <ColumnDefinition Width="Auto" />
    </Grid.ColumnDefinitions>
    <StackPanel>
        <TextBlock Text="悬停切换延迟" Style="{StaticResource SettingsFieldTitleStyle}" />
        <TextBlock Text="鼠标停留在分组标签上多久后切换分组。"
                   Style="{StaticResource SettingsFieldDescriptionStyle}" />
        <Slider x:Name="GroupHoverDelaySlider"
                Margin="0,8,0,0"
                Minimum="0"
                Maximum="500"
                TickFrequency="10"
                IsSnapToTickEnabled="True"
                IsEnabled="{Binding IsGroupHoverDelayEnabled}"
                Tag="groupHoverDelayMs"
                ValueChanged="ContinuousSlider_ValueChanged"
                primitives:Thumb.DragCompleted="ContinuousSlider_DragCompleted" />
    </StackPanel>
    <TextBlock Grid.Column="1"
               Margin="12,0,0,0"
               MinWidth="58"
               VerticalAlignment="Center"
               TextAlignment="Right"
               Text="{Binding ElementName=GroupHoverDelaySlider, Path=Value, StringFormat={}{0:0} ms}" />
</Grid>
```

点击切换模式下 Slider 禁用，但右侧仍显示保存的整数毫秒值。

- [ ] **Step 4: 把 delay 加入预览 throttle 和控件加载**

`InitializePreviewThrottles()` 的 settingKey 集合增加：

```csharp
"groupHoverDelayMs",
```

`LoadControls()` 在分组控件赋值段增加：

```csharp
GroupHoverDelaySlider.Value = Working.GroupHoverDelayMs;
```

现有 `ContinuousSlider_ValueChanged` 和 `ContinuousSlider_DragCompleted` 继续通过 `Tag="groupHoverDelayMs"` 复用统一预览与 flush 流程，不增加独立计时器。

- [ ] **Step 5: 主窗口启动和预览都更新 coordinator**

`MainWindowSettingsCoordinator` 增加字段：

```csharp
private readonly Action<Settings> _applyGroupSwitchParameters;
```

在构造函数的 `applyNavigationParameters` 之后增加参数并进行空值检查：

```csharp
Action<Settings> applyGroupSwitchParameters
```

`ApplyCore` 固定在 `_applyNavigationParameters(settings);` 后调用：

```csharp
_applyGroupSwitchParameters(settings);
```

`MainWindow` 当前已经先构造 `_viewModel`，再构造 `_groupSwitchCoordinator`；直接把硬编码 200ms 改为加载后的配置值：

```csharp
_groupSwitchCoordinator = new GroupSwitchCoordinator(
    new DispatcherTimerScheduler(Dispatcher),
    TimeSpan.FromMilliseconds(_viewModel.Settings.GroupHoverDelayMs));
```

构造 `MainWindowSettingsCoordinator` 时在 `ApplyNavigationSettings` 后传入：

```csharp
settings => _groupSwitchCoordinator.UpdateIntentDelay(
    TimeSpan.FromMilliseconds(settings.GroupHoverDelayMs)),
```

这样启动、预览、应用和取消恢复均经过同一 coordinator 更新入口；`UpdateIntentDelay` 会取消旧悬停与拖拽悬停计时，下一次意图使用新延迟。

- [ ] **Step 6: 增加架构合同并运行设置和分组测试**

`MainWindowArchitectureContractTests` 增加：

```csharp
Assert.Contains("TimeSpan.FromMilliseconds(_viewModel.Settings.GroupHoverDelayMs)", mainWindow);
Assert.Contains("_groupSwitchCoordinator.UpdateIntentDelay(", mainWindow);
```

运行：

```powershell
dotnet test native\LanFlow.Desktop.Tests\LanFlow.Desktop.Tests.csproj -c Release --filter "GroupSwitchCoordinatorTests|SettingsWindowViewModelTests|SettingsWindowContractTests|SettingsPreviewSessionTests|MainWindowArchitectureContractTests"
```

Expected：0 failed。

### Task 9: 全量验证和提交 Phase 4

- [ ] **Step 1: 运行 Core 全量测试**

```powershell
dotnet test native\LanFlow.Core.Tests\LanFlow.Core.Tests.csproj -c Release
```

Expected：0 failed。

- [ ] **Step 2: 运行 Desktop 全量测试**

```powershell
dotnet test native\LanFlow.Desktop.Tests\LanFlow.Desktop.Tests.csproj -c Release
```

Expected：0 failed。

- [ ] **Step 3: 构建 Desktop**

```powershell
dotnet build native\LanFlow.Desktop\LanFlow.Desktop.csproj -c Release
```

Expected：Build succeeded，0 errors。

- [ ] **Step 4: 提交 Phase 4**

```powershell
git add native/LanFlow.Core native/LanFlow.Core.Tests native/LanFlow.Desktop native/LanFlow.Desktop.Tests
git commit -m "feat: add config migration and hover delay settings"
```

完成后回到计划索引执行最终 Debug 验证和打包，不推送远端。
