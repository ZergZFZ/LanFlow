# LanFlow 阶段 4：设置页与最终验证 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把设置窗口重构为分类式、可预览、可应用和可取消的事务界面，保留全部现有设置，并用可复现的功能与性能验证完成本轮原地重构。

**Architecture:** `SettingsPreviewSession` 保存原始快照与工作副本，所有控件只修改工作副本，主窗口通过统一预览事件临时应用。设置页用左侧分类、右侧滚动内容和固定底部操作区呈现；最终把主窗口中设置协调、图标协调和拖放协调抽到小型展示服务，并保留现有业务模型。

**Tech Stack:** .NET 8、WPF、XAML、xUnit、`INotifyPropertyChanged`、Dispatcher 节流、Stopwatch/CSV 性能采样。

## Global Constraints

- 设置页必须是左侧分类导航、右侧当前分类滚动区、底部固定操作区。
- 不得把全部设置重新堆为单一长页，也不得用“每项一个大卡片”的低密度布局。
- 分类至少覆盖：外观与主题、布局与项目、分组标签、透明度与材质、交互与动画、启动与快捷键、性能与缓存、关于；可小幅合并但不得遗漏。
- 所有现有设置必须可找到、可编辑并生效。
- 打开窗口建立原始快照和工作副本；预览不持久化；应用保存；取消恢复；未保存关闭警告。
- 应用成功后原始快照更新，使后续取消只回到最近一次应用状态。
- 两种透明模式分别记忆值；“恢复 85%”只修改当前模式预览；滑块显示精确百分比。
- 透明度、图标/卡片/文字尺寸、标签大小/字体/左侧宽度等连续滑块预览固定 33 ms 节流；滑块拖动不写磁盘，松开或应用时提交工作副本。
- 标签尺寸、字体、左侧宽度的预览必须钳制到阶段 1 的范围；左侧宽度只在左侧布局时启用。
- 未保存关闭提示提供“应用并关闭 / 放弃 / 返回设置”三种明确结果。
- 只做目标职责抽取，不重写全部 MVVM 或业务层。
- 最终验收使用 Windows 11、Release、500 总项目/100 当前组，记录硬件、分辨率、缩放、透明模式、缓存冷热和帧时间分布。
- 性能目标：选中反馈 P95 约 50 ms，热缓存内容稳定 P95 约 100 ms，滚动接近 60 FPS；不得以主观描述代替数据。
- 每个任务 TDD、完整验证和独立提交。

---

### Task 1: 实现设置预览事务

**Files:**
- Create: `native/LanFlow.Desktop/Presentation/SettingsPreviewSession.cs`
- Create: `native/LanFlow.Core/Services/SettingsNormalizer.cs`
- Create: `native/LanFlow.Core/Services/SettingsComparer.cs`
- Modify: `native/LanFlow.Core/Services/ConfigStore.cs`
- Create: `native/LanFlow.Desktop.Tests/SettingsPreviewSessionTests.cs`
- Modify: `native/LanFlow.Desktop/Views/SettingsWindow.xaml.cs`
- Modify: `native/LanFlow.Desktop/MainWindow.xaml.cs`

**Interfaces:**
- Consumes: `Settings.Clone()`, normalized settings from阶段 1.
- Produces: frozen cross-plan interface `SettingsPreviewSession`; a single `PreviewRequested` event; commit/cancel semantics.

- [ ] **Step 1: 写工作副本、预览、应用后重置基线和取消恢复失败测试**

