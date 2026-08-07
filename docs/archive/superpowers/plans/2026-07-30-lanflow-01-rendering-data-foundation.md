# LanFlow 阶段 1：渲染与数据基础 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 建立向后兼容的设置模型、稳定的可见项目集合、首帧即正确的项目几何尺寸，以及可取消、可缓存、按优先级调度的异步 Shell 图标管线。

**Architecture:** 与 UI 无关的配置迁移和集合更新保留在 `LanFlow.Core`；Shell 提取和异步队列保留在 `LanFlow.Desktop`。主窗口在本阶段仍使用现有面板，但必须去除后置尺寸修正、同步 `UpdateLayout()` 和全组同步图标加载，为下一阶段虚拟化提供稳定数据与图像服务。

**Tech Stack:** .NET 8、C# 12、WPF、XAML、xUnit、Windows Shell API、`Channel<T>`、`ReadOnlyObservableCollection<T>`。

## Global Constraints

- 保留 WPF、网格/列表/卡片和顶部/左侧分组能力；不做完整 MVVM 重写。
- 目标规模为单组 30–100 项、总计 100–500 项。
- 旧用户缺失透明模式时迁移为整窗透明并保留旧 `Opacity`；新配置默认分层透明 85%。
- `LayoutMode == "tile"` 必须迁移为 `"grid"`，保存后不再写回 `"tile"`。
- 所有透明度钳制到 0.40–1.00；标签大小 28–52 DIP，字体 11–18 DIP，左侧宽度 96–280 DIP。
- 首帧项目、图标和文字尺寸必须由绑定/样式在首次测量前确定。
- 禁止分组切换路径调用同步 `UpdateLayout()`、递归遍历所有项目视觉树或同步加载全组图标。
- 图标缓存容量固定 256，工作线程固定 2，优先级顺序为 Viewport、Buffer、Idle。
- 取消只节省资源；正确性必须通过请求版本和绑定身份校验保证。
- 每个任务先写失败测试、再最小实现、再运行该项目全量测试并提交。

---

### Task 1: 扩展设置模型并实现兼容迁移

**Files:**
- Create: `native/LanFlow.Core/Models/SettingsOptionValues.cs`
- Modify: `native/LanFlow.Core/Models/AppConfig.cs`
- Modify: `native/LanFlow.Core/Services/ConfigStore.cs`
- Modify: `native/LanFlow.Core/ViewModels/MainViewModel.cs`
- Modify: `native/LanFlow.Core.Tests/ConfigStoreTests.cs`
- Create: `native/LanFlow.Core.Tests/SettingsCloneTests.cs`

**Interfaces:**
- Consumes: existing `AppConfig`, `Settings`, `ConfigStore.Load()`, `ConfigStore.Save(AppConfig)`.
- Produces: `SettingsOptionValues` constants; new `Settings` properties `GroupSwitchMode`, `GroupLabelSize`, `GroupLabelFontSize`, `GroupNavigationWidth`, `TransparencyMode`, `LayeredOpacity`, `WholeWindowOpacity`, `AnimationMode`; normalized persisted configuration.

- [ ] **Step 1: 写迁移、默认值、钳制和克隆失败测试**

在 `ConfigStoreTests.cs` 中加入精确用例：

```csharp
[Fact]
public void Load_LegacyOpacityMigratesToWholeWindowWithoutChangingValue()
{
    File.WriteAllText(Path.Combine(_tempDirectory, "config.json"), """
        { "settings": { "opacity": 0.72, "layoutMode": "tile" }, "groups": [] }
        """);

    var settings = new ConfigStore("Alt+Space", _tempDirectory).Load().Settings;

    Assert.Equal(SettingsOptionValues.TransparencyWholeWindow, settings.TransparencyMode);
    Assert.Equal(0.72, settings.WholeWindowOpacity, 3);
    Assert.Equal(0.85, settings.LayeredOpacity, 3);
    Assert.Equal(SettingsOptionValues.GridLayout, settings.LayoutMode);
}

[Fact]
public void Load_MissingFileUsesLayeredEightyFivePercentDefaults()
{
    var settings = new ConfigStore("Alt+Space", _tempDirectory).Load().Settings;

    Assert.Equal(SettingsOptionValues.TransparencyLayered, settings.TransparencyMode);
    Assert.Equal(0.85, settings.LayeredOpacity, 3);
    Assert.Equal(0.85, settings.WholeWindowOpacity, 3);
}

[Fact]
public void Load_ClampsNewVisualSettings()
{
    File.WriteAllText(Path.Combine(_tempDirectory, "config.json"), """
        { "settings": {
            "groupLabelSize": 2,
            "groupLabelFontSize": 99,
            "groupNavigationWidth": 999,
            "layeredOpacity": 0.1,
            "wholeWindowOpacity": 2.0
        }, "groups": [] }
        """);

    var settings = new ConfigStore("Alt+Space", _tempDirectory).Load().Settings;

    Assert.Equal(28, settings.GroupLabelSize);
    Assert.Equal(18, settings.GroupLabelFontSize);
    Assert.Equal(280, settings.GroupNavigationWidth);
    Assert.Equal(0.40, settings.LayeredOpacity, 3);
    Assert.Equal(1.00, settings.WholeWindowOpacity, 3);
}
```

