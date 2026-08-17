# LanFlow 阶段 2：导航与虚拟化 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 用容器回收虚拟化支撑网格和卡片布局，按真实视口调度图标，并把顶部/左侧分组导航统一为单一数据驱动控件，同时保证点击、悬停、键盘和拖放行为完整。

**Architecture:** 自定义 `VirtualizingWrapPanel` 只负责布局、滚动和逻辑索引映射，纯数学部分放入可测试的 `VirtualizingWrapLayout`。`GroupNavigationControl` 绑定同一组集合并通过模板切换顶部/左侧呈现；`GroupSwitchCoordinator` 统一点击、200 ms 悬停意图和拖拽悬停，但把普通浏览与拖拽状态分开。

**Tech Stack:** .NET 8、WPF `VirtualizingPanel`/`IScrollInfo`、XAML、xUnit、`DispatcherTimer`、阶段 1 的 `IIconService`。

## Global Constraints

- 网格和卡片必须只生成视口及缓冲区需要的容器并启用回收；列表继续使用 WPF 虚拟化。
- 不能退回“全部生成，只优化图标”的实现。
- 必须保留键盘方向导航、单击选择、双击启动、同组排序、跨组拖放、边缘自动滚动和搜索索引映射。
- 顶部和左侧导航共享一个数据源和选中状态；任一时刻只存在一套导航视觉，不得出现右侧导航。
- 普通切组不得清空并重建全部分组按钮；新增、删除、重命名和排序通过集合变更差量反映。
- 点击为默认；悬停为可选，意图延迟固定 200 ms，只执行最后一个仍有效目标。
- 已选中组不得重复加载；快速切组的过期内容和图标不得覆盖当前状态。
- 标签大小 28–52 DIP、字体 11–18 DIP、左侧宽度 96–280 DIP；顶部单项最大宽度 180 DIP。
- 长名称使用省略号、工具提示和可访问名称保留全文。
- 拖拽预览轻量且完全不透明，不得同步提取图标或复制复杂项目模板。
- 每个任务先失败测试，再最小实现，再完整回归并提交。

---

### Task 1: 实现可测试的虚拟换行布局数学

**Files:**
- Create: `native/LanFlow.Desktop/Controls/ViewportRange.cs`
- Create: `native/LanFlow.Desktop/Controls/VirtualizingWrapLayout.cs`
- Create: `native/LanFlow.Desktop.Tests/VirtualizingWrapLayoutTests.cs`

**Interfaces:**
- Consumes: item count、viewport width/height、horizontal/vertical offset、item width/height、item/row spacing、buffer rows.
- Produces: `ViewportRange`, extent、index-to-rect、point-to-index 和方向导航索引；后续 `VirtualizingWrapPanel` 不重复布局公式。

- [ ] **Step 1: 写可见范围、坐标映射和方向导航失败测试**

```csharp
using System.Windows;
using LanFlow.Desktop.Controls;

namespace LanFlow.Desktop.Tests;

public sealed class VirtualizingWrapLayoutTests
{
    private static readonly VirtualizingWrapLayout Layout = new(
        itemWidth: 100, itemHeight: 80, horizontalSpacing: 8, verticalSpacing: 10, bufferRows: 1);

    [Fact]
    public void CalculateRange_IncludesOneBufferRowAroundViewport()
    {
        var range = Layout.CalculateRange(itemCount: 100, viewportWidth: 440, viewportHeight: 180, verticalOffset: 180);
        Assert.Equal(4, range.Columns);
        Assert.Equal(4, range.FirstIndex);
        Assert.Equal(23, range.LastIndex);
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(3, 3, 0)]
    [InlineData(4, 0, 90)]
    public void GetItemRect_MapsLogicalIndexToStableCoordinates(int index, double x, double y)
    {
        Assert.Equal(new Rect(x, y, 100, 80), Layout.GetItemRect(index, columns: 4));
    }

    [Theory]
    [InlineData(5, NavigationDirection.Left, 4)]
    [InlineData(5, NavigationDirection.Right, 6)]
    [InlineData(5, NavigationDirection.Up, 1)]
    [InlineData(5, NavigationDirection.Down, 9)]
    public void MoveIndex_UsesLogicalGridRatherThanRealizedContainers(int index, NavigationDirection direction, int expected)
    {
        Assert.Equal(expected, Layout.MoveIndex(index, direction, itemCount: 20, columns: 4));
    }
}
```

