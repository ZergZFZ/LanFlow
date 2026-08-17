# LanFlow 阶段 3：设计系统与双透明度 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 建立分层设计令牌并精修主窗口，在保持 85% 常用透明度可读性的前提下实现分层透明与整窗透明两种模式，以及遵循 Windows 偏好的低成本内容过渡。

**Architecture:** XAML 资源按基础、语义和组件三层拆分，主题色只更新语义资源。`WindowAppearanceController` 把透明度设置翻译为窗口级与表面级 Alpha；`AnimationPreferenceService` 只决定是否允许 100 ms 的淡入/微位移，不参与数据加载或布局正确性。

**Tech Stack:** .NET 8、WPF ResourceDictionary、DynamicResource、xUnit、SystemParameters/Windows UI Settings、Storyboard。

## Global Constraints

- 主窗口保持原有结构和业务入口，不复制概念模板，不增加右侧分组栏。
- 设计风格为紧凑、专业、高信息密度、低装饰；圆角、阴影和动效克制。
- 新视觉值必须进入基础、语义或组件令牌，不继续散布同义硬编码颜色、间距和圆角。
- 核心语义键至少包括 `WindowBackgroundBrush`、`SurfaceBrush`、`ItemHoverBrush`、`ItemSelectedBrush`、`PrimaryTextBrush`、`SecondaryTextBrush`、`FocusBorderBrush`、`GroupTabSelectedBrush`。
- 透明度范围 40%–100%，默认 85%；两种模式分别记忆数值。
- 分层透明时文字、图标、焦点、选中、拖拽和关键交互保持完全不透明；整窗透明保留兼容行为。
- 不使用动态全窗模糊作为可读性保障。
- 40%、85%、100% 都必须在浅色、深色和复杂桌面背景上可辨识。
- 只对热缓存内容使用 80–120 ms 透明度和极小位移；固定实现时长 100 ms。
- 禁止图标缩放、弹跳、大范围页面移动或任何改变布局几何的动画。
- 动画模式为 system/on/off；system 遵循 Windows 减少动画偏好。
- 每个任务执行 TDD、完整构建和独立提交。

---

### Task 1: 建立三层设计令牌资源字典

**Files:**
- Create: `native/LanFlow.Desktop/Themes/Tokens.Base.xaml`
- Create: `native/LanFlow.Desktop/Themes/Tokens.Semantic.xaml`
- Create: `native/LanFlow.Desktop/Themes/Components.xaml`
- Modify: `native/LanFlow.Desktop/App.xaml`
- Create: `native/LanFlow.Desktop.Tests/ThemeResourceContractTests.cs`

**Interfaces:**
- Consumes: existing theme colors and WPF application resources.
- Produces: stable resource keys for main window, navigation, item templates and settings controls.

- [ ] **Step 1: 写资源键和硬编码颜色边界失败测试**

```csharp
public sealed class ThemeResourceContractTests
{
    private static readonly string Root = FindRepositoryRoot();

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !Directory.Exists(Path.Combine(current.FullName, "native"))) current = current.Parent;
        return current?.FullName ?? throw new DirectoryNotFoundException("Repository root containing native/ was not found.");
    }

    [Theory]
    [InlineData("WindowBackgroundBrush")]
    [InlineData("SurfaceBrush")]
    [InlineData("ItemHoverBrush")]
    [InlineData("ItemSelectedBrush")]
    [InlineData("PrimaryTextBrush")]
    [InlineData("SecondaryTextBrush")]
    [InlineData("FocusBorderBrush")]
    [InlineData("GroupTabSelectedBrush")]
    public void SemanticDictionary_DefinesRequiredKey(string key)
    {
        var xaml = File.ReadAllText(Path.Combine(Root, "native/LanFlow.Desktop/Themes/Tokens.Semantic.xaml"));
        Assert.Contains($"x:Key=\"{key}\"", xaml);
    }

    [Fact]
    public void App_MergesBaseSemanticAndComponentDictionariesInOrder()
    {
        var xaml = File.ReadAllText(Path.Combine(Root, "native/LanFlow.Desktop/App.xaml"));
        Assert.True(xaml.IndexOf("Tokens.Base.xaml", StringComparison.Ordinal) < xaml.IndexOf("Tokens.Semantic.xaml", StringComparison.Ordinal));
        Assert.True(xaml.IndexOf("Tokens.Semantic.xaml", StringComparison.Ordinal) < xaml.IndexOf("Components.xaml", StringComparison.Ordinal));
    }
}
```

增加扫描 `MainWindow.xaml` 新增行不含十六进制颜色的约束；历史颜色通过本任务迁移后白名单只允许透明 `#00FFFFFF`。

