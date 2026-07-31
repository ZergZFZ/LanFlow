# LanFlow Phase 3 Modeless Settings Window Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把设置窗口改为可与主页同时操作的单实例非模态窗口，并在当前显示器工作区内优先与主页并排。

**Architecture:** 新增不依赖窗口句柄的纯定位类，单元测试覆盖右侧、左侧和工作区限制。`MainWindow` 持有设置窗口和预览会话，在窗口关闭事件中执行现有 `SettingsCloseFlow` 结果，不再依赖 `ShowDialog()` 返回值。

**Tech Stack:** WPF Window、System.Windows.Rect/Size/Point、xUnit、现有 SettingsPreviewSession/MainWindowSettingsCoordinator。

## Global Constraints

- 任意时刻最多一个设置窗口。
- 设置窗口使用 `Show()`；仅未保存更改确认小窗口仍可使用 `ShowDialog()`。
- 主窗口和设置窗口均保持可操作。
- 默认间距 12 个 WPF 设备无关像素。
- 定位顺序：右侧、左侧、限制到工作区。
- 应用、放弃、继续编辑语义保持不变。
- 主窗口退出时不得遗留设置窗口或事件订阅。

---

### Task 1: 创建并排定位纯函数

**Files:**
- Create: `native/LanFlow.Desktop/Presentation/SettingsWindowPlacement.cs`
- Create: `native/LanFlow.Desktop.Tests/SettingsWindowPlacementTests.cs`

**Interfaces:**
- Consumes: `Rect ownerBounds`, `Size settingsSize`, `Rect workArea`, `double gap`。
- Produces: `Point SettingsWindowPlacement.Calculate(...)`。

- [ ] **Step 1: 写右侧定位失败测试**

```csharp
using System.Windows;
using LanFlow.Desktop.Presentation;

namespace LanFlow.Desktop.Tests;

public sealed class SettingsWindowPlacementTests
{
    [Fact]
    public void Calculate_PlacesSettingsToTheRightWhenItFits()
    {
        var result = SettingsWindowPlacement.Calculate(
            new Rect(100, 100, 760, 550),
            new Size(900, 720),
            new Rect(0, 0, 2560, 1440),
            gap: 12);

        Assert.Equal(new Point(872, 100), result);
    }
}
```

- [ ] **Step 2: 运行测试确认类型不存在**

```powershell
dotnet test native\LanFlow.Desktop.Tests\LanFlow.Desktop.Tests.csproj -c Release --filter SettingsWindowPlacementTests
```

Expected：编译失败，`SettingsWindowPlacement` 不存在。

- [ ] **Step 3: 添加最小定位实现**

```csharp
using System;
using System.Windows;

namespace LanFlow.Desktop.Presentation;

public static class SettingsWindowPlacement
{
    public const double DefaultGap = 12;

    public static Point Calculate(Rect ownerBounds, Size settingsSize, Rect workArea, double gap = DefaultGap)
    {
        if (settingsSize.Width <= 0 || settingsSize.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(settingsSize));
        }

        if (gap < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(gap));
        }

        double right = ownerBounds.Right + gap;
        double left = ownerBounds.Left - gap - settingsSize.Width;
        double top = Clamp(ownerBounds.Top, workArea.Top, workArea.Bottom - settingsSize.Height);

        if (right + settingsSize.Width <= workArea.Right)
        {
            return new Point(right, top);
        }

        if (left >= workArea.Left)
        {
            return new Point(left, top);
        }

        return new Point(
            Clamp(ownerBounds.Left, workArea.Left, workArea.Right - settingsSize.Width),
            top);
    }

    private static double Clamp(double value, double minimum, double maximum) =>
        maximum < minimum ? minimum : Math.Clamp(value, minimum, maximum);
}
```

- [ ] **Step 4: 增加左侧和工作区测试**

