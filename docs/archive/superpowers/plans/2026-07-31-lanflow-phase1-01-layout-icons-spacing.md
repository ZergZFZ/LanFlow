# LanFlow Phase 1 Layout, Icons, and Spacing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 删除列表布局入口，兼容旧配置，修复网格/卡片切换后的图标刷新，并使列间距准确控制水平间隔。

**Architecture:** 保持现有 `VirtualizingWrapPanel`、`ViewportIconCoordinator` 和视图状态快照；布局切换只更换网格/卡片模板，在 Loaded 调度点重新附着面板并刷新可见图标。配置层和 ViewModel 层同时归一化旧 `list` 值，避免任何调用路径重新引入列表模式。

**Tech Stack:** C# 12、.NET 8、WPF ItemsControl/VirtualizingWrapPanel、xUnit。

## Global Constraints

- `layoutMode = "list"` 必须迁移为 `grid`。
- 用户界面仅保留 `grid` 和 `card`。
- JSON 字段 `itemSpacing` 与 C# 属性 `ItemSpacing` 不改名。
- 列间距范围保持 0–64。
- 图标刷新继续走 `ViewportIconCoordinator`，不增加全量同步加载。
- 不删除与本阶段无关的历史模板。

---

### Task 1: 锁定旧列表配置迁移

**Files:**
- Modify: `native/LanFlow.Core.Tests/ConfigStoreTests.cs`
- Modify: `native/LanFlow.Core/Services/ConfigStore.cs`
- Modify: `native/LanFlow.Core/ViewModels/MainViewModel.cs`
- Modify: `native/LanFlow.Core/Models/SettingsOptionValues.cs`

**Interfaces:**
- Consumes: `ConfigStore.Load()`, `MainViewModel.ApplySettings(Settings)` 的现有归一化路径。
- Produces: 只返回 `SettingsOptionValues.GridLayout` 或 `SettingsOptionValues.CardLayout` 的布局值。

- [ ] **Step 1: 添加旧 list 配置失败测试**

在 `ConfigStoreTests` 增加：

```csharp
[Theory]
[InlineData("list", SettingsOptionValues.GridLayout)]
[InlineData("grid", SettingsOptionValues.GridLayout)]
[InlineData("card", SettingsOptionValues.CardLayout)]
[InlineData("unexpected", SettingsOptionValues.GridLayout)]
public void Load_NormalizesSupportedAndLegacyLayoutModes(string input, string expected)
{
    File.WriteAllText(
        Path.Combine(_tempDirectory, "config.json"),
        $$"""{ "settings": { "layoutMode": "{{input}}" }, "groups": [] }""");

    var settings = new ConfigStore("Alt+Space", _tempDirectory).Load().Settings;

    Assert.Equal(expected, settings.LayoutMode);
}
```

- [ ] **Step 2: 运行测试确认 list 分支先失败**

Run:

```powershell
dotnet test native\LanFlow.Core.Tests\LanFlow.Core.Tests.csproj -c Release --filter Load_NormalizesSupportedAndLegacyLayoutModes
```

Expected：`list` 用例失败，因为当前仍保留为 `list`。

- [ ] **Step 3: 修改 ConfigStore 归一化**

在 `ConfigStore` 增加私有兼容常量并替换布局 switch：

```csharp
private const string LegacyListLayout = "list";

settings.LayoutMode = settings.LayoutMode switch
{
    SettingsOptionValues.CardLayout => SettingsOptionValues.CardLayout,
    SettingsOptionValues.GridLayout => SettingsOptionValues.GridLayout,
    LegacyListLayout => SettingsOptionValues.GridLayout,
    _ => SettingsOptionValues.GridLayout,
};
```

- [ ] **Step 4: 同步 MainViewModel 设置复制规则**

把 `MainViewModel` 中允许 list 的 switch 改为：

```csharp
settings.LayoutMode = source.LayoutMode switch
{
    SettingsOptionValues.CardLayout => SettingsOptionValues.CardLayout,
    _ => SettingsOptionValues.GridLayout,
};
```