- [ ] **Step 2: 运行测试并确认字典文件不存在**

Run: `dotnet test native\LanFlow.Desktop.Tests\LanFlow.Desktop.Tests.csproj -c Release --filter ThemeResourceContractTests`

Expected: 失败，提示三个字典文件不存在或必需键缺失。

- [ ] **Step 3: 创建基础令牌字典**

`Tokens.Base.xaml` 必须定义：

```xml
<Color x:Key="Color.Neutral.000">#FFFFFFFF</Color>
<Color x:Key="Color.Neutral.050">#FFF7F8FB</Color>
<Color x:Key="Color.Neutral.100">#FFE9ECF2</Color>
<Color x:Key="Color.Neutral.700">#FF344054</Color>
<Color x:Key="Color.Neutral.900">#FF121826</Color>
<Color x:Key="Color.Accent.500">#FF5B67D6</Color>
<Color x:Key="Color.Danger.500">#FFD04444</Color>
<System:Double x:Key="Space.1">4</System:Double>
<System:Double x:Key="Space.2">8</System:Double>
<System:Double x:Key="Space.3">12</System:Double>
<System:Double x:Key="Space.4">16</System:Double>
<CornerRadius x:Key="Radius.Small">5</CornerRadius>
<CornerRadius x:Key="Radius.Medium">8</CornerRadius>
<Duration x:Key="Motion.Fast">0:0:0.1</Duration>
<System:Double x:Key="Font.Size.Body">13</System:Double>
<System:Double x:Key="Font.Size.Caption">12</System:Double>
<System:Double x:Key="Icon.Size.Command">16</System:Double>
```

命名空间用 `xmlns:System="clr-namespace:System;assembly=mscorlib"`；只放原始值，不表达具体组件用途。

- [ ] **Step 4: 创建语义和组件字典并合并到 App.xaml**

`Tokens.Semantic.xaml` 把基础色包装为可替换的 Brush，除八个必需键外至少提供 `WindowBorderBrush`、`MutedSurfaceBrush`、`DividerBrush`、`DangerBrush`、`DragIndicatorBrush`。`Components.xaml` 定义 `CommandButtonStyle`、`PrimaryButtonStyle`、`CompactTextBoxStyle`、`GroupTabItemStyle`、`LauncherItemContainerStyle`、`SettingsSectionHeaderStyle`。

`App.xaml` 仅保留合并入口：

```xml
<ResourceDictionary>
  <ResourceDictionary.MergedDictionaries>
    <ResourceDictionary Source="Themes/Tokens.Base.xaml" />
    <ResourceDictionary Source="Themes/Tokens.Semantic.xaml" />
    <ResourceDictionary Source="Themes/Components.xaml" />
  </ResourceDictionary.MergedDictionaries>
</ResourceDictionary>
```

- [ ] **Step 5: 运行资源测试、构建并提交**

Run:

```powershell
dotnet test native\LanFlow.Desktop.Tests\LanFlow.Desktop.Tests.csproj -c Release --filter ThemeResourceContractTests
dotnet build native\LanFlow.Desktop\LanFlow.Desktop.csproj -c Release
git diff --check
git add native/LanFlow.Desktop/Themes/Tokens.Base.xaml native/LanFlow.Desktop/Themes/Tokens.Semantic.xaml native/LanFlow.Desktop/Themes/Components.xaml native/LanFlow.Desktop/App.xaml native/LanFlow.Desktop.Tests/ThemeResourceContractTests.cs
git commit -m "style: add layered WPF design tokens"
```

Expected: 测试通过，应用启动时无 StaticResource 查找异常。

---

### Task 2: 将主窗口、分组标签和项目模板迁移到语义令牌

**Files:**
- Modify: `native/LanFlow.Desktop/MainWindow.xaml`
- Modify: `native/LanFlow.Desktop/MainWindow.xaml.cs`
- Modify: `native/LanFlow.Desktop/Controls/GroupNavigationControl.xaml`
- Modify: `native/LanFlow.Desktop/Themes/Components.xaml`
- Create: `native/LanFlow.Desktop/Presentation/ThemeResourceUpdater.cs`
- Create: `native/LanFlow.Desktop.Tests/ThemeResourceUpdaterTests.cs`

**Interfaces:**
- Consumes: `ThemeColors`, Task 1 semantic brushes.
- Produces: `ThemeResourceUpdater.Apply(ResourceDictionary, ThemeColors)`; distinct hover/selected/focus/drag states without layout transforms.

- [ ] **Step 1: 写主题映射和状态区分失败测试**