```csharp
using LanFlow.Desktop.Models;
using LanFlow.Desktop.Presentation;

namespace LanFlow.Desktop.Tests;

public sealed class SettingsPreviewSessionTests
{
    [Fact]
    public void Update_ChangesWorkingCopyAndRaisesPreviewWithoutMutatingOriginalInput()
    {
        var source = new Settings { LayeredOpacity = 0.85 };
        var session = new SettingsPreviewSession(source);
        Settings? preview = null;
        session.PreviewRequested += (_, value) => preview = value;

        session.Update(settings => settings.LayeredOpacity = 0.62);

        Assert.Equal(0.85, source.LayeredOpacity, 3);
        Assert.Equal(0.62, session.Working.LayeredOpacity, 3);
        Assert.Equal(0.62, Assert.IsType<Settings>(preview).LayeredOpacity, 3);
        Assert.True(session.HasChanges);
    }

    [Fact]
    public void Commit_UpdatesBaselineSoLaterCancelReturnsLastAppliedSettings()
    {
        var session = new SettingsPreviewSession(new Settings { TextSize = 13 });
        session.Update(settings => settings.TextSize = 15);
        var committed = session.Commit();
        session.Update(settings => settings.TextSize = 17);

        var restored = session.Cancel();

        Assert.Equal(15, committed.TextSize);
        Assert.Equal(15, restored.TextSize);
        Assert.False(session.HasChanges);
    }

    [Fact]
    public void Cancel_RaisesPreviewForRestoredSnapshot()
    {
        var session = new SettingsPreviewSession(new Settings { GroupLabelSize = 36 });
        Settings? lastPreview = null;
        session.PreviewRequested += (_, value) => lastPreview = value;
        session.Update(settings => settings.GroupLabelSize = 48);

        session.Cancel();

        Assert.Equal(36, Assert.IsType<Settings>(lastPreview).GroupLabelSize);
    }
}
```

- [ ] **Step 2: 运行测试并确认 session 不存在**

Run: `dotnet test native\LanFlow.Desktop.Tests\LanFlow.Desktop.Tests.csproj -c Release --filter SettingsPreviewSessionTests`

Expected: 编译失败，缺少 `SettingsPreviewSession`。

- [ ] **Step 3: 实现冻结接口并用序列化等价比较检测变更**

```csharp
namespace LanFlow.Desktop.Presentation;

public sealed class SettingsPreviewSession
{
    private Settings _original;

    public SettingsPreviewSession(Settings settings)
    {
        _original = settings.Clone();
        Working = settings.Clone();
    }

    public Settings Original => _original.Clone();
    public Settings Working { get; private set; }
    public bool HasChanges => !SettingsComparer.Equals(_original, Working);
    public event EventHandler<Settings>? PreviewRequested;

    public void Update(Action<Settings> mutation)
    {
        mutation(Working);
        SettingsNormalizer.ClampPreviewValues(Working);
        PreviewRequested?.Invoke(this, Working.Clone());
    }

    public Settings Commit()
    {
        _original = Working.Clone();
        return _original.Clone();
    }

    public Settings Cancel()
    {
        Working = _original.Clone();
        PreviewRequested?.Invoke(this, Working.Clone());
        return Working.Clone();
    }
}
```

把阶段 1 的范围钳制提取到 `native/LanFlow.Core/Services/SettingsNormalizer.cs`：

```csharp
public static class SettingsNormalizer
{
    public static void ClampPreviewValues(Settings settings)
    {
        settings.GroupLabelSize = Math.Clamp(settings.GroupLabelSize, 28, 52);
        settings.GroupLabelFontSize = Math.Clamp(settings.GroupLabelFontSize, 11, 18);
        settings.GroupNavigationWidth = Math.Clamp(settings.GroupNavigationWidth, 96, 280);
        settings.LayeredOpacity = Math.Clamp(settings.LayeredOpacity, 0.40, 1.00);
        settings.WholeWindowOpacity = Math.Clamp(settings.WholeWindowOpacity, 0.40, 1.00);
    }
}
```

`ConfigStore` 和 session 共用该方法，避免持久化与预览边界不一致。`SettingsComparer` 放在 `native/LanFlow.Core/Services/SettingsComparer.cs`，通过 `JsonSerializer.Serialize` 使用固定 `JsonSerializerOptions` 比较两个 `Settings` 的全部持久字段；运行时 `JsonIgnore` 图像和请求版本自然不参与比较：

```csharp
public static class SettingsComparer
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    public static bool Equals(Settings left, Settings right) =>
        StringComparer.Ordinal.Equals(JsonSerializer.Serialize(left, Options), JsonSerializer.Serialize(right, Options));
}
```