- [ ] **Step 2: 运行测试并确认布局类型尚不存在**

Run: `dotnet test native\LanFlow.Desktop.Tests\LanFlow.Desktop.Tests.csproj -c Release --filter VirtualizingWrapLayoutTests`

Expected: 编译失败，包含 `VirtualizingWrapLayout`、`ViewportRange` 或 `NavigationDirection` 不存在。

- [ ] **Step 3: 实现纯布局值类型和边界规则**

```csharp
namespace LanFlow.Desktop.Controls;

public readonly record struct ViewportRange(int FirstIndex, int LastIndex, int Columns)
{
    public static ViewportRange Empty => new(-1, -1, 1);
    public bool Contains(int index) => index >= FirstIndex && index <= LastIndex;
}

public enum NavigationDirection { Left, Right, Up, Down }
```

`VirtualizingWrapLayout` 规则：列数 `Math.Max(1, floor((viewportWidth + spacing) / (itemWidth + spacing)))`；首尾行按 vertical offset 计算并各扩展一行；范围钳制到 `0..itemCount-1`；空集合返回 `ViewportRange.Empty`。提供精确签名：

```csharp
public sealed class VirtualizingWrapLayout
{
    public VirtualizingWrapLayout(double itemWidth, double itemHeight,
        double horizontalSpacing, double verticalSpacing, int bufferRows);

    public Size CalculateExtent(int itemCount, double viewportWidth);
    public ViewportRange CalculateRange(int itemCount, double viewportWidth,
        double viewportHeight, double verticalOffset);
    public Rect GetItemRect(int index, int columns);
    public int IndexFromPoint(Point point, int itemCount, int columns);
    public int MoveIndex(int index, NavigationDirection direction, int itemCount, int columns);
}
```

`IndexFromPoint` 对间距区返回最近合法项并钳制索引；最后一行不完整时方向移动不得返回超出项数的索引。

- [ ] **Step 4: 运行布局测试并提交**

Run:

```powershell
dotnet test native\LanFlow.Desktop.Tests\LanFlow.Desktop.Tests.csproj -c Release --filter VirtualizingWrapLayoutTests
git diff --check
git add native/LanFlow.Desktop/Controls/ViewportRange.cs native/LanFlow.Desktop/Controls/VirtualizingWrapLayout.cs native/LanFlow.Desktop.Tests/VirtualizingWrapLayoutTests.cs
git commit -m "feat: add virtual wrap layout geometry"
```

Expected: 筛选测试全部通过，空集合、窄视口和最后一行边界无异常。

---

### Task 2: 实现回收式 VirtualizingWrapPanel 并接入三种显示模式

**Files:**
- Create: `native/LanFlow.Desktop/Controls/VirtualizingWrapPanel.cs`
- Modify: `native/LanFlow.Desktop/MainWindow.xaml`
- Modify: `native/LanFlow.Desktop/MainWindow.xaml.cs`
- Create: `native/LanFlow.Desktop.Tests/VirtualizingWrapPanelContractTests.cs`

**Interfaces:**
- Consumes: Task 1 `VirtualizingWrapLayout`; `ListBox` item generator; settings item dimensions.
- Produces: `VirtualizingWrapPanel : VirtualizingPanel, IScrollInfo`; read-only `RealizedRange` and `RealizedIndices`; `ViewportChanged` event for icon scheduling；按 `groupId + layoutMode` 保存的选择、焦点与滚动快照。

- [ ] **Step 1: 写面板契约和 XAML 配置失败测试**

契约测试不创建真实窗口，只验证类型与依赖属性：

```csharp
[Fact]
public void Panel_ExposesRequiredLayoutAndViewportContract()
{
    Assert.True(typeof(VirtualizingPanel).IsAssignableFrom(typeof(VirtualizingWrapPanel)));
    Assert.True(typeof(IScrollInfo).IsAssignableFrom(typeof(VirtualizingWrapPanel)));
    Assert.NotNull(VirtualizingWrapPanel.ItemWidthProperty);
    Assert.NotNull(VirtualizingWrapPanel.ItemHeightProperty);
    Assert.NotNull(typeof(VirtualizingWrapPanel).GetEvent(nameof(VirtualizingWrapPanel.ViewportChanged)));
}
```