```csharp
[Fact]
public void Calculate_PlacesSettingsToTheLeftWhenRightDoesNotFit()
{
    var result = SettingsWindowPlacement.Calculate(
        new Rect(1700, 100, 760, 550),
        new Size(900, 720),
        new Rect(0, 0, 2560, 1400));

    Assert.Equal(new Point(788, 100), result);
}

[Fact]
public void Calculate_ClampsWindowIntoWorkAreaWhenNeitherSideFits()
{
    var result = SettingsWindowPlacement.Calculate(
        new Rect(400, 900, 760, 550),
        new Size(900, 720),
        new Rect(0, 0, 1600, 1000));

    Assert.Equal(new Point(400, 280), result);
}
```

- [ ] **Step 5: 运行定位测试**

```powershell
dotnet test native\LanFlow.Desktop.Tests\LanFlow.Desktop.Tests.csproj -c Release --filter SettingsWindowPlacementTests
```

Expected：0 failed。

### Task 2: 锁定非模态和单实例架构

**Files:**
- Modify: `native/LanFlow.Desktop.Tests/MainWindowArchitectureContractTests.cs`
- Modify: `native/LanFlow.Desktop.Tests/SettingsWindowContractTests.cs`

**Interfaces:**
- Consumes: `MainWindow.xaml.cs` 和 `SettingsWindow.xaml` 文本合同。
- Produces: `_settingsWindow` 单实例字段、`Show()`、Manual startup location。

- [ ] **Step 1: 增加架构合同测试**

```csharp
[Fact]
public void MainWindow_OpensOneModelessSettingsWindow()
{
    var code = File.ReadAllText(GetDesktopPath("MainWindow.xaml.cs"));

    Assert.Contains("private SettingsWindow? _settingsWindow", code);
    Assert.Contains("window.Show();", code);
    Assert.Contains("_settingsWindow.Activate();", code);
    Assert.DoesNotContain("settingsWindow.ShowDialog()", code);
    Assert.Contains("SettingsWindowPlacement.Calculate", code);
}
```

设置窗口合同增加：

```csharp
Assert.Contains("WindowStartupLocation=\"Manual\"", xaml);
```

- [ ] **Step 2: 运行合同测试确认失败**

```powershell
dotnet test native\LanFlow.Desktop.Tests\LanFlow.Desktop.Tests.csproj -c Release --filter "MainWindow_OpensOneModelessSettingsWindow|SettingsWindowContractTests"
```

Expected：失败，当前仍使用 `ShowDialog()` 和 `CenterOwner`。

### Task 3: 改造 MainWindow 设置窗口生命周期

**Files:**
- Create: `native/LanFlow.Desktop/Presentation/MonitorWorkAreaProvider.cs`
- Modify: `native/LanFlow.Desktop/MainWindow.xaml.cs`
- Modify: `native/LanFlow.Desktop/Views/SettingsWindow.xaml`
- Modify: `native/LanFlow.Desktop/Views/SettingsWindow.xaml.cs`
- Modify: `native/LanFlow.Desktop.Tests/MainWindowArchitectureContractTests.cs`
- Modify: `native/LanFlow.Desktop.Tests/SettingsWindowContractTests.cs`

**Interfaces:**
- Consumes: `SettingsPreviewSession`, `MainWindowSettingsCoordinator`, `SettingsCloseFlow.TryComplete(SettingsPreviewSession, UnsavedCloseDecision, Action, Func<Settings, Settings>)`、`SettingsWindow.CloseDecision`。
- Produces: `OpenOrActivateSettingsWindow()`、`MonitorWorkAreaProvider.GetForWindow(Window)`、单实例窗口上下文和完整关闭清理流程。

- [ ] **Step 1: 添加主窗口单实例上下文和失败合同**

在 `MainWindowArchitectureContractTests` 增加：

```csharp
Assert.Contains("private SettingsWindow? _settingsWindow", code);
Assert.Contains("private SettingsWindowContext? _settingsWindowContext", code);
Assert.Contains("OpenOrActivateSettingsWindow();", code);
Assert.Contains("window.Show();", code);
Assert.DoesNotContain("settingsWindow.ShowDialog()", code);
Assert.Contains("MonitorWorkAreaProvider.GetForWindow(this)", code);
```