- [ ] **Step 4: 用 session 替换两个窗口里的重复 Clone 和直接预览**

`SettingsWindow` 构造函数接收 `SettingsPreviewSession`；删除本地 `_working` 克隆器和私有 `Clone(Settings)`。主窗口订阅一次 `PreviewRequested` 并调用统一 `ApplySettingsPreview(Settings)`；DialogResult=true 时调用 `Commit()` 后替换 view model 设置并保存；取消或 DialogResult=false 调用 `Cancel()`。

- [ ] **Step 5: 运行事务测试、Core 测试和构建并提交**

Run:

```powershell
dotnet test native\LanFlow.Desktop.Tests\LanFlow.Desktop.Tests.csproj -c Release --filter SettingsPreviewSessionTests
dotnet test native\LanFlow.Core.Tests\LanFlow.Core.Tests.csproj -c Release
dotnet build native\LanFlow.Desktop\LanFlow.Desktop.csproj -c Release
git diff --check
git add native/LanFlow.Desktop/Presentation/SettingsPreviewSession.cs native/LanFlow.Core/Services/SettingsNormalizer.cs native/LanFlow.Core/Services/SettingsComparer.cs native/LanFlow.Desktop.Tests/SettingsPreviewSessionTests.cs native/LanFlow.Desktop/Views/SettingsWindow.xaml.cs native/LanFlow.Desktop/MainWindow.xaml.cs native/LanFlow.Core/Services/ConfigStore.cs
git commit -m "feat: add transactional settings preview"
```

Expected: 所有测试通过；预览不写配置文件；应用后再次修改再取消回到最近应用值。

---

### Task 2: 重构设置页信息架构与可绑定状态

**Files:**
- Create: `native/LanFlow.Desktop/Presentation/SettingsCategory.cs`
- Create: `native/LanFlow.Desktop/Presentation/SettingsWindowViewModel.cs`
- Modify: `native/LanFlow.Desktop/Views/SettingsWindow.xaml`
- Modify: `native/LanFlow.Desktop/Views/SettingsWindow.xaml.cs`
- Modify: `native/LanFlow.Desktop/Themes/Components.xaml`
- Create: `native/LanFlow.Desktop.Tests/SettingsWindowViewModelTests.cs`
- Create: `native/LanFlow.Desktop.Tests/SettingsWindowContractTests.cs`

**Interfaces:**
- Consumes: Task 1 `SettingsPreviewSession`; all current Settings fields.
- Produces: category list, selected category, command methods `Apply()`, `Cancel()`, `ResetCurrentOpacity()`; dependency state such as `IsLeftNavigationWidthEnabled`.

- [ ] **Step 1: 写分类、字段覆盖和依赖状态失败测试**

```csharp
[Fact]
public void Categories_CoverApprovedInformationArchitecture()
{
    var viewModel = CreateViewModel();
    Assert.Equal([
        "外观与主题", "布局与项目", "分组标签", "透明度与材质",
        "交互与动画", "启动与快捷键", "性能与缓存", "关于"
    ], viewModel.Categories.Select(category => category.Title));
}

[Theory]
[InlineData(SettingsOptionValues.GroupLeft, true)]
[InlineData(SettingsOptionValues.GroupTop, false)]
public void LeftNavigationWidth_OnlyEnabledForLeftLayout(string layout, bool expected)
{
    var viewModel = CreateViewModel();
    viewModel.Update(settings => settings.GroupLayout = layout);
    Assert.Equal(expected, viewModel.IsLeftNavigationWidthEnabled);
}
```

