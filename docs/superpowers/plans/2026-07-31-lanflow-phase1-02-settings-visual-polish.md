# LanFlow Phase 2 Settings Visual Polish Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 统一设置页与主页视觉令牌，修复左侧导航轮廓被裁切和透明度百分比显示不全，并改善约 85% 透明度下的信息层级。

**Architecture:** 不新增主题系统，只调整 `SettingsWindow.xaml` 的布局约束和 `Components.xaml` 的设置专用样式；使用现有动态资源保证主题及透明度同步。合同测试锁定资源、间距和可显示宽度，不引入依赖截图的脆弱测试。

**Tech Stack:** WPF XAML、DynamicResource、xUnit 文件合同测试。

## Global Constraints

- 保留左侧分类导航、右侧内容和底部操作栏。
- 分组导航只在顶部或左侧显示，不新增右侧分组栏。
- 设置页使用现有语义 Brush 和 Radius。
- 输入框、按钮和选择状态在约 85% 透明度下必须有边界。
- 百分比框至少 72px，完整显示 0、85、100。
- 不进行整套主题或控件库重写。

---

### Task 1: 锁定设置页结构和语义资源

**Files:**
- Modify: `native/LanFlow.Desktop.Tests/SettingsWindowContractTests.cs`
- Modify: `native/LanFlow.Desktop.Tests/ThemeResourceContractTests.cs`
- Modify: `native/LanFlow.Desktop/Views/SettingsWindow.xaml`
- Modify: `native/LanFlow.Desktop/Themes/Components.xaml`

**Interfaces:**
- Consumes: `WindowBackgroundBrush`、`SurfaceBrush`、`MutedSurfaceBrush`、`WindowBorderBrush`、`DividerBrush`、`ItemHoverBrush`、`ItemSelectedBrush`、`FocusBorderBrush`。
- Produces: 无硬编码页面色板的设置窗口结构。

- [ ] **Step 1: 添加设置视觉合同测试**

在 `SettingsWindowContractTests` 增加：

```csharp
[Fact]
public void SettingsWindow_UsesSharedSemanticSurfacesAndSafeNavigationGeometry()
{
    var xaml = File.ReadAllText(GetDesktopPath("Views", "SettingsWindow.xaml"));

    Assert.Contains("Background=\"{DynamicResource WindowBackgroundBrush}\"", xaml);
    Assert.Contains("HorizontalContentAlignment=\"Stretch\"", xaml);
    Assert.DoesNotContain("Margin=\"-8,0\"", xaml);
    Assert.DoesNotContain("<ListBox Width=\"184\"", xaml);
    Assert.Contains("Padding=\"14,18,8,14\"", xaml);
}
```

在 `ThemeResourceContractTests` 增加：

```csharp
[Fact]
public void SettingsStyles_UseOnlyApprovedSemanticBrushes()
{
    var xaml = File.ReadAllText(GetDesktopPath("Themes", "Components.xaml"));

    Assert.Contains("ItemSelectedBrush", xaml);
    Assert.Contains("FocusBorderBrush", xaml);
    Assert.Contains("SurfaceBrush", xaml);
    Assert.Contains("DividerBrush", xaml);
    Assert.DoesNotContain("#FFFFFFFF", xaml);
}
```

- [ ] **Step 2: 运行合同测试确认当前几何失败**

Run:

```powershell
dotnet test native\LanFlow.Desktop.Tests\LanFlow.Desktop.Tests.csproj -c Release --filter "SettingsWindowContractTests|ThemeResourceContractTests"
```

Expected：导航固定宽度/负 Margin 断言失败。

### Task 2: 修复左侧导航裁切

**Files:**
- Modify: `native/LanFlow.Desktop/Views/SettingsWindow.xaml`
- Modify: `native/LanFlow.Desktop/Themes/Components.xaml`

**Interfaces:**
- Consumes: `SettingsCategoryItemStyle`。
- Produces: 选中和键盘焦点边框始终在导航栏内部。

- [ ] **Step 1: 调整导航列和 ListBox 布局**

保持左列约 184px，但内部结构改为：