另写读取 `MainWindow.xaml` 的测试，断言含 `VirtualizingWrapPanel`、`VirtualizationMode="Recycling"`，且不含普通 `<WrapPanel`。

- [ ] **Step 2: 运行契约测试并确认失败**

Run: `dotnet test native\LanFlow.Desktop.Tests\LanFlow.Desktop.Tests.csproj -c Release --filter VirtualizingWrapPanelContractTests`

Expected: 类型不存在或 XAML 仍包含普通 `WrapPanel`。

- [ ] **Step 3: 实现 Measure/Arrange、回收和 IScrollInfo**

面板公开依赖属性：

```csharp
public static readonly DependencyProperty ItemWidthProperty;
public static readonly DependencyProperty ItemHeightProperty;
public static readonly DependencyProperty HorizontalSpacingProperty;
public static readonly DependencyProperty VerticalSpacingProperty;
public static readonly DependencyProperty BufferRowsProperty;

public ViewportRange RealizedRange { get; private set; } = ViewportRange.Empty;
public IReadOnlyList<int> RealizedIndices => _realizedIndices;
public event EventHandler<ViewportRange>? ViewportChanged;
```

`MeasureOverride` 必须：

1. 用 `ItemsControl.GetItemsOwner(this)` 和 `IItemContainerGenerator` 获取项目数。
2. 用 `VirtualizingWrapLayout.CalculateRange` 计算目标逻辑范围。
3. `generator.StartAt(..., GeneratorDirection.Forward, true)` 只生成目标范围。
4. 新容器执行 `AddInternalChild` 与 `generator.PrepareItemContainer`。
5. 超出范围的已实现容器按倒序调用 `generator.Remove` 和 `RemoveInternalChildRange`，允许 Recycling 模式复用。
6. 只测量固定 `ItemWidth × ItemHeight`，不允许子项改变布局几何。
7. extent/viewport/offset 变化时调用 `ScrollOwner?.InvalidateScrollInfo()` 并仅在范围变化时触发 `ViewportChanged`。

`ArrangeOverride` 使用 Task 1 的 `GetItemRect(logicalIndex, columns)`，减去垂直偏移后安排容器。`IScrollInfo` 实现行滚动、页滚动、鼠标滚轮、`MakeVisible` 和 offset 钳制。

- [ ] **Step 4: 在 XAML 中按模式切换虚拟面板参数**

`ListBox` 必须设置：

```xml
<ListBox x:Name="ItemList"
         ItemsSource="{Binding VisibleItems}"
         ScrollViewer.CanContentScroll="True"
         VirtualizingPanel.IsVirtualizing="True"
         VirtualizingPanel.VirtualizationMode="Recycling">
    <ListBox.ItemsPanel>
        <ItemsPanelTemplate>
            <controls:VirtualizingWrapPanel
                ItemWidth="{Binding DataContext.Settings.CardWidth, RelativeSource={RelativeSource AncestorType=ListBox}}"
                ItemHeight="{Binding DataContext.Settings.CardHeight, RelativeSource={RelativeSource AncestorType=ListBox}}"
                HorizontalSpacing="{Binding DataContext.Settings.ItemSpacing, RelativeSource={RelativeSource AncestorType=ListBox}}"
                VerticalSpacing="{Binding DataContext.Settings.RowSpacing, RelativeSource={RelativeSource AncestorType=ListBox}}"
                BufferRows="1" />
        </ItemsPanelTemplate>
    </ListBox.ItemsPanel>
</ListBox>
```

网格和卡片使用该面板；列表模式切换到 `VirtualizingStackPanel`。模式切换只换 `ItemsPanelTemplate` 和项目模板，不替换 `ItemsSource` 或创建第二个列表控件。

- [ ] **Step 5: 接回键盘导航并定义选择、焦点和滚动保留策略**

在 `PreviewKeyDown` 中按当前面板类型处理：虚拟换行模式用 `MoveIndex` 得到逻辑索引，设置 `SelectedIndex` 后调用 `ScrollIntoView(item)`；列表模式保留上下导航。Enter 启动当前项，Space 只选择，不让回收容器决定逻辑索引。