合同测试读取 XAML，断言存在左侧 category ListBox、右侧 ScrollViewer、固定 footer Grid；断言所有当前 Settings JSON 名称均在分类映射表中出现：hotkey、theme、themeProfile、themeColors、customThemes、opacity、layoutMode、iconSize、cardWidth、cardHeight、cardSize、textSize、itemSpacing、rowSpacing、contentPadding、showShortcutBadge、showFullItemName、showItemTitle、groupLayout、groupSwitchMode、groupLabelSize、groupLabelFontSize、groupNavigationWidth、transparencyMode、layeredOpacity、wholeWindowOpacity、animationMode、startWithWindows、openItemsOnSingleClick。`themeColors` 由自定义配色编辑器覆盖；`opacity` 与 `cardSize` 作为兼容字段保留在映射中但不创建重复控件。

- [ ] **Step 2: 运行测试并确认 view model/结构不存在**

Run: `dotnet test native\LanFlow.Desktop.Tests\LanFlow.Desktop.Tests.csproj -c Release --filter "SettingsWindowViewModelTests|SettingsWindowContractTests"`

Expected: 编译失败或合同断言显示仍为单一长页面。

- [ ] **Step 3: 实现分类和设置页展示模型**

```csharp
public sealed record SettingsCategory(string Id, string Title, string Description);

public sealed class SettingsWindowViewModel : INotifyPropertyChanged
{
    public SettingsPreviewSession Session { get; }
    public IReadOnlyList<SettingsCategory> Categories { get; }
    public SettingsCategory SelectedCategory { get; set; }
    public Settings Working => Session.Working;
    public bool HasChanges => Session.HasChanges;
    public bool IsLeftNavigationWidthEnabled => Working.GroupLayout == SettingsOptionValues.GroupLeft;
    public double CurrentOpacity => Working.TransparencyMode == SettingsOptionValues.TransparencyWholeWindow
        ? Working.WholeWindowOpacity : Working.LayeredOpacity;

    public void Update(Action<Settings> mutation);
    public Settings Apply();
    public Settings Cancel();
    public void ResetCurrentOpacity();
}
```

`Update` 调用 session 后对 `Working`、`HasChanges`、`IsLeftNavigationWidthEnabled`、`CurrentOpacity` 发属性通知。`ResetCurrentOpacity` 只把当前模式值设为 0.85。

- [ ] **Step 4: 把 XAML 改为三段式分类布局**

根布局固定三列/两行：左侧 184 DIP 分类栏；右侧当前分类 `ScrollViewer`；第二行跨两列固定 footer。分类内容使用紧凑两列 Grid：标签/说明在左，控件在右；相关设置共享 section，不为每项创建大卡片。

分类显示规则：

- 外观与主题：主题、主题配置、自定义颜色。
- 布局与项目：网格/列表/卡片、图标、卡片尺寸、文字、间距、标题/完整名称/快捷标记。
- 分组标签：顶部/左侧、点击/悬停、标签大小、字体、左侧宽度。
- 透明度与材质：两模式、当前滑块、精确百分比、恢复 85%；两个模式选择器分别写入 `SettingsOptionValues.TransparencyLayered` 与 `SettingsOptionValues.TransparencyWholeWindow`。
- 交互与动画：单击打开、system/on/off。
- 启动与快捷键：开机启动、热键。
- 性能与缓存：内存缓存 256 的说明和“清空图标缓存”按钮，不暴露危险线程参数。
- 关于：版本、更新检查和现有链接。

- [ ] **Step 5: 删除散落控件读取，统一走 view model 更新**

事件处理只把具体值传给 `SettingsWindowViewModel.Update`；不得再次手工复制所有字段。保留颜色选择和更新下载的现有业务，但让分类面板调用已有服务。footer 的“应用”在无变更时禁用，“取消”始终可用。

- [ ] **Step 6: 运行测试、键盘导航和 100/125/150% 缩放验证并提交**

Run:

```powershell
dotnet test native\LanFlow.Desktop.Tests\LanFlow.Desktop.Tests.csproj -c Release --filter "SettingsWindowViewModelTests|SettingsWindowContractTests"
dotnet build native\LanFlow.Desktop\LanFlow.Desktop.csproj -c Release
git diff --check
git add native/LanFlow.Desktop/Presentation/SettingsCategory.cs native/LanFlow.Desktop/Presentation/SettingsWindowViewModel.cs native/LanFlow.Desktop/Views/SettingsWindow.xaml native/LanFlow.Desktop/Views/SettingsWindow.xaml.cs native/LanFlow.Desktop/Themes/Components.xaml native/LanFlow.Desktop.Tests/SettingsWindowViewModelTests.cs native/LanFlow.Desktop.Tests/SettingsWindowContractTests.cs
git commit -m "style: reorganize settings into categories"
```