在 `SettingsWindowContractTests` 增加：

```csharp
Assert.Contains("WindowStartupLocation=\"Manual\"", xaml);
Assert.Contains("ShowInTaskbar=\"False\"", xaml);
Assert.Contains("CloseForOwnerShutdown", code);
```

运行：

```powershell
dotnet test native\LanFlow.Desktop.Tests\LanFlow.Desktop.Tests.csproj -c Release --filter "MainWindowArchitectureContractTests|SettingsWindowContractTests"
```

Expected：失败，因为当前仍通过 `ShowDialog()` 打开主设置窗口，且没有单实例字段和显示器工作区提供器。

- [ ] **Step 2: 添加窗口上下文并改为打开或激活**

在 `MainWindow` 中增加：

```csharp
private sealed record SettingsWindowContext(
    SettingsPreviewSession Session,
    Settings Original,
    bool WasEditMode);

private SettingsWindow? _settingsWindow;
private SettingsWindowContext? _settingsWindowContext;
```

`OpenSettings_Click` 只调用：

```csharp
private void OpenSettings_Click(object sender, RoutedEventArgs e) =>
    OpenOrActivateSettingsWindow();
```

新增：

```csharp
private void OpenOrActivateSettingsWindow()
{
    if (_settingsWindow is not null)
    {
        if (_settingsWindow.WindowState == WindowState.Minimized)
        {
            _settingsWindow.WindowState = WindowState.Normal;
        }

        _settingsWindow.Activate();
        return;
    }

    var session = new SettingsPreviewSession(_viewModel.Settings);
    session.PreviewRequested += SettingsSession_PreviewRequested;

    bool wasEditMode = _isEditMode;
    SetEditMode(true, "\u8BBE\u7F6E\u4E2D\uFF1A\u53EF\u540C\u65F6\u67E5\u770B\u548C\u7BA1\u7406\u542F\u52A8\u9879");

    var window = new SettingsWindow(session, _iconService.Clear)
    {
        Owner = this,
    };

    _settingsWindowContext = new SettingsWindowContext(session, session.Original, wasEditMode);
    _settingsWindow = window;
    window.Loaded += SettingsWindow_Loaded;
    window.Closed += SettingsWindow_Closed;
    window.Show();
}

private void SettingsSession_PreviewRequested(object? sender, Settings settings) =>
    ApplySettingsPreview(settings);
```

设置窗口 XAML 固定使用：

```xml
WindowStartupLocation="Manual"
ShowInTaskbar="False"
```

保留 `Owner = this`，不设置 `Topmost`。

- [ ] **Step 3: 创建主窗口所在显示器的工作区提供器**

`MonitorWorkAreaProvider.cs` 使用 Win32 monitor API，并将物理像素转换为 WPF DIP：

```csharp
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace LanFlow.Desktop.Presentation;

internal static class MonitorWorkAreaProvider
{
    private const uint MonitorDefaultToNearest = 2;

    public static Rect GetForWindow(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        nint handle = new WindowInteropHelper(window).Handle;
        if (handle == 0)
        {
            return SystemParameters.WorkArea;
        }

        nint monitor = MonitorFromWindow(handle, MonitorDefaultToNearest);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == 0 || !GetMonitorInfo(monitor, ref info))
        {
            return SystemParameters.WorkArea;
        }

        Matrix fromDevice = HwndSource.FromHwnd(handle)?.CompositionTarget?.TransformFromDevice
            ?? Matrix.Identity;
        Point topLeft = fromDevice.Transform(new Point(info.WorkArea.Left, info.WorkArea.Top));
        Point bottomRight = fromDevice.Transform(new Point(info.WorkArea.Right, info.WorkArea.Bottom));
        return new Rect(topLeft, bottomRight);
    }

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect WorkArea;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
```

`SystemParameters.WorkArea` 只作为窗口句柄或 Monitor API 不可用时的回退。

- [ ] **Step 4: Loaded 后使用当前显示器工作区定位**