新增内部 `ViewStateSnapshot(string? SelectedItemId, string? FocusedItemId, double VerticalOffset)`，以 `groupId + layoutMode` 为键保存状态。规则必须固定为：同组搜索、导入或项目更新时，若稳定项目 ID 仍存在则恢复选中项和键盘焦点，并把旧 offset 钳制到新 extent；切到其他组前保存当前快照，返回该组且显示模式相同时恢复；显示模式改变时保留仍存在的选中项，但滚动回到使该项可见的位置，不直接复用另一模式的 offset。自动恢复只遍历逻辑集合和当前已实现容器，不生成屏幕外容器。

- [ ] **Step 6: 运行测试、构建和 100 项容器烟雾验证并提交**

Run:

```powershell
dotnet test native\LanFlow.Desktop.Tests\LanFlow.Desktop.Tests.csproj -c Release --filter "VirtualizingWrapLayoutTests|VirtualizingWrapPanelContractTests"
dotnet build native\LanFlow.Desktop\LanFlow.Desktop.csproj -c Release
git diff --check
git add native/LanFlow.Desktop/Controls/VirtualizingWrapPanel.cs native/LanFlow.Desktop/MainWindow.xaml native/LanFlow.Desktop/MainWindow.xaml.cs native/LanFlow.Desktop.Tests/VirtualizingWrapPanelContractTests.cs
git commit -m "perf: virtualize grid and card layouts"
```

Expected: 构建成功；100 项当前组在首屏仅实现首屏加一行缓冲容器；切到列表仍启用 Recycling；方向键可穿过未实现项并滚入视口。

---

### Task 3: 按视口、缓冲区和空闲区调度图标

**Files:**
- Create: `native/LanFlow.Desktop/Presentation/ViewportIconCoordinator.cs`
- Modify: `native/LanFlow.Desktop/MainWindow.xaml.cs`
- Create: `native/LanFlow.Desktop.Tests/ViewportIconCoordinatorTests.cs`

**Interfaces:**
- Consumes: `ViewportRange`, stable `VisibleItems`, Task 1 `IIconService`.
- Produces: `ViewportIconCoordinator.RefreshAsync(IReadOnlyList<LauncherItem>, ViewportRange, int, string, CancellationToken)`；`PreheatAsync(IReadOnlyList<Group>, string?, int, string, CancellationToken)`；priority mapping and stale-result protection.

- [ ] **Step 1: 写优先级分区和最后请求获胜失败测试**

```csharp
[Fact]
public void BuildRequests_AssignsViewportBufferAndIdlePriorities()
{
    var requests = ViewportIconCoordinator.BuildRequests(itemCount: 20,
        viewport: new ViewportRange(5, 9, 5), bufferItemCount: 5);

    Assert.All(requests.Where(r => r.Index is >= 5 and <= 9), r => Assert.Equal(IconLoadPriority.Viewport, r.Priority));
    Assert.All(requests.Where(r => r.Index is >= 0 and <= 4 or >= 10 and <= 14), r => Assert.Equal(IconLoadPriority.Buffer, r.Priority));
    Assert.All(requests.Where(r => r.Index >= 15), r => Assert.Equal(IconLoadPriority.Idle, r.Priority));
}

[Fact]
public async Task RefreshAsync_DoesNotApplyResultsFromPreviousGeneration()
{
    var icons = new ControlledIconService();
    var coordinator = new ViewportIconCoordinator(icons);
    var item = new LauncherItem { Path = "old.exe" };

    var oldRefresh = coordinator.RefreshAsync([item], new ViewportRange(0, 0, 1), 48, "dark", default);
    item.Path = "new.exe";
    var newRefresh = coordinator.RefreshAsync([item], new ViewportRange(0, 0, 1), 48, "dark", default);
    icons.Complete("new.exe", NewImage());
    icons.Complete("old.exe", OldImage());
    await Task.WhenAll(oldRefresh, newRefresh);

    Assert.Same(icons.NewImage, item.IconImage);
}
```

- [ ] **Step 2: 运行测试并确认协调器不存在**