- [ ] **Step 5: 删除公开列表选项常量**

从 `SettingsOptionValues` 删除：

```csharp
public const string ListLayout = "list";
```

然后编译查找所有剩余引用：

```powershell
Get-ChildItem native -Recurse -File -Include *.cs,*.xaml | Select-String -Pattern 'ListLayout|Tag="list"|VirtualizingListItemsPanel'
```

Expected：只剩下一阶段明确要处理的设置 XAML/MainWindow 引用，不出现未知调用方。

- [ ] **Step 6: 运行 Core 测试**

Run:

```powershell
dotnet test native\LanFlow.Core.Tests\LanFlow.Core.Tests.csproj -c Release --filter "ConfigStoreTests|SettingsCloneTests"
```

Expected：0 failed。

### Task 2: 删除设置页列表入口并改正文案

**Files:**
- Modify: `native/LanFlow.Desktop.Tests/SettingsWindowContractTests.cs`
- Modify: `native/LanFlow.Desktop/Views/SettingsWindow.xaml`
- Modify: `native/LanFlow.Desktop/Views/SettingsWindow.xaml.cs`

**Interfaces:**
- Consumes: `SettingsWindowViewModel.Working.LayoutMode`。
- Produces: `GridLayoutRadio`、`CardLayoutRadio` 两个选择；列间距仍写入 `itemSpacing`。

- [ ] **Step 1: 修改合同测试为只允许两种布局**

把现有布局选择断言替换为：

```csharp
Assert.Contains("Tag=\"grid\"", xaml);
Assert.Contains("Tag=\"card\"", xaml);
Assert.DoesNotContain("Tag=\"list\"", xaml);
Assert.DoesNotContain("x:Name=\"ListLayoutRadio\"", xaml);
Assert.Contains(">列间距<", xaml);
Assert.Contains("调整同一行中相邻项目之间的水平距离。", xaml);
```

- [ ] **Step 2: 运行合同测试确认失败**

Run:

```powershell
dotnet test native\LanFlow.Desktop.Tests\LanFlow.Desktop.Tests.csproj -c Release --filter SettingsWindow_OffersEveryLayoutTransparencyAndAnimationChoice
```

Expected：失败，报告仍存在 `Tag="list"` 或缺少“列间距”。

- [ ] **Step 3: 删除 ListLayoutRadio 并更新列间距文字**

设置布局行只保留：

```xml
<RadioButton x:Name="GridLayoutRadio" Content="网格" Tag="grid" Checked="LayoutMode_Checked" />
<RadioButton x:Name="CardLayoutRadio" Content="卡片" Tag="card" Checked="LayoutMode_Checked" />
```

字段标题和说明改为：

```xml
<TextBlock Style="{StaticResource SettingsFieldTitleStyle}" Text="列间距" />
<TextBlock Style="{StaticResource SettingsFieldDescriptionStyle}"
           Text="调整同一行中相邻项目之间的水平距离。" />
```

- [ ] **Step 4: 删除 code-behind 的列表选择逻辑**

`LoadControls()` 改为：

```csharp
GridLayoutRadio.IsChecked = Working.LayoutMode == SettingsOptionValues.GridLayout;
CardLayoutRadio.IsChecked = Working.LayoutMode == SettingsOptionValues.CardLayout;
if (GridLayoutRadio.IsChecked != true && CardLayoutRadio.IsChecked != true)
{
    GridLayoutRadio.IsChecked = true;
}
```

- [ ] **Step 5: 运行设置合同测试**

Run:

```powershell
dotnet test native\LanFlow.Desktop.Tests\LanFlow.Desktop.Tests.csproj -c Release --filter SettingsWindowContractTests
```

Expected：0 failed。

### Task 3: 锁定列间距的几何行为

