# SettingsWindow 滚动条与滑块美化方案

## 现状分析

当前 `SettingsWindow.xaml` 中所有需要美化的交互控件：

| 控件 | 位置 | 当前状态 |
|------|------|----------|
| **ScrollViewer × 4** | 四个 TabItem 内嵌 | 使用系统默认滚动条，与暗色主题格格不入 |
| **ScrollViewer × 1** | ComboBox 下拉列表 | 同上，默认样式 |
| **Slider × 7** | 布局与密度 + 透明度 | 仅设 Foreground/Background，其他为系统默认 |
| **LabelToggle** | 显示方式 / 分组位置 | 已有自定义 Track+Knob，但颜色硬编码 `#38425B` |

---

## 统一设计规范

所有控件配色使用以下语义色，与主题联动：

| 语义色 | 用途 | 暗色默认值 |
|--------|------|-----------|
| `TrackBg` | 滑轨/滚动条背景 | `#38425B`（已是 SurfaceBorder） |
| `ThumbDefault` | 滑块/拇指默认 | `#4A5568` |
| `ThumbHover` | 滑块/拇指悬停 | `#718096` |
| `Accent` | 走过区域/强调 | `#8FA6E8` |
| `ThumbKnob` | 圆形把手 | `#F5F7FC`（已是 TextPrimary） |

---

## 一、ScrollBar（滚动条）美化

### 设计目标

窄式滚动条，融入暗色面板背景，不抢夺视觉焦点。

### 设计规格

```
宽度：          6px
圆角：          3px（Thumb 半高）
滑轨背景：      透明（Track.IsDeferredScrollingEnabled 时 Track 不可见）
滑块默认色：    #4A5568 @ 60% 不透明度
滑块悬停色：    #718096 @ 80% 不透明度
滑块拖拽色：    #8FA6E8 @ 100% 不透明度
最小滑块高度：  30px
重复按钮：      隐藏（宽/高 = 0）
```

### 实现要点

在 `SettingsWindow.xaml` 的 `Window.Resources` 中添加一个**隐式 ScrollBar Style**（`TargetType="ScrollBar"`），影响窗口内所有 ScrollBar。

自定义 `ScrollViewer` 模板不必要 —— 因为 WPF 的 `ScrollViewer` 会查找隐式 ScrollBar 样式并自动应用。

关键 Template 部件：

```
ScrollBar Template:
├── Track（滑轨）
│   ├── Track.DecreaseRepeatButton（上/左按钮）→ 宽高 0，隐藏
│   ├── Track.Thumb（滑块）→ 圆角 Border，动态绑定颜色
│   └── Track.IncreaseRepeatButton（下/右按钮）→ 宽高 0，隐藏
```

---

## 二、Slider（横向调节滑块）美化

### 设计目标

现代扁平化滑块，与暗色主题融合，手感流畅。

### 设计规格

```
滑轨高度：        4px
滑轨圆角：        2px
已走过颜色：      Accent（#8FA6E8）
未走过颜色：      TrackBg（#38425B）
把手形状：        圆形
把手尺寸：        默认 14px，悬停 16px
把手背景：        白色 #FFFFFF
把手边框：        无
把手阴影：        DropShadowEffect BlurRadius=3
```

### 实现要点

替换第 114 行的简易 Slider Style 为完整自定义 Template：

```
Slider Template:
├── Track（两层叠放）
│   ├── 底层：TrackBg 全宽矩形
│   └── 上层：从左到 Thumb 位置的 Accent 色矩形
├── Thumb（圆形把手）
│   └── Ellipse + DropShadowEffect
└── 行为触发器
    ├── IsMouseOver → Thumb 放大到 16px
    └── IsDragging → Thumb 变纯白
```

**注意事项**：

- WPF Slider 的 Track 分为 `PART_Track`，内含 `DecreaseRepeatButton`（走过区）和 `IncreaseRepeatButton`（未走区），两个按钮组合成完整滑轨。
- 实际做法是：`DecreaseRepeatButton` 背景 = Accent，`IncreaseRepeatButton` 背景 = TrackBg，中间用 `Thumb` 分隔。

简化方案（推荐）：直接在 Slider Template 中使用一个底层 Border（TrackBg全宽）+ 一个上层 Border（用 `PART_SelectionRange` 或自定义逻辑控制宽度），避免复杂的 RepeatButton 样式。

**最简单可靠的方式**：