Expected: 分类和字段覆盖测试通过；Tab 键顺序合理；footer 不随内容滚动；高 DPI 下无裁切。

---

### Task 3: 实现透明度节流预览和未保存关闭流程

**Files:**
- Create: `native/LanFlow.Desktop/Presentation/PreviewThrottle.cs`
- Create: `native/LanFlow.Desktop/Presentation/UnsavedCloseDecision.cs`
- Modify: `native/LanFlow.Desktop/Presentation/SettingsWindowViewModel.cs`
- Modify: `native/LanFlow.Desktop/Views/SettingsWindow.xaml`
- Modify: `native/LanFlow.Desktop/Views/SettingsWindow.xaml.cs`
- Create: `native/LanFlow.Desktop.Tests/PreviewThrottleTests.cs`
- Create: `native/LanFlow.Desktop.Tests/SettingsCloseFlowTests.cs`

**Interfaces:**
- Consumes: Settings preview session and WPF slider drag events.
- Produces: 33 ms trailing-edge coalescing; `UnsavedCloseDecision.ApplyAndClose/Discard/KeepEditing`.

- [ ] **Step 1: 写节流合并和关闭决策失败测试**

虚拟时钟测试在 10 ms 内输入 0.80、0.70、0.60，只在第 33 ms 预览 0.60；`Flush()` 立即提交最后值。关闭流程测试：无变更直接关闭；KeepEditing 取消关闭；Discard 调用 session.Cancel；ApplyAndClose 调用 session.Commit 并请求持久化一次。

- [ ] **Step 2: 运行测试并确认类型不存在**

Run: `dotnet test native\LanFlow.Desktop.Tests\LanFlow.Desktop.Tests.csproj -c Release --filter "PreviewThrottleTests|SettingsCloseFlowTests"`

Expected: 编译失败，缺少 throttle/decision 类型。

- [ ] **Step 3: 实现 33 ms trailing-edge 预览节流**

```csharp
public sealed class PreviewThrottle<T> : IDisposable
{
    public PreviewThrottle(TimeSpan interval, ITimerScheduler scheduler, Action<T> apply);
    public void Push(T value);
    public void Flush();
    public void Dispose();
}
```

`Push` 覆盖 pending 值而不启动多个 timer；timer 到期应用最后值；`Flush` 取消 timer 并应用 pending。透明度、图标尺寸、卡片宽高、文字大小、项目/行间距、内容边距、标签大小、标签字体和左侧宽度等所有连续 Slider 的 ValueChanged 都调用各自 `PreviewThrottle<double>.Push`，DragCompleted 调用 `Flush`；任何路径都只更新 session，不调用 config save。切换分类或关闭窗口前也 Flush，避免最后输入丢失。

- [ ] **Step 4: 实现三选项未保存关闭提示**

`UnsavedCloseDecision`：

```csharp
public enum UnsavedCloseDecision { ApplyAndClose, Discard, KeepEditing }
```

窗口 `Closing` 时：无变更直接允许；有变更弹出自定义紧凑对话框，三个按钮明确映射；KeepEditing 设置 `e.Cancel=true`；Discard 调用 Cancel 后关闭；ApplyAndClose Flush throttle、Commit、持久化一次后关闭。不得用只有 Yes/No 且语义不明的提示。

- [ ] **Step 5: 验证模式独立值、恢复 85% 和持久化次数并提交**

Run:

```powershell
dotnet test native\LanFlow.Desktop.Tests\LanFlow.Desktop.Tests.csproj -c Release --filter "PreviewThrottleTests|SettingsCloseFlowTests|SettingsPreviewSessionTests"
dotnet build native\LanFlow.Desktop\LanFlow.Desktop.csproj -c Release
git diff --check
git add native/LanFlow.Desktop/Presentation/PreviewThrottle.cs native/LanFlow.Desktop/Presentation/UnsavedCloseDecision.cs native/LanFlow.Desktop/Presentation/SettingsWindowViewModel.cs native/LanFlow.Desktop/Views/SettingsWindow.xaml native/LanFlow.Desktop/Views/SettingsWindow.xaml.cs native/LanFlow.Desktop.Tests/PreviewThrottleTests.cs native/LanFlow.Desktop.Tests/SettingsCloseFlowTests.cs
git commit -m "feat: throttle previews and guard unsaved settings"
```

Expected: 连续拖动最多约每 33 ms 一次预览；切换模式恢复各自值；恢复 85% 只改当前模式；拖动不写磁盘；三种关闭决策正确。

---

### Task 4: 抽取主窗口性能关键协调职责并清理旧路径

**Files:**
- Create: `native/LanFlow.Desktop/Presentation/MainWindowSettingsCoordinator.cs`
- Create: `native/LanFlow.Desktop/Presentation/LauncherDragDropCoordinator.cs`
- Modify: `native/LanFlow.Desktop/MainWindow.xaml.cs`
- Modify: `native/LanFlow.Desktop/MainWindow.xaml`
- Create: `native/LanFlow.Desktop.Tests/MainWindowArchitectureContractTests.cs`

**Interfaces:**
- Consumes: SettingsPreviewSession, WindowAppearanceController, ThemeResourceUpdater, ViewportIconCoordinator, GroupSwitchCoordinator.
- Produces: focused coordinators; MainWindow remains event host and composition root, not a second business model.

- [ ] **Step 1: 写旧路径清理和文件职责失败测试**

合同测试扫描生产代码，断言不存在：`ApplyItemMetrics`、`RefreshGroupTabs`、`LoadIcons()`、`ItemList.UpdateLayout()`、同步 `ShellIconService.GetIcon`、SettingsWindow 私有 `Clone(Settings)`。另断言 `MainWindow.xaml.cs` 不再声明 Shell P/Invoke、LRU、hover timer 或透明度计算。

- [ ] **Step 2: 运行合同测试并记录尚存职责**

Run: `dotnet test native\LanFlow.Desktop.Tests\LanFlow.Desktop.Tests.csproj -c Release --filter MainWindowArchitectureContractTests`

Expected: 若阶段 1–3 未完全清理，测试列出具体命中；不得通过放宽字符串而绕过。

- [ ] **Step 3: 抽取设置协调器**

`MainWindowSettingsCoordinator` 公开：

```csharp
public sealed class MainWindowSettingsCoordinator
{
    public void Preview(Settings settings);
    public void Apply(Settings settings);
    public void Restore(Settings settings);
}
```

三方法共用一个私有 `ApplyCore`：更新主题资源、窗口外观、布局参数、导航参数、动画模式和图标尺寸；只有 Apply 调用上层提供的保存委托，Preview/Restore 不写磁盘。

- [ ] **Step 4: 抽取拖放逻辑并保留逻辑索引映射**

`LauncherDragDropCoordinator` 接收 `VirtualizingWrapLayout` 和 view model 操作委托，提供 `Begin`、`Update`、`Drop`、`Cancel`；保存源组/源索引/当前 generation；搜索过滤时使用明确的源索引映射。MainWindow 只负责鼠标捕获、adorner 展示和把坐标传入协调器。

- [ ] **Step 5: 删除迁移期并行旧实现并通过合同测试**

删除所有已不可达的后置尺寸、全量导航、同步图标、重复 clone 和窗口内透明度计算。不得保留 feature flag 或备用旧面板；git 历史已经提供回退能力。

- [ ] **Step 6: 运行全部测试、构建和提交**

Run:

```powershell
dotnet test native\LanFlow.Core.Tests\LanFlow.Core.Tests.csproj -c Release
dotnet test native\LanFlow.Desktop.Tests\LanFlow.Desktop.Tests.csproj -c Release
dotnet build native\LanFlow.Desktop\LanFlow.Desktop.csproj -c Release
git diff --check
git add native/LanFlow.Desktop/Presentation/MainWindowSettingsCoordinator.cs native/LanFlow.Desktop/Presentation/LauncherDragDropCoordinator.cs native/LanFlow.Desktop/MainWindow.xaml.cs native/LanFlow.Desktop/MainWindow.xaml native/LanFlow.Desktop.Tests/MainWindowArchitectureContractTests.cs
git commit -m "refactor: extract main window UI coordinators"
```

Expected: 全部测试通过，MainWindow 仍是 composition root，但不再实现图标缓存、hover 状态机、透明度计算或重复设置克隆。

---

### Task 5: 建立可复现性能采样与最终回归报告

**Files:**
- Create: `native/LanFlow.Desktop/Diagnostics/PerformanceSample.cs`
- Create: `native/LanFlow.Desktop/Diagnostics/PerformanceSampleCollector.cs`
- Create: `native/LanFlow.Desktop.Tests/PerformanceSampleCollectorTests.cs`
- Modify: `docs/performance/windows-ui-baseline.md`
- Create: `docs/performance/windows-ui-final-report.md`
- Create: `docs/performance/windows-ui-regression-checklist.md`

**Interfaces:**
- Consumes:阶段 1 `UiPerformanceTrace`,阶段 2 realized container count, frame timestamps.
- Produces: P50/P95/P99/max summaries and CSV; final documented regression matrix.

- [ ] **Step 1: 写百分位和 CSV 失败测试**

```csharp
[Fact]
public void Summarize_ReturnsNearestRankPercentiles()
{
    var samples = Enumerable.Range(1, 100).Select(value => (double)value).ToArray();
    var summary = PerformanceSampleCollector.Summarize(samples);
    Assert.Equal(50, summary.P50);
    Assert.Equal(95, summary.P95);
    Assert.Equal(99, summary.P99);
    Assert.Equal(100, summary.Maximum);
}

[Fact]
public void ExportCsv_IncludesEnvironmentAndCacheState()
{
    var csv = PerformanceSampleCollector.ExportCsv([
        new PerformanceSample("selection-ack", 42.5, "warm", "layered", 28)
    ], new PerformanceEnvironment("Windows 11", "CPU", "GPU", "2560x1440", "125%"));
    Assert.Contains("cacheState,transparencyMode,realizedContainers", csv);
    Assert.Contains("warm,layered,28", csv);
}
```

- [ ] **Step 2: 运行测试并确认采样类型不存在**

Run: `dotnet test native\LanFlow.Desktop.Tests\LanFlow.Desktop.Tests.csproj -c Release --filter PerformanceSampleCollectorTests`

Expected: 编译失败，缺少 sample/collector/environment。

- [ ] **Step 3: 实现 nearest-rank 汇总和 CSV 导出**

定义：

```csharp
public sealed record PerformanceSample(string Marker, double ElapsedMs, string CacheState,
    string TransparencyMode, int RealizedContainers);
public sealed record PerformanceEnvironment(string Os, string Cpu, string Gpu, string Resolution, string Scale);
public sealed record PerformanceSummary(double P50, double P95, double P99, double Maximum);
```

`Summarize` 对排序数组使用 nearest-rank `ceil(percentile * count)-1`；空数组抛 `ArgumentException`。CSV 使用 invariant culture 和 RFC 4180 引号转义。

- [ ] **Step 4: 准备 500/100 数据集并执行 Release 基准**

在应用已有导入功能中导入可删除的本地测试清单：10 组 × 50 项，其中当前组 100 项；图标路径混合存在文件、不存在扩展名和重复路径以覆盖缓存。执行：