在 `SettingsCloneTests.cs` 创建一个设置全部新字段后逐项断言的测试：

```csharp
using LanFlow.Desktop.Models;

namespace LanFlow.Core.Tests;

public sealed class SettingsCloneTests
{
    [Fact]
    public void Clone_CopiesEveryInteractionAndTransparencySetting()
    {
        var source = new Settings
        {
            GroupSwitchMode = SettingsOptionValues.GroupSwitchHover,
            GroupLabelSize = 44,
            GroupLabelFontSize = 16,
            GroupNavigationWidth = 220,
            TransparencyMode = SettingsOptionValues.TransparencyWholeWindow,
            LayeredOpacity = 0.63,
            WholeWindowOpacity = 0.91,
            AnimationMode = SettingsOptionValues.AnimationOff,
        };

        var clone = source.Clone();

        Assert.NotSame(source, clone);
        Assert.Equal(source.GroupSwitchMode, clone.GroupSwitchMode);
        Assert.Equal(source.GroupLabelSize, clone.GroupLabelSize);
        Assert.Equal(source.GroupLabelFontSize, clone.GroupLabelFontSize);
        Assert.Equal(source.GroupNavigationWidth, clone.GroupNavigationWidth);
        Assert.Equal(source.TransparencyMode, clone.TransparencyMode);
        Assert.Equal(source.LayeredOpacity, clone.LayeredOpacity);
        Assert.Equal(source.WholeWindowOpacity, clone.WholeWindowOpacity);
        Assert.Equal(source.AnimationMode, clone.AnimationMode);
    }
}
```

- [ ] **Step 2: 运行新增测试并确认失败原因正确**

Run:

```powershell
dotnet test native\LanFlow.Core.Tests\LanFlow.Core.Tests.csproj -c Release --filter "ConfigStoreTests|SettingsCloneTests"
```

Expected: 编译失败，错误包含 `SettingsOptionValues` 或新增 `Settings` 属性不存在；不得是现有测试失败。

- [ ] **Step 3: 添加冻结的设置常量与序列化属性**

创建 `SettingsOptionValues.cs`：

```csharp
namespace LanFlow.Desktop.Models;

public static class SettingsOptionValues
{
    public const string GridLayout = "grid";
    public const string ListLayout = "list";
    public const string CardLayout = "card";
    public const string GroupTop = "top";
    public const string GroupLeft = "left";
    public const string GroupSwitchClick = "click";
    public const string GroupSwitchHover = "hover";
    public const string TransparencyLayered = "layered";
    public const string TransparencyWholeWindow = "wholeWindow";
    public const string AnimationSystem = "system";
    public const string AnimationOn = "on";
    public const string AnimationOff = "off";
}
```

向 `Settings` 添加以下属性，并把相同字段逐项加入 `Clone()` 对象初始化器：

```csharp
[JsonPropertyName("groupSwitchMode")]
public string GroupSwitchMode { get; set; } = SettingsOptionValues.GroupSwitchClick;

[JsonPropertyName("groupLabelSize")]
public double GroupLabelSize { get; set; } = 36;

[JsonPropertyName("groupLabelFontSize")]
public double GroupLabelFontSize { get; set; } = 13;

[JsonPropertyName("groupNavigationWidth")]
public double GroupNavigationWidth { get; set; } = 132;

[JsonPropertyName("transparencyMode")]
public string? TransparencyMode { get; set; }

[JsonPropertyName("layeredOpacity")]
public double LayeredOpacity { get; set; } = 0.85;

[JsonPropertyName("wholeWindowOpacity")]
public double WholeWindowOpacity { get; set; } = 0.85;

[JsonPropertyName("animationMode")]
public string AnimationMode { get; set; } = SettingsOptionValues.AnimationSystem;
```