Run: `dotnet test native\LanFlow.Desktop.Tests\LanFlow.Desktop.Tests.csproj -c Release --filter ViewportIconCoordinatorTests`

Expected: 编译失败，`ViewportIconCoordinator` 不存在。

- [ ] **Step 3: 实现视口请求协调器**

固定请求值类型：

```csharp
public readonly record struct IconViewportRequest(int Index, IconLoadPriority Priority);
```

`BuildRequests` 先返回视口、再返回上下各一视口长度的缓冲区、最后返回当前组其余项；每个索引只出现一次。`RefreshAsync` 每次递增 generation 并取消旧低优先级批次；写回前同时检查 generation、`LauncherItem.IconRequestVersion`、路径、像素尺寸和主题变体。`PreheatAsync` 只选择最近访问组和当前组前后各一个相邻组，全部以 Idle 优先级提交；只在 `ShellIconService` 暴露的高/中优先级等待计数均为 0 时开始，并在任一新 Viewport/Buffer 请求到达时取消剩余预热。

- [ ] **Step 4: 将 VirtualizingWrapPanel.ViewportChanged 接到协调器**

主窗口在面板 `Loaded` 后订阅一次 `ViewportChanged`，关闭时解除。滚动、窗口尺寸、显示模式、搜索、组和图标尺寸变化时调用 `RefreshAsync`；普通滚动不得重新请求已缓存键。列表模式用 `ItemContainerGenerator` 的首尾可见容器计算范围，但只遍历当前已实现容器，不遍历全部项目。

- [ ] **Step 5: 运行测试和快速滚动/切组验证并提交**

Run:

```powershell
dotnet test native\LanFlow.Desktop.Tests\LanFlow.Desktop.Tests.csproj -c Release --filter ViewportIconCoordinatorTests
dotnet build native\LanFlow.Desktop\LanFlow.Desktop.csproj -c Release
git diff --check
git add native/LanFlow.Desktop/Presentation/ViewportIconCoordinator.cs native/LanFlow.Desktop/MainWindow.xaml.cs native/LanFlow.Desktop.Tests/ViewportIconCoordinatorTests.cs
git commit -m "perf: prioritize icons by viewport"
```

Expected: 测试通过；快速滚动后新视口先显示；快速切组三次不会出现旧组图标串入；没有同步 Shell 调用。

---

### Task 4: 用单一数据驱动控件替换全量重建分组按钮

**Files:**
- Create: `native/LanFlow.Desktop/Controls/GroupNavigationControl.xaml`
- Create: `native/LanFlow.Desktop/Controls/GroupNavigationControl.xaml.cs`
- Modify: `native/LanFlow.Desktop/MainWindow.xaml`
- Modify: `native/LanFlow.Desktop/MainWindow.xaml.cs`
- Create: `native/LanFlow.Desktop.Tests/GroupNavigationContractTests.cs`

**Interfaces:**
- Consumes: `ObservableCollection<Group> Groups`, `SelectedGroup`, settings `GroupLayout`, `GroupLabelSize`, `GroupLabelFontSize`, `GroupNavigationWidth`.
- Produces: one `GroupNavigationControl`; routed events `GroupInvoked`, `GroupHovered`, `GroupDragHovered`, `GroupDropped`; no `RefreshGroupTabs()`.

- [ ] **Step 1: 写单实例导航和资源边界失败测试**

测试读取 `MainWindow.xaml`，断言 `GroupNavigationControl` 恰好出现一次；读取生产代码，断言不含 `RefreshGroupTabs` 或 `GroupTabs.Children.Clear`；反射测试控件拥有上述四个 routed event 和 `ItemsSourceProperty`/`SelectedItemProperty`。

- [ ] **Step 2: 运行测试并确认旧导航实现失败**

Run: `dotnet test native\LanFlow.Desktop.Tests\LanFlow.Desktop.Tests.csproj -c Release --filter GroupNavigationContractTests`

Expected: 类型不存在，且旧字符串断言失败。

- [ ] **Step 3: 实现顶部/左侧共享 ItemsControl 的导航控件**

控件只创建一个 `ListBox`，通过 `GroupLayout` 切换 ItemsPanel：顶部为横向虚拟化/滚动面板，左侧为纵向 `VirtualizingStackPanel`。项目模板核心：

