# Win11 性能模式下窗口阴影伪影修复方案

## 问题描述

在 Windows 11 性能模式下，窗口在桌面的**右侧**和**下侧**出现多余的阴影。

## 问题根因

`MainWindow.xaml` 中使用了以下组合：

```xml
AllowsTransparency="True"
WindowStyle="None"
```

并通过 WPF 的 `DropShadowEffect` 模拟阴影：

```xml
<Border Margin="16" ...>
    <Border.Effect>
        <DropShadowEffect BlurRadius="22" ShadowDepth="0" Opacity="0.45" Color="#000000" />
    </Border.Effect>
</Border>
```

WPF 的 `DropShadowEffect` 是**软件渲染**的位图模糊效果。在 Win11 性能模式（关闭透明效果和动画）下，DWM 合成行为变化导致软件阴影在右侧和下侧出现渲染溢出/裁剪伪影。

---

## 推荐方案：使用 Windows DWM 原生阴影替代 DropShadowEffect

### 核心思路

去掉 WPF 的 `DropShadowEffect`，改用 Windows Desktop Window Manager (DWM) 的原生窗口阴影。DWM 阴影是 GPU 加速的，不存在软件渲染的溢出问题，且视觉效果与系统一致。

### 具体步骤

#### 1. 修改 XAML

- **移除** 外层 `Border` 上的 `DropShadowEffect`
- **去掉** 外层 `Border` 的 `Margin="16"`（该 margin 原本是为阴影预留空间的）

修改前：

```xml
<Border Margin="16" Background="{DynamicResource PanelBrush}" ...>
    <Border.Effect>
        <DropShadowEffect BlurRadius="22" ShadowDepth="0" Opacity="0.45" Color="#000000" />
    </Border.Effect>
    ...
</Border>
```

修改后：

```xml
<Border Background="{DynamicResource PanelBrush}" ...>
    <!-- 移除 DropShadowEffect，移除 Margin="16" -->
    ...
</Border>
```

#### 2. 在 C# 中添加 DWM 原生阴影

在 `MainWindow.xaml.cs` 的 `SourceInitialized` 回调中，通过 Win32 API 给无边框窗口加上 DWM 阴影：

```csharp
// ===== P/Invoke 声明（添加到类中或单独文件）=====

[DllImport("user32.dll")]
private static extern int GetWindowLong(IntPtr hwnd, int nIndex);

[DllImport("user32.dll")]
private static extern int SetWindowLong(IntPtr hwnd, int nIndex, int dwNewLong);

[DllImport("dwmapi.dll")]
private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS pMarInset);

[StructLayout(LayoutKind.Sequential)]
private struct MARGINS
{
    public int cxLeftWidth;
    public int cxRightWidth;
    public int cyTopHeight;
    public int cyBottomHeight;
}

private const int GWL_STYLE = -16;
private const int WS_THICKFRAME = 0x00040000;

// ===== 在 SourceInitialized 中调用 =====

private void AddDwmShadow()
{
    var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;

    // 恢复 WS_THICKFRAME，让 DWM 认为这是一个可调整大小的窗口，从而绘制原生阴影
    var style = GetWindowLong(hwnd, GWL_STYLE);
    SetWindowLong(hwnd, GWL_STYLE, style | WS_THICKFRAME);

    // 让 DWM 将 frame 扩展到客户区边缘，触发原生阴影绘制
    var margins = new MARGINS
    {
        cxLeftWidth = 1,
        cxRightWidth = 1,
        cyTopHeight = 1,
        cyBottomHeight = 1
    };
    DwmExtendFrameIntoClientArea(hwnd, ref margins);
}
```

在构造函数的 `SourceInitialized` 事件中调用：

```csharp
SourceInitialized += (_, _) =>
{
    // ... 现有的 WndProc hook 和热键注册代码 ...

    AddDwmShadow();  // 添加此行
};
```

### 优点

- 阴影由系统 GPU 渲染，**不存在**软件渲染的溢出/伪影问题
- 在 Win11 下视觉效果与系统原生窗口一致
- 性能更好，不占用 CPU 做软件模糊

### 注意事项

- 需要 `DwmApi.dll` 和 `user32.dll` 的 P/Invoke 声明
- 恢复 `WS_THICKFRAME` 后系统会认为窗口可拖拽调整大小，但当前窗口已有 `ResizeGrip` 自定义逻辑且 `ResizeMode="NoResize"`
- 如有冲突，可在 `WndProc` 中拦截 `WM_NCCALCSIZE`（消息号 `0x0083`）来消除可调整行为但保留阴影：

```csharp
// 在 WndProc 中添加
const int wmNcCalcSize = 0x0083;
if (msg == wmNcCalcSize)
{
    handled = true;
    return (IntPtr)0; // 阻止系统调整客户区计算
}
```

- 在 Win7 基础主题（无 DWM）下会退化，但 Win11 不存在此问题

---

## 备选方案：纯 XAML 渐变阴影（不引入 P/Invoke）

将 `DropShadowEffect` 替换为多个半透明 `Border` 叠加模拟的渐变阴影：

```xml
<Grid Margin="16">
    <!-- 多层半透明 Border 模拟模糊阴影 -->
    <Border Background="#08000000" CornerRadius="20" Margin="-6" />
    <Border Background="#0A000000" CornerRadius="18" Margin="-4" />
    <Border Background="#0D000000" CornerRadius="16" Margin="-2" />
    <!-- 主面板 -->
    <Border Background="{DynamicResource PanelBrush}" CornerRadius="14" ...>
        ...
    </Border>
</Grid>
```

**缺点**：效果不如 DWM 原生阴影自然，且增加渲染层数。

---

## 总结

| 方案 | 效果 | 性能 | 复杂度 |
|------|------|------|--------|
| DWM 原生阴影（推荐） | 系统原生，最佳 | GPU 加速，最优 | 中等（需 P/Invoke） |
| 多层 Border 渐变阴影 | 一般 | 增加渲染层数 | 低（纯 XAML） |