- [ ] **Step 4: 实现新旧配置分流规范化**

将 `Load()` 的两个入口分别调用 `Normalize(config, isExistingConfig: false/true)`，并在 `ConfigStore.cs` 使用以下规则：

```csharp
private AppConfig Normalize(AppConfig config, bool isExistingConfig)
{
    config.Settings ??= new Settings();
    var settings = config.Settings;

    settings.LayoutMode = settings.LayoutMode switch
    {
        "tile" => SettingsOptionValues.GridLayout,
        SettingsOptionValues.GridLayout or SettingsOptionValues.ListLayout or SettingsOptionValues.CardLayout => settings.LayoutMode,
        _ => SettingsOptionValues.GridLayout,
    };

    settings.GroupLayout = settings.GroupLayout == SettingsOptionValues.GroupTop
        ? SettingsOptionValues.GroupTop
        : SettingsOptionValues.GroupLeft;
    settings.GroupSwitchMode = settings.GroupSwitchMode == SettingsOptionValues.GroupSwitchHover
        ? SettingsOptionValues.GroupSwitchHover
        : SettingsOptionValues.GroupSwitchClick;
    settings.AnimationMode = settings.AnimationMode is SettingsOptionValues.AnimationOn or SettingsOptionValues.AnimationOff
        ? settings.AnimationMode
        : SettingsOptionValues.AnimationSystem;

    if (string.IsNullOrWhiteSpace(settings.TransparencyMode))
    {
        settings.TransparencyMode = isExistingConfig
            ? SettingsOptionValues.TransparencyWholeWindow
            : SettingsOptionValues.TransparencyLayered;
        if (isExistingConfig) settings.WholeWindowOpacity = settings.Opacity;
    }
    else if (settings.TransparencyMode != SettingsOptionValues.TransparencyWholeWindow)
    {
        settings.TransparencyMode = SettingsOptionValues.TransparencyLayered;
    }

    settings.Opacity = Math.Clamp(settings.Opacity, 0.40, 1.00);
    settings.LayeredOpacity = Math.Clamp(settings.LayeredOpacity, 0.40, 1.00);
    settings.WholeWindowOpacity = Math.Clamp(settings.WholeWindowOpacity, 0.40, 1.00);
    settings.GroupLabelSize = Math.Clamp(settings.GroupLabelSize, 28, 52);
    settings.GroupLabelFontSize = Math.Clamp(settings.GroupLabelFontSize, 11, 18);
    settings.GroupNavigationWidth = Math.Clamp(settings.GroupNavigationWidth, 96, 280);

    settings.Opacity = settings.TransparencyMode == SettingsOptionValues.TransparencyWholeWindow
        ? settings.WholeWindowOpacity
        : settings.LayeredOpacity;

    // 保留 Normalize 现有的热键、主题、组和项目修复逻辑。
    return config;
}
```

保存前调用相同规范化逻辑，但传入 `isExistingConfig: true`，保证兼容字段 `Opacity` 与当前模式同步。

- [ ] **Step 5: 运行 Core 全量测试并提交**

Run:

```powershell
dotnet test native\LanFlow.Core.Tests\LanFlow.Core.Tests.csproj -c Release
git diff --check
git add native/LanFlow.Core/Models/SettingsOptionValues.cs native/LanFlow.Core/Models/AppConfig.cs native/LanFlow.Core/Services/ConfigStore.cs native/LanFlow.Core/ViewModels/MainViewModel.cs native/LanFlow.Core.Tests/ConfigStoreTests.cs native/LanFlow.Core.Tests/SettingsCloneTests.cs
git commit -m "feat: add compatible visual settings schema"
```

Expected: `Failed: 0`；提交成功且只包含本任务文件。

---

### Task 2: 将 VisibleItems 改为稳定的只读可观察集合

**Files:**
- Create: `native/LanFlow.Core/Collections/RangeObservableCollection.cs`
- Modify: `native/LanFlow.Core/ViewModels/MainViewModel.cs`
- Create: `native/LanFlow.Core.Tests/MainViewModelVisibleItemsTests.cs`