构造空 `ResourceDictionary`，调用 updater 后断言八个语义 brush 都存在且颜色来自对应 `ThemeColors`。XAML 合同测试断言项目模板分别引用 `ItemHoverBrush`、`ItemSelectedBrush`、`FocusBorderBrush`，且不含 `ScaleTransform`。

- [ ] **Step 2: 运行测试并确认 updater 不存在**

Run: `dotnet test native\LanFlow.Desktop.Tests\LanFlow.Desktop.Tests.csproj -c Release --filter "ThemeResourceUpdaterTests|ThemeResourceContractTests"`

Expected: 编译失败或 XAML 状态断言失败。

- [ ] **Step 3: 实现唯一主题资源更新入口**

```csharp
public sealed class ThemeResourceUpdater
{
    public void Apply(ResourceDictionary resources, ThemeColors colors)
    {
        Set(resources, "WindowBackgroundBrush", colors.Panel);
        Set(resources, "SurfaceBrush", colors.Surface);
        Set(resources, "ItemHoverBrush", colors.Hover);
        Set(resources, "ItemSelectedBrush", colors.Accent);
        Set(resources, "PrimaryTextBrush", colors.TextPrimary);
        Set(resources, "SecondaryTextBrush", colors.TextSecondary);
        Set(resources, "FocusBorderBrush", colors.Accent);
        Set(resources, "GroupTabSelectedBrush", colors.Accent);
        Set(resources, "WindowBorderBrush", colors.PanelBorder);
        Set(resources, "DividerBrush", colors.SurfaceBorder);
    }

    private static void Set(ResourceDictionary resources, string key, string color) =>
        resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
}
```

创建的 brush 可冻结时冻结。删除 `MainWindow.xaml.cs` 中散落的 `SetBrush` 调用，统一委托给 updater。

- [ ] **Step 4: 迁移主窗口视觉并保持布局边界**

用 `DynamicResource` 替换主窗口、搜索、工具栏、内容区、空状态、分组标签和项目模板内的颜色。组件状态规则：hover 只改 brush/边框；selected 使用独立背景；keyboard focus 使用 2 DIP focus border；drag insertion 使用不透明 `DragIndicatorBrush`；任何 trigger 均不得修改 Width/Height/Margin 或应用缩放。

保留原入口位置；禁止增加第二套或右侧分组导航。文字统一 `CharacterEllipsis` + Tooltip，主要间距改用 `Space.*` 令牌。

- [ ] **Step 5: 运行测试、三主题人工验证并提交**

Run:

```powershell
dotnet test native\LanFlow.Desktop.Tests\LanFlow.Desktop.Tests.csproj -c Release --filter "ThemeResourceUpdaterTests|ThemeResourceContractTests"
dotnet build native\LanFlow.Desktop\LanFlow.Desktop.csproj -c Release
git diff --check
git add native/LanFlow.Desktop/MainWindow.xaml native/LanFlow.Desktop/MainWindow.xaml.cs native/LanFlow.Desktop/Controls/GroupNavigationControl.xaml native/LanFlow.Desktop/Themes/Components.xaml native/LanFlow.Desktop/Presentation/ThemeResourceUpdater.cs native/LanFlow.Desktop.Tests/ThemeResourceUpdaterTests.cs
git commit -m "style: refine main window with semantic tokens"
```

Expected: 深色、浅色、自定义主题均可启动；hover/selected/focus/drag 状态可区分；布局没有因状态变化跳动。

---

### Task 3: 实现分层透明与整窗透明策略

**Files:**
- Create: `native/LanFlow.Desktop/Presentation/WindowAppearanceState.cs`
- Create: `native/LanFlow.Desktop/Presentation/WindowAppearanceController.cs`
- Modify: `native/LanFlow.Desktop/MainWindow.xaml`
- Modify: `native/LanFlow.Desktop/MainWindow.xaml.cs`
- Create: `native/LanFlow.Desktop.Tests/WindowAppearanceControllerTests.cs`

**Interfaces:**
- Consumes: `Settings.TransparencyMode`, `LayeredOpacity`, `WholeWindowOpacity`, semantic surface resources.
- Produces: `WindowAppearanceState(double WindowOpacity, byte SurfaceAlpha, double ContentOpacity)`; `Apply(Window, FrameworkElement surfaceRoot, FrameworkElement contentRoot, Settings)`.

- [ ] **Step 1: 写两模式和边界失败测试**