```xml
<Border Grid.Column="0"
        Background="{DynamicResource SurfaceBrush}"
        BorderBrush="{DynamicResource DividerBrush}"
        BorderThickness="0,0,1,0">
    <Grid Margin="14,18,8,14">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="Auto" />
            <RowDefinition Height="16" />
            <RowDefinition Height="*" />
        </Grid.RowDefinitions>
        <TextBlock Text="设置" FontSize="22" FontWeight="SemiBold" />
        <TextBlock Grid.Row="1"
                   Margin="0,4,0,0"
                   Text="按功能分类调整 LanFlow"
                   FontSize="12"
                   Foreground="{DynamicResource SecondaryTextBrush}" />
        <ListBox x:Name="CategoryList"
                 Grid.Row="3"
                 Padding="0"
                 BorderThickness="0"
                 Background="Transparent"
                 HorizontalContentAlignment="Stretch"
                 ItemsSource="{Binding Categories}"
                 SelectedItem="{Binding SelectedCategory, Mode=TwoWay}"
                 ItemContainerStyle="{StaticResource SettingsCategoryItemStyle}"
                 SelectionChanged="CategoryList_SelectionChanged" />
    </Grid>
</Border>
```

删除 ListBox 的固定 `Width` 和 `Margin="-8,0"`。

- [ ] **Step 2: 把焦点轮廓限制在模板内部**

`SettingsCategoryItemStyle` 保持容器 `BorderThickness="1"`，触发器改为只设置模板 Border：

```xml
<Trigger Property="IsSelected" Value="True">
    <Setter TargetName="CategoryBorder" Property="Background" Value="{DynamicResource ItemSelectedBrush}" />
    <Setter TargetName="CategoryBorder" Property="BorderBrush" Value="{DynamicResource FocusBorderBrush}" />
    <Setter TargetName="CategoryBorder" Property="BorderThickness" Value="1" />
</Trigger>
<Trigger Property="IsKeyboardFocusWithin" Value="True">
    <Setter TargetName="CategoryBorder" Property="BorderBrush" Value="{DynamicResource FocusBorderBrush}" />
    <Setter TargetName="CategoryBorder" Property="BorderThickness" Value="2" />
</Trigger>
```

模板 Border 不设置负 Margin，`SnapsToDevicePixels="True"`。

- [ ] **Step 3: 运行设置视觉合同测试**

Run:

```powershell
dotnet test native\LanFlow.Desktop.Tests\LanFlow.Desktop.Tests.csproj -c Release --filter SettingsWindow_UsesSharedSemanticSurfacesAndSafeNavigationGeometry
```

Expected：PASS。

### Task 3: 统一右侧区块视觉层级

**Files:**
- Modify: `native/LanFlow.Desktop/Themes/Components.xaml`
- Modify: `native/LanFlow.Desktop/Views/SettingsWindow.xaml`

**Interfaces:**
- Consumes: `SettingsSectionStyle`、`SettingsFieldRowStyle`、`SettingsFooterStyle`。
- Produces: 统一的设置区块、字段行和页脚视觉。

- [ ] **Step 1: 固定设置组件规格**

把设置样式调整为：

```xml
<Style x:Key="SettingsSectionStyle" TargetType="Border">
    <Setter Property="Margin" Value="0,0,0,16" />
    <Setter Property="Padding" Value="18,6" />
    <Setter Property="Background" Value="{DynamicResource SurfaceBrush}" />
    <Setter Property="BorderBrush" Value="{DynamicResource WindowBorderBrush}" />
    <Setter Property="BorderThickness" Value="1" />
    <Setter Property="CornerRadius" Value="{StaticResource Radius.Medium}" />
</Style>

<Style x:Key="SettingsFieldRowStyle" TargetType="Border">
    <Setter Property="MinHeight" Value="44" />
    <Setter Property="Padding" Value="0,12" />
    <Setter Property="BorderBrush" Value="{DynamicResource DividerBrush}" />
    <Setter Property="BorderThickness" Value="0,0,0,1" />
</Style>
```

页脚继续使用 `SurfaceBrush` 和顶部边框，不改操作按钮语义。

- [ ] **Step 2: 统一内容页边距**

右侧标题和滚动内容采用 22px 左右边距、16px 区块间距；移除单个页面中与公共样式冲突的白色背景、硬编码边框和重复 Padding。

每个分类仍使用当前命名面板：

```text
AppearancePanel
LayoutPanel
GroupsPanel
TransparencyPanel
InteractionPanel
StartupPanel
PerformancePanel
AboutPanel
```

- [ ] **Step 3: 增加不使用硬编码色板的合同断言**