**Interfaces:**
- Consumes: `SelectedGroup`, `SearchText`, `Group.Items`.
- Produces: `public ReadOnlyObservableCollection<LauncherItem> VisibleItems { get; }`; `RangeObservableCollection<T>.ReplaceRange(IReadOnlyList<T>)`.

- [ ] **Step 1: 写集合身份和内容更新失败测试**

```csharp
using LanFlow.Desktop.Models;
using LanFlow.Desktop.Services;
using LanFlow.Desktop.ViewModels;

namespace LanFlow.Core.Tests;

public sealed class MainViewModelVisibleItemsTests
{
    [Fact]
    public void GroupSwitch_UpdatesContentsWithoutReplacingCollection()
    {
        var first = new Group { Name = "A", Items = [new LauncherItem { Name = "A1" }] };
        var second = new Group { Name = "B", Items = [new LauncherItem { Name = "B1" }, new LauncherItem { Name = "B2" }] };
        var viewModel = Create(first, second);
        var visible = viewModel.VisibleItems;

        viewModel.SelectedGroup = second;

        Assert.Same(visible, viewModel.VisibleItems);
        Assert.Equal(["B1", "B2"], viewModel.VisibleItems.Select(item => item.Name));
    }

    [Fact]
    public void Search_UpdatesContentsWithoutReplacingCollection()
    {
        var group = new Group { Items = [new LauncherItem { Name = "Alpha" }, new LauncherItem { Name = "Beta" }] };
        var viewModel = Create(group);
        var visible = viewModel.VisibleItems;

        viewModel.SearchText = "bet";

        Assert.Same(visible, viewModel.VisibleItems);
        Assert.Equal("Beta", Assert.Single(viewModel.VisibleItems).Name);
    }

    private static MainViewModel Create(params Group[] groups) =>
        new(new MemoryConfigStore(new AppConfig { Groups = new(groups) }));

    private sealed class MemoryConfigStore(AppConfig config) : IConfigStore
    {
        public AppConfig Load() => config;
        public void Save(AppConfig value) { }
    }
}
```

- [ ] **Step 2: 运行测试并确认旧 IEnumerable 语义导致失败**

Run: `dotnet test native\LanFlow.Core.Tests\LanFlow.Core.Tests.csproj -c Release --filter MainViewModelVisibleItemsTests`

Expected: 至少一个 `Assert.Same` 失败，或编译错误表明 `VisibleItems` 不是 `ReadOnlyObservableCollection<LauncherItem>`。

- [ ] **Step 3: 实现批量替换集合**

创建：

```csharp
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace LanFlow.Core.Collections;

public sealed class RangeObservableCollection<T> : ObservableCollection<T>
{
    public void ReplaceRange(IReadOnlyList<T> values)
    {
        Items.Clear();
        for (var index = 0; index < values.Count; index++) Items.Add(values[index]);
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
```

不要调用 `ClearItems()`/`Add()`，否则会为每一项发通知。

- [ ] **Step 4: 在 MainViewModel 中保持集合实例并原地刷新**

加入字段和构造初始化：

```csharp
private readonly RangeObservableCollection<LauncherItem> _visibleItems = [];

public MainViewModel(IConfigStore configStore)
{
    _configStore = configStore;
    Config = _configStore.Load();
    VisibleItems = new ReadOnlyObservableCollection<LauncherItem>(_visibleItems);
    SelectedGroup = Config.Groups.FirstOrDefault();
    RefreshVisibleItems();
}

public ReadOnlyObservableCollection<LauncherItem> VisibleItems { get; }

private void RefreshVisibleItems()
{
    var query = SelectedGroup?.Items.AsEnumerable() ?? [];
    if (!string.IsNullOrWhiteSpace(SearchText))
    {
        query = query.Where(item => item.Name.Contains(SearchText, StringComparison.CurrentCultureIgnoreCase));
    }
    _visibleItems.ReplaceRange(query.ToArray());
}
```

在 `SelectedGroup` 与 `SearchText` setter 中用 `RefreshVisibleItems()` 替换 `OnPropertyChanged(nameof(VisibleItems))`。凡新增、删除、导入或排序后当前列表可能变化的现有方法，也在保存前调用 `RefreshVisibleItems()`。

- [ ] **Step 5: 运行集合测试与全部 Core 测试并提交**

Run:

```powershell
dotnet test native\LanFlow.Core.Tests\LanFlow.Core.Tests.csproj -c Release
git diff --check
git add native/LanFlow.Core/Collections/RangeObservableCollection.cs native/LanFlow.Core/ViewModels/MainViewModel.cs native/LanFlow.Core.Tests/MainViewModelVisibleItemsTests.cs
git commit -m "perf: keep visible item collection stable"
```

Expected: `Failed: 0`，组切换和搜索测试均保留同一个集合引用。

---

### Task 3: 首帧尺寸绑定与切组性能标记

**Files:**
- Create: `native/LanFlow.Desktop/Converters/DoubleToUniformThicknessConverter.cs`
- Create: `native/LanFlow.Desktop/Diagnostics/UiPerformanceTrace.cs`
- Modify: `native/LanFlow.Desktop/App.xaml`
- Modify: `native/LanFlow.Desktop/MainWindow.xaml`
- Modify: `native/LanFlow.Desktop/MainWindow.xaml.cs`
- Create: `docs/performance/windows-ui-baseline.md`

**Interfaces:**
- Consumes: `MainViewModel.Settings`, stable `VisibleItems` from Task 2.
- Produces: XAML-only first-frame geometry; `UiPerformanceTrace.GroupSwitchStarted(string)`, `SelectionAcknowledged(string)`, `ContentStable(string, int)` event markers.

- [ ] **Step 1: 添加可测试的厚度转换器与性能事件契约**

创建转换器：

```csharp
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace LanFlow.Desktop.Converters;

public sealed class DoubleToUniformThicknessConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var divisor = parameter is string text && double.TryParse(text, CultureInfo.InvariantCulture, out var parsed) ? parsed : 1;
        return new Thickness(System.Convert.ToDouble(value, CultureInfo.InvariantCulture) / divisor);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
```

创建性能标记器，使用同一个 `Stopwatch` 和 `TraceSource`，输出结构化单行文本：

```csharp
namespace LanFlow.Desktop.Diagnostics;

public sealed class UiPerformanceTrace
{
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly TraceSource _source = new("LanFlow.UI");
    private readonly ConcurrentDictionary<string, long> _starts = new();

    public void GroupSwitchStarted(string groupId) => _starts[groupId] = _clock.ElapsedTicks;
    public void SelectionAcknowledged(string groupId) => Write(groupId, "selection-ack", 0);
    public void ContentStable(string groupId, int realizedContainers) => Write(groupId, "content-stable", realizedContainers);

    private void Write(string groupId, string marker, int realized)
    {
        if (!_starts.TryGetValue(groupId, out var start)) return;
        var elapsedMs = (_clock.ElapsedTicks - start) * 1000d / Stopwatch.Frequency;
        _source.TraceEvent(TraceEventType.Information, 0,
            $"group={groupId};marker={marker};elapsedMs={elapsedMs:F2};realized={realized}");
        _source.Flush();
    }
}
```

- [ ] **Step 2: 把所有项目几何尺寸移到首次测量前的 XAML 绑定**

在 `App.xaml` 注册转换器；在三个项目模板和 `ListBoxItem` 容器样式中使用 `DataContext.Settings` 的祖先绑定。核心绑定必须是：

```xml
<local:DoubleToUniformThicknessConverter x:Key="DoubleToUniformThicknessConverter" />

<Setter Property="Width" Value="{Binding DataContext.Settings.CardWidth, RelativeSource={RelativeSource AncestorType=ListBox}}" />
<Setter Property="Height" Value="{Binding DataContext.Settings.CardHeight, RelativeSource={RelativeSource AncestorType=ListBox}}" />
<Setter Property="Margin" Value="{Binding DataContext.Settings.ItemSpacing, RelativeSource={RelativeSource AncestorType=ListBox}, Converter={StaticResource DoubleToUniformThicknessConverter}, ConverterParameter=2}" />

<Image x:Name="ItemIcon"
       Width="{Binding DataContext.Settings.IconSize, RelativeSource={RelativeSource AncestorType=ListBox}}"
       Height="{Binding DataContext.Settings.IconSize, RelativeSource={RelativeSource AncestorType=ListBox}}"
       Stretch="Uniform"
       Source="{Binding IconImage}" />
<TextBlock x:Name="ItemName"
           FontSize="{Binding DataContext.Settings.TextSize, RelativeSource={RelativeSource AncestorType=ListBox}}"
           Text="{Binding Name}"
           TextTrimming="CharacterEllipsis"
           ToolTip="{Binding Name}" />
```