```csharp
private void SettingsWindow_Loaded(object sender, RoutedEventArgs e)
{
    if (sender is not SettingsWindow window)
    {
        return;
    }

    var ownerBounds = new Rect(Left, Top, ActualWidth, ActualHeight);
    Rect workArea = MonitorWorkAreaProvider.GetForWindow(this);
    var settingsSize = new Size(window.ActualWidth, window.ActualHeight);
    Point location = SettingsWindowPlacement.Calculate(ownerBounds, settingsSize, workArea);
    window.Left = location.X;
    window.Top = location.Y;
}
```

- [ ] **Step 5: 保存按钮关闭非模态窗口并提供宿主退出入口**

把主设置窗口 `Save_Click` 末尾的 `DialogResult = true;` 改为：

```csharp
Close();
```

仅 `ShowUnsavedChangesDialog()` 创建的小型确认窗口继续设置 `dialog.DialogResult`。

在 `SettingsWindow` 增加：

```csharp
public void CloseForOwnerShutdown()
{
    CloseDecision = UnsavedCloseDecision.Discard;
    Close();
}
```

- [ ] **Step 6: Closed 中用真实 TryComplete 签名完成事务并清理**

```csharp
private void SettingsWindow_Closed(object? sender, EventArgs e)
{
    if (sender is not SettingsWindow window || _settingsWindowContext is not { } context)
    {
        return;
    }

    window.Loaded -= SettingsWindow_Loaded;
    window.Closed -= SettingsWindow_Closed;
    context.Session.PreviewRequested -= SettingsSession_PreviewRequested;

    bool accepted = false;
    try
    {
        bool completed = SettingsCloseFlow.TryComplete(
            context.Session,
            window.CloseDecision,
            window.FlushPendingPreviews,
            result =>
            {
                if (!_hotkeyService.TryRegister(result.Hotkey))
                {
                    result.Hotkey = context.Original.Hotkey;
                    _viewModel.StatusText = "\u5FEB\u6377\u952E\u88AB\u5176\u4ED6\u7A0B\u5E8F\u5360\u7528\uFF0C\u5DF2\u4FDD\u7559\u539F\u7EC4\u5408\u952E";
                }

                bool requestedStartup = result.StartWithWindows;
                result.StartWithWindows = _startupService.SetEnabled(requestedStartup)
                    && _startupService.IsEnabled();
                if (result.StartWithWindows != requestedStartup)
                {
                    _viewModel.StatusText = "\u5F00\u673A\u542F\u52A8\u8BBE\u7F6E\u5931\u8D25\uFF0C\u8BF7\u68C0\u67E5\u5F53\u524D\u7528\u6237\u6CE8\u518C\u8868\u6743\u9650";
                }

                _settingsCoordinator.Apply(result);
                return _viewModel.Settings.Clone();
            });
        accepted = completed && window.CloseDecision == UnsavedCloseDecision.ApplyAndClose;
    }
    finally
    {
        if (!accepted)
        {
            _settingsCoordinator.Restore(_viewModel.Settings);
        }

        SetEditMode(
            accepted ? false : context.WasEditMode,
            accepted ? "\u8BBE\u7F6E\u5DF2\u4FDD\u5B58" : null);
        window.DisposePreviewThrottles();
        _settingsWindow = null;
        _settingsWindowContext = null;
    }
}
```

这里严格复用当前热键注册、开机启动和 `_settingsCoordinator.Apply(result)` 回调；不复制新的持久化路径。

- [ ] **Step 7: 主窗口退出前关闭设置窗口**

在 `MainWindow_Closed` 的 `_isClosed = true;` 之前执行：

```csharp
_settingsWindow?.CloseForOwnerShutdown();
```

`CloseForOwnerShutdown()` 预先设置 `Discard`，因此不显示未保存确认框；`SettingsWindow_Closed` 会先恢复预览并解除事件订阅，然后主窗口再释放服务。

- [ ] **Step 8: 运行生命周期合同测试和构建**