```csharp
public sealed class WindowAppearanceControllerTests
{
    [Theory]
    [InlineData(0.40)]
    [InlineData(0.85)]
    [InlineData(1.00)]
    public void WholeWindow_UsesOneOpacityForWindowAndContent(double opacity)
    {
        var state = WindowAppearanceController.Calculate(SettingsOptionValues.TransparencyWholeWindow, 0.85, opacity);
        Assert.Equal(opacity, state.WindowOpacity, 3);
        Assert.Equal(255, state.SurfaceAlpha);
        Assert.Equal(1.0, state.ContentOpacity, 3);
    }

    [Theory]
    [InlineData(0.40, 102)]
    [InlineData(0.85, 217)]
    [InlineData(1.00, 255)]
    public void Layered_LeavesWindowAndContentOpaqueAndChangesSurfaceAlpha(double opacity, byte alpha)
    {
        var state = WindowAppearanceController.Calculate(SettingsOptionValues.TransparencyLayered, opacity, 0.85);
        Assert.Equal(1.0, state.WindowOpacity, 3);
        Assert.Equal(alpha, state.SurfaceAlpha);
        Assert.Equal(1.0, state.ContentOpacity, 3);
    }
}
```

- [ ] **Step 2: 运行测试并确认 appearance 类型不存在**

Run: `dotnet test native\LanFlow.Desktop.Tests\LanFlow.Desktop.Tests.csproj -c Release --filter WindowAppearanceControllerTests`

Expected: 编译失败，缺少 controller/state。

- [ ] **Step 3: 实现纯计算与窗口应用**

```csharp
public readonly record struct WindowAppearanceState(double WindowOpacity, byte SurfaceAlpha, double ContentOpacity);

public static WindowAppearanceState Calculate(string mode, double layeredOpacity, double wholeWindowOpacity)
{
    layeredOpacity = Math.Clamp(layeredOpacity, 0.40, 1.00);
    wholeWindowOpacity = Math.Clamp(wholeWindowOpacity, 0.40, 1.00);
    return mode == SettingsOptionValues.TransparencyWholeWindow
        ? new(wholeWindowOpacity, 255, 1.0)
        : new(1.0, (byte)Math.Round(layeredOpacity * 255, MidpointRounding.AwayFromZero), 1.0);
}
```

`Apply` 在整窗模式设置 `window.Opacity`，表面 brush 保持不透明；分层模式强制 `window.Opacity = 1`，只给 `WindowBackgroundBrush`、`SurfaceBrush` 和 `MutedSurfaceBrush` 创建带 alpha 的副本。`contentRoot`、焦点层、拖拽层和错误提示层始终 `Opacity = 1`。

- [ ] **Step 4: 在 XAML 中划分 surface、content 和 feedback 三层**

主窗口根布局明确命名：

```xml
<Grid x:Name="AppearanceRoot">
  <Border x:Name="SurfaceRoot" Background="{DynamicResource WindowBackgroundBrush}" />
  <Grid x:Name="ContentRoot"><!-- 原有工具栏、导航、项目内容 --></Grid>
  <AdornerDecorator x:Name="FeedbackRoot" IsHitTestVisible="False" />
</Grid>
```

不要设置 `AllowsTransparency=True` 或引入全窗 blur；保持现有窗口合成路径。设置预览和正式应用都只调用同一个 controller。

- [ ] **Step 5: 验证 40/85/100% 与重启恢复并提交**

Run:

```powershell
dotnet test native\LanFlow.Desktop.Tests\LanFlow.Desktop.Tests.csproj -c Release --filter WindowAppearanceControllerTests
dotnet build native\LanFlow.Desktop\LanFlow.Desktop.csproj -c Release
git diff --check
git add native/LanFlow.Desktop/Presentation/WindowAppearanceState.cs native/LanFlow.Desktop/Presentation/WindowAppearanceController.cs native/LanFlow.Desktop/MainWindow.xaml native/LanFlow.Desktop/MainWindow.xaml.cs native/LanFlow.Desktop.Tests/WindowAppearanceControllerTests.cs
git commit -m "feat: add layered and whole-window transparency"
```

Expected: 两模式在 40/85/100% 都与测试一致；分层模式文字/图标/焦点/拖拽完全不透明；整窗模式保持旧行为；无动态模糊。

---

### Task 4: 遵循系统动画偏好并实现缓存命中轻量过渡

**Files:**
- Create: `native/LanFlow.Desktop/Services/IAnimationPreferenceService.cs`
- Create: `native/LanFlow.Desktop/Services/AnimationPreferenceService.cs`
- Create: `native/LanFlow.Desktop/Presentation/ContentTransitionController.cs`
- Modify: `native/LanFlow.Desktop/MainWindow.xaml`
- Modify: `native/LanFlow.Desktop/MainWindow.xaml.cs`
- Create: `native/LanFlow.Desktop.Tests/ContentTransitionControllerTests.cs`