网格、列表和卡片模板都必须在 `Image` 创建时拥有明确宽高；占位状态与真实图标使用同一几何区域。

- [ ] **Step 3: 删除后置尺寸修正与同步布局路径**

从 `MainWindow.xaml.cs` 删除：

```csharp
ItemList.ItemContainerGenerator.StatusChanged += ...;
ItemList.UpdateLayout();
Dispatcher.BeginInvoke(DispatcherPriority.Loaded, ApplyItemMetrics);
private void ApplyItemMetrics() { ... }
private static T? FindVisualChild<T>(DependencyObject parent, string name) { ... }
```

设置变化后只更新绑定源并触发必要属性通知；不得用新名字重新引入相同视觉树遍历。

- [ ] **Step 4: 在切组路径接入三个性能标记并记录基线步骤**

点击/悬停请求入口调用 `GroupSwitchStarted(group.Id)`；更新导航选中视觉后调用 `SelectionAcknowledged(group.Id)`；布局完成且当前请求仍有效时调用 `ContentStable(group.Id, realizedCount)`。`docs/performance/windows-ui-baseline.md` 必须写明：

```markdown
# Windows UI 性能基线

- Build: Release
- OS: Windows 11
- Dataset: 500 total / 100 active group
- Cache states: cold and warm
- Record: CPU, GPU, resolution, scale, transparency mode
- Markers: selection-ack, content-stable
- Report: sample count, P50, P95, P99, maximum, realized container count

## Baseline command

dotnet run --project native\LanFlow.Desktop\LanFlow.Desktop.csproj -c Release

## Required visual checks

1. Switch grid/list/card groups ten times each.
2. Record whether any icon appears outside its configured geometry on first frame.
3. Record trace samples without calling UpdateLayout or recursive item traversal.
```

- [ ] **Step 5: 构建、人工首帧烟雾测试并提交**

Run:

```powershell
dotnet build native\LanFlow.Desktop\LanFlow.Desktop.csproj -c Release
git diff --check
git add native/LanFlow.Desktop/Converters/DoubleToUniformThicknessConverter.cs native/LanFlow.Desktop/Diagnostics/UiPerformanceTrace.cs native/LanFlow.Desktop/App.xaml native/LanFlow.Desktop/MainWindow.xaml native/LanFlow.Desktop/MainWindow.xaml.cs docs/performance/windows-ui-baseline.md
git commit -m "perf: bind item metrics before first render"
```

Expected: `Build succeeded.`；网格/列表/卡片第一次切入时均无超大图标帧；代码搜索 `ApplyItemMetrics|ItemList.UpdateLayout` 无结果。

---

### Task 4: 建立异步图标服务、缓存和过期写回保护

**Files:**
- Modify: `native/LanFlow.Core/Models/AppConfig.cs`
- Create: `native/LanFlow.Desktop/Services/IconCacheKey.cs`
- Create: `native/LanFlow.Desktop/Services/IIconExtractor.cs`
- Create: `native/LanFlow.Desktop/Services/ShellIconExtractor.cs`
- Modify: `native/LanFlow.Desktop/Services/ShellIconService.cs`
- Create: `native/LanFlow.Desktop.Tests/LanFlow.Desktop.Tests.csproj`
- Create: `native/LanFlow.Desktop.Tests/IconCacheKeyTests.cs`
- Create: `native/LanFlow.Desktop.Tests/ShellIconServiceTests.cs`
- Modify: `native/LanFlow.Desktop/MainWindow.xaml.cs`

**Interfaces:**
- Consumes: `LauncherItem.Path`, configured icon size, Task 3 fixed placeholder geometry.
- Produces: frozen `ImageSource` via `IIconService.GetIconAsync`; item-level `IconRequestVersion`; 256-entry LRU; request coalescing; priority queues.

- [ ] **Step 1: 创建 Desktop 测试项目和缓存键测试**

`LanFlow.Desktop.Tests.csproj`：

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
    <PackageReference Include="xunit" Version="2.5.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.5.3" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\LanFlow.Desktop\LanFlow.Desktop.csproj" />
  </ItemGroup>