**Files:**
- Modify: `native/LanFlow.Desktop.Tests/VirtualizingWrapLayoutTests.cs`
- Inspect/Modify only if test exposes duplication: `native/LanFlow.Desktop/Controls/VirtualizingWrapLayout.cs`
- Modify: `native/LanFlow.Desktop/MainWindow.xaml`?`VirtualizingWrapItemsPanel`?`LauncherTile`?`LauncherCard`?

**Interfaces:**
- Consumes: `VirtualizingWrapLayout.GetItemRect`, `CalculateRange`。
- Produces: 实际相邻项目 X 坐标差等于 `itemWidth + horizontalSpacing`。

- [ ] **Step 1: 增加水平间距几何测试**

```csharp
[Theory]
[InlineData(0, 100)]
[InlineData(8, 108)]
[InlineData(32, 132)]
[InlineData(64, 164)]
public void HorizontalSpacing_DefinesExactDistanceBetweenColumns(double spacing, double expectedX)
{
    var layout = new VirtualizingWrapLayout(100, 80, spacing, 10, 1);

    var second = layout.GetItemRect(index: 1, columns: 4);

    Assert.Equal(expectedX, second.X);
}

[Fact]
public void CalculateRange_UsesTheSameHorizontalPitchAsItemPlacement()
{
    var layout = new VirtualizingWrapLayout(100, 80, 20, 10, 1);

    var range = layout.CalculateRange(20, viewportWidth: 340, viewportHeight: 80, verticalOffset: 0);

    Assert.Equal(3, range.Columns);
}
```

- [ ] **Step 2: 运行布局测试**

Run:

```powershell
dotnet test native\LanFlow.Desktop.Tests\LanFlow.Desktop.Tests.csproj -c Release --filter VirtualizingWrapLayoutTests
```

Expected：0 failed；当前 `CalculateColumns` 和 `GetItemRect` 已共同使用 `itemWidth + horizontalSpacing`，本任务不修改 `VirtualizingWrapLayout.cs`。

- [ ] **Step 3: 增加 XAML 合同检查避免双重水平 Margin**

在 `native/LanFlow.Desktop.Tests/VirtualizingWrapPanelContractTests.cs` 读取 `MainWindow.xaml` 并断言：

```csharp
Assert.Contains("HorizontalSpacing=\"{Binding DataContext.Settings.ItemSpacing", xaml);
Assert.DoesNotContain("VirtualizingListItemsPanel", xaml);
Assert.DoesNotContain("x:Key=\"LauncherList\"", xaml);
```

从 `LauncherTile` 和 `LauncherCard` 删除绑定 `ItemSpacing` 的 `Margin` Setter，统一设置 `Margin="0"`；水平空隙只由 `VirtualizingWrapPanel.HorizontalSpacing` 提供。

- [ ] **Step 4: 删除列表 ItemsPanel 资源**

从 `MainWindow.xaml` 删除 `VirtualizingListItemsPanel` 和 `LauncherList`：

```xml
<ItemsPanelTemplate x:Key="VirtualizingListItemsPanel">
    <VirtualizingStackPanel />
</ItemsPanelTemplate>
```

- [ ] **Step 5: 运行虚拟化相关测试**

Run:

```powershell
dotnet test native\LanFlow.Desktop.Tests\LanFlow.Desktop.Tests.csproj -c Release --filter "VirtualizingWrapLayoutTests|VirtualizingWrapPanelContractTests"
```

Expected：0 failed。

### Task 4: 修复网格/卡片切换后的图标刷新

**Files:**
- Modify: `native/LanFlow.Desktop.Tests/MainWindowArchitectureContractTests.cs`
- Modify: `native/LanFlow.Desktop/MainWindow.xaml.cs`

**Interfaces:**
- Consumes: `ViewportIconCoordinator.CancelPending()`, `AttachVirtualizingPanel()`, `RestoreViewState(bool)`, `LoadVisibleIconsAsync()`。
- Produces: `QueueLayoutRefresh(bool reuseOffset)`，在新模板 Loaded 后恢复视图并刷新图标。

- [ ] **Step 1: 添加架构合同测试**