```xml
<TextBlock Text="{Binding Name}"
           FontSize="{Binding RelativeSource={RelativeSource AncestorType=local:GroupNavigationControl}, Path=GroupLabelFontSize}"
           TextTrimming="CharacterEllipsis"
           ToolTip="{Binding Name}"
           AutomationProperties.Name="{Binding Name}" />
```

容器高度绑定 `GroupLabelSize`，顶部 `MaxWidth="180"`；左侧可用宽度绑定 `GroupNavigationWidth` 并扣除控件内边距。点击、鼠标进入、拖拽进入和 Drop 只抛出事件，不直接操作 `MainViewModel`。

- [ ] **Step 4: 在 MainWindow 中替换两套占位和按钮生成逻辑**

`MainWindow.xaml` 只保留：

```xml
<controls:GroupNavigationControl x:Name="GroupNavigation"
    ItemsSource="{Binding Groups}"
    SelectedItem="{Binding SelectedGroup, Mode=TwoWay}"
    GroupLayout="{Binding Settings.GroupLayout}"
    GroupLabelSize="{Binding Settings.GroupLabelSize}"
    GroupLabelFontSize="{Binding Settings.GroupLabelFontSize}"
    GroupNavigationWidth="{Binding Settings.GroupNavigationWidth}" />
```

删除 `GroupTabs`/`TopGroupTabs` 双容器及 `RefreshGroupTabs()`。新增、删除、重命名、排序继续修改原 `ObservableCollection<Group>`，由 WPF 差量更新容器。切换顶部/左侧时不得在视觉树中同时保留两个可见导航控件。

- [ ] **Step 5: 运行测试并验证标签边界后提交**

Run:

```powershell
dotnet test native\LanFlow.Desktop.Tests\LanFlow.Desktop.Tests.csproj -c Release --filter GroupNavigationContractTests
dotnet build native\LanFlow.Desktop\LanFlow.Desktop.csproj -c Release
git diff --check
git add native/LanFlow.Desktop/Controls/GroupNavigationControl.xaml native/LanFlow.Desktop/Controls/GroupNavigationControl.xaml.cs native/LanFlow.Desktop/MainWindow.xaml native/LanFlow.Desktop/MainWindow.xaml.cs native/LanFlow.Desktop.Tests/GroupNavigationContractTests.cs
git commit -m "perf: reuse one group navigation control"
```

Expected: 构建成功；顶部和左侧切换时只有一个导航；长名省略且工具提示完整；普通切组不清空按钮。

---

### Task 5: 统一点击、悬停意图和拖拽切组状态机

**Files:**
- Create: `native/LanFlow.Desktop/Presentation/GroupSwitchCoordinator.cs`
- Modify: `native/LanFlow.Desktop/MainWindow.xaml.cs`
- Modify: `native/LanFlow.Desktop/Controls/GroupNavigationControl.xaml.cs`
- Create: `native/LanFlow.Desktop.Tests/GroupSwitchCoordinatorTests.cs`

**Interfaces:**
- Consumes: Task 4 navigation events; setting `GroupSwitchMode`; `Group` stable IDs.
- Produces: `RequestClick(Group)`, `BeginHover(Group)`, `CancelHover(Group)`, `BeginDragHover(Group)`, `EndDrag()`; event `SwitchRequested(Group, GroupSwitchReason, long generation)`.

- [ ] **Step 1: 写 200 ms 意图、合并、已选组和拖拽隔离失败测试**

通过注入 `ITimerScheduler` 的虚拟时钟测试：199 ms 不切换、200 ms 触发；A→B 快速悬停只触发 B；离开取消；已选组无事件；普通悬停取消不会取消独立的拖拽悬停；点击立即触发并使旧 hover generation 失效。

核心用例：

```csharp
[Fact]
public void Hover_OnlyLatestTargetFiresAfterTwoHundredMilliseconds()
{
    var clock = new ManualTimerScheduler();
    var coordinator = new GroupSwitchCoordinator(clock, TimeSpan.FromMilliseconds(200));
    var fired = new List<Group>();
    coordinator.SwitchRequested += (_, e) => fired.Add(e.Group);

    coordinator.BeginHover(Group("A"));
    clock.AdvanceBy(TimeSpan.FromMilliseconds(100));
    coordinator.BeginHover(Group("B"));
    clock.AdvanceBy(TimeSpan.FromMilliseconds(199));
    Assert.Empty(fired);
    clock.AdvanceBy(TimeSpan.FromMilliseconds(1));
    Assert.Equal("B", Assert.Single(fired).Id);
}
```