</Project>
```

测试同路径不同尺寸、版本或主题不相等，并测试路径大小写按 Windows 规则归一：

```csharp
[Fact]
public void CacheKey_SeparatesSizeVersionAndTheme()
{
    var baseline = IconCacheKey.Create(@"C:\Apps\Tool.exe", 48, 100, "dark");
    Assert.NotEqual(baseline, IconCacheKey.Create(@"C:\Apps\Tool.exe", 64, 100, "dark"));
    Assert.NotEqual(baseline, IconCacheKey.Create(@"C:\Apps\Tool.exe", 48, 101, "dark"));
    Assert.NotEqual(baseline, IconCacheKey.Create(@"C:\Apps\Tool.exe", 48, 100, "light"));
    Assert.Equal(baseline, IconCacheKey.Create(@"c:\apps\tool.exe", 48, 100, "dark"));
}
```

- [ ] **Step 2: 写并发合并、优先级、容量、取消和失效失败测试**

用可控 `FakeIconExtractor` 记录调用次数并通过 `TaskCompletionSource<ImageSource?>` 控制完成时机。至少包含：

```csharp
[Fact]
public async Task SameKeyConcurrentRequests_AreExtractedOnce()
{
    var extractor = new FakeIconExtractor();
    await using var service = new ShellIconService(extractor, capacity: 256, workerCount: 2);

    var first = service.GetIconAsync("tool.exe", 48, IconLoadPriority.Viewport, default).AsTask();
    var second = service.GetIconAsync("tool.exe", 48, IconLoadPriority.Viewport, default).AsTask();
    extractor.CompleteNext(CreateFrozenImage());

    await Task.WhenAll(first, second);
    Assert.Equal(1, extractor.CallCount);
}

[Fact]
public async Task Invalidate_ForcesUpdatedFileToBeExtractedAgain()
{
    var extractor = new FakeIconExtractor(autoComplete: true);
    await using var service = new ShellIconService(extractor, capacity: 256, workerCount: 2);
    await service.GetIconAsync("tool.exe", 48, IconLoadPriority.Viewport, default);
    service.Invalidate("tool.exe");
    await service.GetIconAsync("tool.exe", 48, IconLoadPriority.Viewport, default);
    Assert.Equal(2, extractor.CallCount);
}
```

另加：Viewport 在尚未开始的 Idle 前执行；取消等待者得到 `OperationCanceledException` 且不会写缓存；插入 257 个唯一键后最旧键被重新提取。

- [ ] **Step 3: 运行 Desktop 测试并确认类型尚不存在**

Run: `dotnet test native\LanFlow.Desktop.Tests\LanFlow.Desktop.Tests.csproj -c Release`

Expected: 编译失败，包含 `IconCacheKey`、`IIconExtractor` 或异步构造函数不存在。

- [ ] **Step 4: 实现提取边界、缓存键和冻结图像**

固定接口：

```csharp
public readonly record struct IconCacheKey(string Identity, int PixelSize, long VersionStamp, string ThemeVariant)
{
    public static IconCacheKey Create(string path, int pixelSize, long versionStamp, string themeVariant) =>
        new(Path.GetFullPath(path).ToUpperInvariant(), pixelSize, versionStamp, themeVariant);
}

public interface IIconExtractor
{
    ValueTask<ImageSource?> ExtractAsync(string path, int pixelSize, CancellationToken cancellationToken);
}
```

`ShellIconExtractor` 将现有 `SHGetFileInfo`/图标句柄释放逻辑移入后台 `Task.Run`，按请求像素尺寸渲染；返回前执行：

```csharp
if (image is not null && image.CanFreeze && !image.IsFrozen) image.Freeze();
return image;
```

- [ ] **Step 5: 实现双工作线程三优先级异步服务**

`ShellIconService` 必须实现冻结接口：

```csharp
public enum IconLoadPriority { Viewport = 0, Buffer = 1, Idle = 2 }

public interface IIconService : IAsyncDisposable
{
    ValueTask<ImageSource?> GetIconAsync(string? path, int pixelSize, IconLoadPriority priority, CancellationToken cancellationToken);
    void Invalidate(string? path);
    void Clear();
}
```

实现约束：三个 `Channel<IconRequest>`；两个 worker 每轮按 Viewport→Buffer→Idle 尝试读取；`ConcurrentDictionary<IconCacheKey, Task<ImageSource?>>` 合并进行中请求；锁保护 `Dictionary<IconCacheKey, LinkedListNode<CacheEntry>>` 与 LRU 链；成功完成后插入并淘汰到 256；`Invalidate(path)` 删除所有同 Identity 键；`DisposeAsync` 完成队列、取消 worker 并等待退出。

- [ ] **Step 6: 为 LauncherItem 添加通知和请求版本保护**

将 `LauncherItem` 实现 `INotifyPropertyChanged`，并加入：

```csharp
private object? _iconImage;
private int _iconRequestVersion;