```csharp
[Fact]
public void LayoutSwitch_CancelsStaleIconsAndQueuesAVisibleRefresh()
{
    var code = File.ReadAllText(GetDesktopPath("MainWindow.xaml.cs"));

    Assert.Contains("_iconCoordinator.CancelPending();", code);
    Assert.Contains("QueueLayoutRefresh", code);
    Assert.Contains("DispatcherPriority.Loaded", code);
    Assert.Contains("_ = LoadVisibleIconsAsync();", code);
    Assert.DoesNotContain("SettingsOptionValues.ListLayout", code);
    Assert.DoesNotContain("VirtualizingListItemsPanel", code);
}
```

- [ ] **Step 2: 运行合同测试确认失败**

Run:

```powershell
dotnet test native\LanFlow.Desktop.Tests\LanFlow.Desktop.Tests.csproj -c Release --filter LayoutSwitch_CancelsStaleIconsAndQueuesAVisibleRefresh
```

Expected：失败，因为尚未提取 `QueueLayoutRefresh` 或仍含列表分支。

- [ ] **Step 3: 收敛 ApplyLayoutSettings**

保留现有样式资源名称，核心逻辑改为：

```csharp
private void ApplyLayoutSettings(Settings settings)
{
    string requestedLayoutMode = settings.LayoutMode == SettingsOptionValues.CardLayout
        ? SettingsOptionValues.CardLayout
        : SettingsOptionValues.GridLayout;
    bool layoutModeChanged = !string.Equals(_activeLayoutMode, requestedLayoutMode, StringComparison.Ordinal);

    if (layoutModeChanged)
    {
        SaveCurrentViewState(_activeLayoutMode);
        _iconCoordinator.CancelPending();
        _activeLayoutMode = requestedLayoutMode;
    }

    bool cardMode = requestedLayoutMode == SettingsOptionValues.CardLayout;
    ItemList.ItemContainerStyle =
        (Style)FindResource(cardMode ? "LauncherCard" : "LauncherTile");
    ItemList.ItemTemplate =
        (DataTemplate)FindResource(cardMode ? "CardItemTemplate" : "TileItemTemplate");

    var wrapPanel = (ItemsPanelTemplate)FindResource("VirtualizingWrapItemsPanel");
    if (!ReferenceEquals(ItemList.ItemsPanel, wrapPanel))
    {
        ItemList.ItemsPanel = wrapPanel;
    }

    QueueLayoutRefresh(reuseOffset: !layoutModeChanged);
}
```

资源键固定使用现有 `LauncherTile`、`LauncherCard`、`TileItemTemplate`、`CardItemTemplate`。

- [ ] **Step 4: 添加统一 Loaded 刷新方法**

```csharp
private void QueueLayoutRefresh(bool reuseOffset)
{
    Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
    {
        if (_isClosed)
        {
            return;
        }

        AttachVirtualizingPanel();
        RestoreViewState(reuseOffset);
        _ = LoadVisibleIconsAsync();
    });
}
```

`QueueLayoutRefresh` 固定只执行一次 `AttachVirtualizingPanel()`、一次 `RestoreViewState(reuseOffset)` 和一次 `LoadVisibleIconsAsync()`；`RestoreViewState` 本身不再增加附着或刷新调用。

- [ ] **Step 5: 运行桌面相关测试与构建**

Run:

```powershell
dotnet test native\LanFlow.Desktop.Tests\LanFlow.Desktop.Tests.csproj -c Release --filter "MainWindowArchitectureContractTests|ViewportIconCoordinatorTests|VirtualizingWrapLayoutTests|VirtualizingWrapPanelContractTests|SettingsWindowContractTests"
dotnet build native\LanFlow.Desktop\LanFlow.Desktop.csproj -c Release
```

Expected：0 failed，Build succeeded。

- [ ] **Step 6: 提交 Phase 1**

```powershell
git add native/LanFlow.Core native/LanFlow.Core.Tests native/LanFlow.Desktop native/LanFlow.Desktop.Tests
git commit -m "fix: stabilize grid and card layout switching"
```