- [ ] **Step 2: 运行测试并确认状态机不存在**

Run: `dotnet test native\LanFlow.Desktop.Tests\LanFlow.Desktop.Tests.csproj -c Release --filter GroupSwitchCoordinatorTests`

Expected: 编译失败，缺少协调器和调度器类型。

- [ ] **Step 3: 实现可取消的代际状态机**

定义：

```csharp
public enum GroupSwitchReason { Click, Hover, DragHover }
public sealed record GroupSwitchRequestedEventArgs(Group Group, GroupSwitchReason Reason, long Generation);

public interface ITimerScheduler
{
    IDisposable Schedule(TimeSpan delay, Action action);
}
```

`GroupSwitchCoordinator` 保存普通 hover 与 drag hover 两个独立 disposable；每个新请求递增 generation；回调触发前比较 generation 和目标 ID；`SelectedGroupId` 相同则跳过。生产调度器使用 UI `DispatcherTimer`。

- [ ] **Step 4: 接入导航事件、选中先反馈和最新请求提交**

点击设置为 click 时立即请求；hover 设置为 hover 时才调用 `BeginHover`；拖拽始终使用独立的 `BeginDragHover` 以支持跨组拖放。收到 `SwitchRequested` 时先更新 `SelectedGroup` 与导航选中态并记录 `selection-ack`，再启动可见集合和图标刷新；异步完成前再次比较 generation。

- [ ] **Step 5: 恢复虚拟化下的拖放索引、自动滚动和轻量 ghost**

命中测试必须通过 `VirtualizingWrapLayout.IndexFromPoint` 计算逻辑插入索引，不依赖容器数量。边缘 32 DIP 区域按固定 16 DIP 步长滚动；Drop 时把逻辑索引映射到过滤前源集合，搜索状态下禁止不明确的排序但允许启动和选择。拖拽 ghost 使用当前 `IconImage`、名称和固定尺寸边框，`Opacity=1`，不克隆完整模板。

- [ ] **Step 6: 运行全部 Desktop 测试、构建和交互矩阵后提交**

Run:

```powershell
dotnet test native\LanFlow.Desktop.Tests\LanFlow.Desktop.Tests.csproj -c Release
dotnet test native\LanFlow.Core.Tests\LanFlow.Core.Tests.csproj -c Release
dotnet build native\LanFlow.Desktop\LanFlow.Desktop.csproj -c Release
git diff --check
git add native/LanFlow.Desktop/Presentation/GroupSwitchCoordinator.cs native/LanFlow.Desktop/MainWindow.xaml.cs native/LanFlow.Desktop/Controls/GroupNavigationControl.xaml.cs native/LanFlow.Desktop.Tests/GroupSwitchCoordinatorTests.cs
git commit -m "feat: coordinate click hover and drag group switching"
```

Expected: 所有测试通过；点击立即反馈；悬停 200 ms；快速划过只切最后组；同组/跨组拖放、边缘滚动、键盘方向导航和搜索均无回归。

---

## 阶段 2 完成门

- [ ] 网格和卡片的实现容器数量随视口而非总项目数增长；列表保持 Recycling。
- [ ] 当前视口图标优先于缓冲区和 Idle；相邻/最近组只在空闲时有限预热；滚动/切组后过期结果不写回。
- [ ] 同组刷新尽量保留稳定 ID 对应的选中项、键盘焦点和钳制后的滚动位置；跨模式不错误复用 offset。
- [ ] 主窗口只包含一套分组导航，顶部/左侧共享数据源和选中状态，无右侧栏。
- [ ] 标签尺寸、字体、左侧宽度和长名称规则全部生效。
- [ ] 点击、悬停 200 ms、快速合并、拖拽悬停和已选组跳过测试通过。
- [ ] 键盘、启动、搜索、同组排序、跨组拖放和边缘自动滚动在虚拟化下通过人工回归。