[JsonIgnore]
public object? IconImage
{
    get => _iconImage;
    set
    {
        if (ReferenceEquals(_iconImage, value)) return;
        _iconImage = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IconImage)));
    }
}

[JsonIgnore]
public int IconRequestVersion
{
    get => _iconRequestVersion;
    set => _iconRequestVersion = value;
}
```

不要序列化运行时图像或版本。

- [ ] **Step 7: 主窗口只请求当前可见数据并阻止旧结果写回**

删除 `LoadIcons()`、`_iconsLoaded` 和同步 `GetIcon()` 调用。阶段 1 尚未有真实视口模型，因此对 `VisibleItems` 使用 Viewport 优先级，并在组/搜索/尺寸变化时取消旧批次：

```csharp
private CancellationTokenSource? _iconBatchCts;

private async Task LoadVisibleIconsAsync()
{
    _iconBatchCts?.Cancel();
    _iconBatchCts?.Dispose();
    _iconBatchCts = new CancellationTokenSource();
    var token = _iconBatchCts.Token;
    var pixelSize = Math.Max(16, (int)Math.Ceiling(_viewModel.Settings.IconSize * VisualTreeHelper.GetDpi(this).DpiScaleX));

    foreach (var item in _viewModel.VisibleItems)
    {
        var requestVersion = ++item.IconRequestVersion;
        _ = LoadOneAsync(item, requestVersion, pixelSize, token);
    }
}

private async Task LoadOneAsync(LauncherItem item, int requestVersion, int pixelSize, CancellationToken token)
{
    try
    {
        var image = await _iconService.GetIconAsync(item.Path, pixelSize, IconLoadPriority.Viewport, token);
        if (!token.IsCancellationRequested && item.IconRequestVersion == requestVersion) item.IconImage = image;
    }
    catch (OperationCanceledException) when (token.IsCancellationRequested) { }
}
```

窗口关闭时取消批次并 `await _iconService.DisposeAsync()`。拖拽 ghost 若图标未就绪，只使用固定尺寸占位或当前 `IconImage`，不得同步提取。

- [ ] **Step 8: 运行全部验证并提交**

Run:

```powershell
dotnet test native\LanFlow.Core.Tests\LanFlow.Core.Tests.csproj -c Release
dotnet test native\LanFlow.Desktop.Tests\LanFlow.Desktop.Tests.csproj -c Release
dotnet build native\LanFlow.Desktop\LanFlow.Desktop.csproj -c Release
git diff --check
git add native/LanFlow.Core/Models/AppConfig.cs native/LanFlow.Desktop/Services/IconCacheKey.cs native/LanFlow.Desktop/Services/IIconExtractor.cs native/LanFlow.Desktop/Services/ShellIconExtractor.cs native/LanFlow.Desktop/Services/ShellIconService.cs native/LanFlow.Desktop.Tests/LanFlow.Desktop.Tests.csproj native/LanFlow.Desktop.Tests/IconCacheKeyTests.cs native/LanFlow.Desktop.Tests/ShellIconServiceTests.cs native/LanFlow.Desktop/MainWindow.xaml.cs
git commit -m "perf: load shell icons asynchronously"
```

Expected: 所有测试 `Failed: 0`；桌面构建成功；代码搜索 `GetIcon(` 和 `LoadIcons(` 不再命中生产代码；快速切组三次后只有最后一组项目收到图标。

---

## 阶段 1 完成门

- [ ] 旧配置透明度和 `tile` 布局迁移测试通过，新配置默认分层 85%。
- [ ] `VisibleItems` 在切组和搜索时引用不变，且只发合并集合通知。
- [ ] 三个项目模板首帧拥有明确容器、图标和文字尺寸。
- [ ] `ApplyItemMetrics`、同步 `UpdateLayout`、递归全项目视觉树遍历已从切组路径移除。
- [ ] 图标服务通过同键合并、优先级、取消、256 容量和文件失效测试。
- [ ] Release 下两个测试项目和桌面构建全部通过。