```csharp
Assert.DoesNotContain("Background=\"#", settingsWindowXaml);
Assert.DoesNotContain("Foreground=\"#", settingsWindowXaml);
Assert.DoesNotContain("BorderBrush=\"#", settingsWindowXaml);
```

这些断言只禁止设置窗口直接写入颜色属性，不影响图标字符实体或共享主题字典。

- [ ] **Step 4: 运行主题与设置合同测试**

Run:

```powershell
dotnet test native\LanFlow.Desktop.Tests\LanFlow.Desktop.Tests.csproj -c Release --filter "ThemeResourceContractTests|SettingsWindowContractTests"
```

Expected：0 failed。

### Task 4: 修复透明度百分比显示

**Files:**
- Modify: `native/LanFlow.Desktop.Tests/SettingsWindowContractTests.cs`
- Modify: `native/LanFlow.Desktop/Views/SettingsWindow.xaml`
- Inspect: `native/LanFlow.Desktop/Views/SettingsWindow.xaml.cs`

**Interfaces:**
- Consumes: `OpacityPercentBox` 和现有 `CurrentOpacity` 双向更新。
- Produces: 0–100 的整数百分比完整显示。

- [ ] **Step 1: 添加百分比宽度合同测试**

```csharp
[Fact]
public void OpacityPercentBox_ReservesSpaceForThreeDigitsAndSuffix()
{
    var xaml = File.ReadAllText(GetDesktopPath("Views", "SettingsWindow.xaml"));

    Assert.Matches(
        @"x:Name=""OpacityPercentBox""[\s\S]*?MinWidth=""72""",
        xaml);
    Assert.DoesNotMatch(
        @"x:Name=""OpacityPercentBox""[\s\S]*?Width=""58""",
        xaml);
    Assert.Contains("Text=\"%\"", xaml);
}
```

测试使用正则跨行匹配 `OpacityPercentBox` 属性，不依赖 XAML 换行格式。

- [ ] **Step 2: 运行测试确认失败**

Run:

```powershell
dotnet test native\LanFlow.Desktop.Tests\LanFlow.Desktop.Tests.csproj -c Release --filter OpacityPercentBox_ReservesSpaceForThreeDigitsAndSuffix
```

Expected：失败，因为当前宽度为 58。

- [ ] **Step 3: 修改百分比输入布局**

```xml
<StackPanel Orientation="Horizontal" VerticalAlignment="Center">
    <TextBox x:Name="OpacityPercentBox"
             MinWidth="72"
             HorizontalContentAlignment="Right"
             VerticalContentAlignment="Center"
             TextChanged="OpacityPercentBox_TextChanged" />
    <TextBlock Margin="6,0,0,0"
               VerticalAlignment="Center"
               Foreground="{DynamicResource SecondaryTextBrush}"
               Text="%" />
</StackPanel>
```

保留当前输入解析和 `(_viewModel.CurrentOpacity * 100).ToString("0", ...)`，因为数值逻辑正确。

- [ ] **Step 4: 运行设置窗口和 ViewModel 测试**

Run:

```powershell
dotnet test native\LanFlow.Desktop.Tests\LanFlow.Desktop.Tests.csproj -c Release --filter "SettingsWindowContractTests|SettingsWindowViewModelTests|SettingsPreviewSessionTests"
```

Expected：0 failed。

### Task 5: 构建和提交 Phase 2

**Files:**
- Verify: `native/LanFlow.Desktop/Views/SettingsWindow.xaml`
- Verify: `native/LanFlow.Desktop/Themes/Components.xaml`

- [ ] **Step 1: 检查 XAML 和构建**

```powershell
dotnet build native\LanFlow.Desktop\LanFlow.Desktop.csproj -c Release
```

Expected：Build succeeded，0 errors。

- [ ] **Step 2: 运行本阶段完整测试集**

```powershell
dotnet test native\LanFlow.Desktop.Tests\LanFlow.Desktop.Tests.csproj -c Release --filter "SettingsWindowContractTests|ThemeResourceContractTests|SettingsWindowViewModelTests|SettingsPreviewSessionTests"
```

Expected：0 failed。

- [ ] **Step 3: 提交 Phase 2**

```powershell
git add native/LanFlow.Desktop/Views/SettingsWindow.xaml native/LanFlow.Desktop/Themes/Components.xaml native/LanFlow.Desktop.Tests
git commit -m "style: align settings window with main surface"
```