```xml
<Style TargetType="Slider">
    <!-- Thumb 样式 -->
    <Setter Property="Template">
        <ControlTemplate TargetType="Slider">
            <Grid>
                <!-- 滑轨底层（未走过区） -->
                <Border Height="4" Background="#38425B" CornerRadius="2"
                        VerticalAlignment="Center" Margin="0,8"/>
                <!-- 滑轨上层（已走过区）用 Track 控制 -->
                <Track x:Name="PART_Track">
                    <Track.DecreaseRepeatButton>
                        <RepeatButton Command="Slider.DecreaseLarge">
                            <Border Height="4" Background="#8FA6E8" CornerRadius="2"
                                    VerticalAlignment="Center"/>
                        </RepeatButton>
                    </Track.DecreaseRepeatButton>
                    <Track.IncreaseRepeatButton>
                        <RepeatButton Command="Slider.IncreaseLarge">
                            <Border Height="4" Background="Transparent"
                                    VerticalAlignment="Center"/>
                        </RepeatButton>
                    </Track.IncreaseRepeatButton>
                    <Track.Thumb>
                        <Thumb Width="14" Height="14" Cursor="Hand">
                            <Ellipse Width="14" Height="14" Fill="White">
                                <Ellipse.Effect>
                                    <DropShadowEffect BlurRadius="3" ShadowDepth="0" Opacity="0.3"/>
                                </Ellipse.Effect>
                            </Ellipse>
                        </Thumb>
                    </Track.Thumb>
                </Track>
            </Grid>
            <ControlTemplate.Triggers>
                <Trigger Property="IsMouseOver" Value="True">
                    <Setter Property="Width" Value="16" TargetName="PART_Track"/>
                    <!-- 需要改为 Thumb 内部 Ellipse 的 Width -->
                </Trigger>
            </ControlTemplate.Triggers>
        </ControlTemplate>
    </Setter>
</Style>
```

> 注：WPF Slider 的 Thumb 放大触发较复杂，推荐在 Thumb 模板中用 `VisualStateManager` 或在 Thumb 的 Template 中设置 `IsMouseOver` 触发器缩放 Ellipse。

---

## 三、LabelToggle 美化

### 设计目标

开关状态清晰，滑轨颜色跟随主题强调色变化。

### 设计规格

```
滑轨宽度：        46px
滑轨高度：        22px
滑轨圆角：        11px（半高圆形）
关闭状态背景：    TrackBg（#38425B）
开启状态背景：    Accent（#8FA6E8）
把手尺寸：        16px 圆形
把手颜色：        白色 #FFFFFF
把手阴影：        无（保持简洁）
过渡动画：        可选，非必需
```

### 实现要点

修改 `LabelToggle.xaml`，将硬编码的 `#38425B` 替换为可动态绑定的颜色。

**当前问题**：Track 背景色硬编码为 `#38425B`，且开启/关闭状态仅通过 Knob 位置区分，Track 颜色不变。

**改进方案**：

1. 在 Track 的 `Background` 上使用 `DataTrigger` 绑定 `State` 属性
2. 关闭状态 → `#38425B`
3. 开启状态 → `#8FA6E8`（应可从主题读取）

**注意**：LabelToggle 是在 `SettingsWindow` 中实例化的，设置窗口背景是暗色，但主窗口可以通过主题切换变浅色。因此最好让 LabelToggle 使用 DynamicResource 引用主题色，而不是硬编码。

**建议**：在 `SettingsWindow.xaml` 中实例化 LabelToggle 时，改为直接传参或让 LabelToggle 查找父窗口的 Resources。

---

## 四、ComboBox 下拉 ScrollViewer

当前第 104 行：
```xml
<ScrollViewer MaxHeight="220" CanContentScroll="True">
    <ItemsPresenter/>
</ScrollViewer>
```

加上隐式 ScrollBar 样式后，此处的滚动条也会自动美化，无需额外修改。

---

## 五、实施步骤

| 步骤 | 内容 | 文件 |
|------|------|------|
| 1 | 添加隐式 `ScrollBar` Style 到 `SettingsWindow.Resources` | `SettingsWindow.xaml` |
| 2 | 重写 `Slider` Style 为完整 Template | `SettingsWindow.xaml` |
| 3 | 修改 `LabelToggle` Track 背景色支持状态切换 | `LabelToggle.xaml` |
| 4 | 将 `LabelToggle` 硬编码颜色替换为 DynamicResource | `LabelToggle.xaml` |

---

## 六、配色参考示意图

```
ScrollBar（纵向）：
┌──┐
│  │ ← 6px 宽
│██│ ← Thumb 圆角 3px，#4A5568
│  │
│  │
└──┘

Slider（横向）：
●══════════════════════  ← ●=Thumb 14px 白圆
████████░░░░░░░░░░░░░░  ← ██=已走过 Accent，░░=未走过 TrackBg

LabelToggle：
关闭：[○════]  ← TrackBg #38425B，○白色 Knob 靠左
开启：[════●]  ← Accent #8FA6E8，●白色 Knob 靠右
```