```powershell
dotnet test native\LanFlow.Desktop.Tests\LanFlow.Desktop.Tests.csproj -c Release --filter "MainWindowArchitectureContractTests|SettingsWindowContractTests|SettingsWindowPlacementTests|SettingsCloseFlowTests"
dotnet build native\LanFlow.Desktop\LanFlow.Desktop.csproj -c Release
```

Expected：0 failed，Build succeeded；主设置窗口代码不再调用 `ShowDialog()` 或设置自身 `DialogResult`。

### Task 4: 保持未保存更改确认语义

**Files:**
- Modify: `native/LanFlow.Desktop.Tests/SettingsCloseFlowTests.cs`
- Modify: `native/LanFlow.Desktop.Tests/SettingsWindowContractTests.cs`
- Modify: `native/LanFlow.Desktop/Views/SettingsWindow.xaml.cs`

**Interfaces:**
- Consumes: `UnsavedCloseDecision.ApplyAndClose`、`Discard`、`KeepEditing`。
- Produces: 非模态窗口关闭时保持原三选项结果；只有未保存确认小窗口使用 `ShowDialog()`。

- [ ] **Step 1: 增加非模态关闭合同**

```csharp
Assert.Contains("Closed += SettingsWindow_Closed", mainWindowCode);
Assert.Contains("SettingsCloseFlow.TryComplete(", mainWindowCode);
Assert.Contains("context.Session,", mainWindowCode);
Assert.Contains("window.CloseDecision,", mainWindowCode);
Assert.Contains("window.FlushPendingPreviews,", mainWindowCode);
Assert.Contains("ShowUnsavedChangesDialog", settingsWindowCode);
Assert.Contains("dialog.ShowDialog()", settingsWindowCode);
Assert.DoesNotContain("settingsWindow.ShowDialog()", mainWindowCode);
Assert.Contains("CloseForOwnerShutdown", settingsWindowCode);
```

- [ ] **Step 2: 覆盖三种关闭决策**

在 `SettingsCloseFlowTests` 的现有 fixture 中分别断言：

1. `ApplyAndClose`：`flush` 调用一次、`applyAndPersist` 调用一次、返回 `true`；
2. `Discard`：`flush` 调用一次、`applyAndPersist` 零次、返回 `true`；
3. `KeepEditing`：`flush` 调用一次、`applyAndPersist` 零次、返回 `false`。

不得新建第二套关闭流程。

- [ ] **Step 3: 运行关闭流程测试**

```powershell
dotnet test native\LanFlow.Desktop.Tests\LanFlow.Desktop.Tests.csproj -c Release --filter SettingsCloseFlowTests
```

Expected：0 failed。

- [ ] **Step 4: 运行完整设置事务测试**

```powershell
dotnet test native\LanFlow.Desktop.Tests\LanFlow.Desktop.Tests.csproj -c Release --filter "SettingsCloseFlowTests|SettingsPreviewSessionTests|SettingsWindowViewModelTests|MainWindowArchitectureContractTests|SettingsWindowPlacementTests|SettingsWindowContractTests"
```

Expected：0 failed。
### Task 5: 构建和提交 Phase 3

- [ ] **Step 1: 构建 Desktop**

```powershell
dotnet build native\LanFlow.Desktop\LanFlow.Desktop.csproj -c Release
```

Expected：Build succeeded，0 errors。

- [ ] **Step 2: 做最小手动检查**

运行应用后仅检查：

1. 第一次点击设置显示窗口；
2. 再次点击只激活同一窗口；
3. 主窗口仍能切换分组；
4. 设置窗口优先位于右侧，右侧不足时位于左侧；
5. 应用、放弃、继续编辑按原语义工作。

- [ ] **Step 3: 提交 Phase 3**

```powershell
git add native/LanFlow.Desktop/Presentation/SettingsWindowPlacement.cs native/LanFlow.Desktop/MainWindow.xaml.cs native/LanFlow.Desktop/Views/SettingsWindow.xaml native/LanFlow.Desktop/Views/SettingsWindow.xaml.cs native/LanFlow.Desktop.Tests
git commit -m "feat: open settings as a modeless companion window"
```