```powershell
dotnet test native\LanFlow.Core.Tests\LanFlow.Core.Tests.csproj -c Release
dotnet test native\LanFlow.Desktop.Tests\LanFlow.Desktop.Tests.csproj -c Release
dotnet build native\LanFlow.Desktop\LanFlow.Desktop.csproj -c Release
dotnet run --project native\LanFlow.Desktop\LanFlow.Desktop.csproj -c Release
```

每个组合至少采集 30 次切组样本；冷缓存为启动后首次访问，热缓存为同组再次访问；滚动采集至少 20 秒帧间隔。记录 selection-ack、content-stable、帧 P50/P95/P99/max 和 realized container count。

- [ ] **Step 5: 完成最终性能报告**

`windows-ui-final-report.md` 必须包含实际值而非空表：提交 SHA、硬件、OS build、分辨率、缩放、主题、透明模式、缓存冷热、样本数、P50/P95/P99/max、容器数量、是否达成 50/100 ms 目标、滚动长帧分布、剩余瓶颈和复现步骤。若未达目标，记录可复现差距，不得宣称完成。

- [ ] **Step 6: 执行完整回归矩阵并逐项记录结果**

`windows-ui-regression-checklist.md` 逐项记录通过/失败、测试日期 2026-07-30 或实际执行日期、构建 SHA：

- 显示：grid/list/card。
- 导航：top/left；click/hover。
- 透明：layered/wholeWindow × 40/85/100%。
- 动画：system/on/off；Windows 系统动画关闭。
- 缓存：cold/warm/文件更新失效。
- 数据：小数据、100 当前组、500 总计。
- 项目：新增、编辑、删除、启动、单击/双击。
- 分组：新增、重命名、删除、排序、快速切换。
- 拖放：同组、跨组、自动滚动、悬停切组。
- 搜索：输入、清除、搜索中切组、源索引映射。
- 键盘：方向、Tab、焦点可见、Enter 启动。
- 设置：主题、尺寸、标签、透明、动画、预览、应用、取消、未保存关闭、重启恢复。
- 可访问性：长名称 Tooltip/AutomationName、键盘焦点、40% 可读性；浅色、深色、复杂桌面和 Windows 高对比背景。

- [ ] **Step 7: 最终验证并提交报告**

Run:

```powershell
dotnet test native\LanFlow.Core.Tests\LanFlow.Core.Tests.csproj -c Release
dotnet test native\LanFlow.Desktop.Tests\LanFlow.Desktop.Tests.csproj -c Release
dotnet build native\LanFlow.Desktop\LanFlow.Desktop.csproj -c Release
git diff --check
git status --short
git add native/LanFlow.Desktop/Diagnostics/PerformanceSample.cs native/LanFlow.Desktop/Diagnostics/PerformanceSampleCollector.cs native/LanFlow.Desktop.Tests/PerformanceSampleCollectorTests.cs docs/performance/windows-ui-baseline.md docs/performance/windows-ui-final-report.md docs/performance/windows-ui-regression-checklist.md
git commit -m "test: document Windows UI performance and regression"
```

Expected: 两个测试项目 `Failed: 0`，桌面 Release 构建成功；报告包含实测值和完整矩阵；git diff 无空白错误。

---

## 阶段 4 完成门

- [ ] SettingsPreviewSession 的预览、应用、取消和应用后重置基线测试通过。
- [ ] 设置页为左分类/右滚动/固定 footer，全部旧设置和新增设置都在覆盖映射中。
- [ ] 所有连续视觉滑块预览 33 ms 节流，不在拖动时写磁盘；双透明模式独立值与恢复 85% 正确。
- [ ] 未保存关闭的应用并关闭、放弃、返回设置三条路径正确。
- [ ] MainWindow 中旧后置尺寸、全量导航、同步图标、重复 clone 和透明度计算路径已删除。
- [ ] Release 500/100 基准报告包含 P50/P95/P99/max、容器数和环境信息。
- [ ] 完整功能、性能、透明度、动画、缓存、拖放、键盘和可访问性矩阵均有实际记录。
- [ ] 只有在自动化测试、构建、性能目标和人工回归均通过或明确记录剩余差距时，才宣告本项目完成。