**Interfaces:**
- Consumes: `Settings.AnimationMode`, Windows system animations enabled state, group cache-hit flag.
- Produces: `ShouldAnimate(string animationMode, bool systemAnimationsEnabled, bool cacheHit)`; 100 ms opacity + 4 DIP translation transition.

- [ ] **Step 1: 写三模式决策矩阵失败测试**

```csharp
[Theory]
[InlineData(SettingsOptionValues.AnimationSystem, true, true, true)]
[InlineData(SettingsOptionValues.AnimationSystem, false, true, false)]
[InlineData(SettingsOptionValues.AnimationOn, false, true, true)]
[InlineData(SettingsOptionValues.AnimationOff, true, true, false)]
[InlineData(SettingsOptionValues.AnimationOn, true, false, false)]
public void ShouldAnimate_RequiresPreferenceAndCacheHit(string mode, bool systemEnabled, bool cacheHit, bool expected)
{
    Assert.Equal(expected, ContentTransitionController.ShouldAnimate(mode, systemEnabled, cacheHit));
}
```

- [ ] **Step 2: 运行测试并确认 controller 不存在**

Run: `dotnet test native\LanFlow.Desktop.Tests\LanFlow.Desktop.Tests.csproj -c Release --filter ContentTransitionControllerTests`

Expected: 编译失败，缺少接口或 controller。

- [ ] **Step 3: 实现 Windows 偏好读取与决策逻辑**

`IAnimationPreferenceService`：

```csharp
public interface IAnimationPreferenceService
{
    bool AreAnimationsEnabled { get; }
    event EventHandler? PreferenceChanged;
}
```

生产实现以 `SystemParameters.ClientAreaAnimation` 为主并监听 `SystemParameters.StaticPropertyChanged`；读取失败时安全返回 true。`ShouldAnimate` 仅在 cacheHit 且模式允许时返回 true。

- [ ] **Step 4: 实现不改变布局的 100 ms 过渡**

`ContentTransitionController.PlayAsync(FrameworkElement content, bool animate, CancellationToken)`：禁用时立即设置 `Opacity=1`、`TranslateTransform.Y=0`；启用时从 `Opacity=0.92`、`Y=4` 到 1/0，Duration 固定 100 ms，`FillBehavior=Stop` 后提交最终值。新切组取消旧 storyboard；未缓存内容不播放，只保留固定占位。

- [ ] **Step 5: 接入组内容稳定事件并验证无图标缩放**

只有阶段 2 的当前 generation 内容稳定且缓存命中时播放；导航选中反馈不等待动画。XAML 和代码搜索必须不含面向图标的 `ScaleTransform`、`BounceEase` 或影响 Margin/Width/Height 的动画。

- [ ] **Step 6: 运行全部测试、系统动画开关烟雾验证并提交**

Run:

```powershell
dotnet test native\LanFlow.Desktop.Tests\LanFlow.Desktop.Tests.csproj -c Release
dotnet test native\LanFlow.Core.Tests\LanFlow.Core.Tests.csproj -c Release
dotnet build native\LanFlow.Desktop\LanFlow.Desktop.csproj -c Release
git diff --check
git add native/LanFlow.Desktop/Services/IAnimationPreferenceService.cs native/LanFlow.Desktop/Services/AnimationPreferenceService.cs native/LanFlow.Desktop/Presentation/ContentTransitionController.cs native/LanFlow.Desktop/MainWindow.xaml native/LanFlow.Desktop/MainWindow.xaml.cs native/LanFlow.Desktop.Tests/ContentTransitionControllerTests.cs
git commit -m "feat: respect animation preference for group transitions"
```

Expected: system/on/off 全矩阵通过；关闭 Windows 动画时 system 模式无过渡；动画开启时仅有 100 ms 淡入和 4 DIP 位移，无尺寸抖动。

---

## 阶段 3 完成门

- [ ] 三层资源字典合并顺序固定，八个核心语义键存在。
- [ ] 主窗口主要组件迁移到语义令牌，hover/selected/focus/drag 状态区分且不改变布局。
- [ ] 分层透明和整窗透明在 40/85/100% 计算、预览和重启恢复正确。
- [ ] 分层透明下关键内容与反馈完全不透明，未引入动态全窗模糊。
- [ ] system/on/off 动画矩阵通过；100 ms 过渡仅用于热缓存内容且无图标缩放。
- [ ] 深/浅/自定义主题在常见浅色、深色、复杂桌面和 Windows 高对比背景上完成可读性检查。
