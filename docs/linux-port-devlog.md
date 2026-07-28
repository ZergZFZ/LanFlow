# LanFlow Linux 版移植开发记录

> 状态：**全功能迁移完成**（WPF → Avalonia 11.3.18）。UI 与全部 Windows 专属服务均已改写为 Linux 等价实现，待 UOS 实机验证。
> 更新日期：2026-07-27
> 目标机：UOS Desktop 20 Professional（统信），x86_64

---

## 1. 背景与目标

LanFlow 当前是 Windows 独占的 WPF 程序（`net8.0-windows`）。用户希望在 **UOS（统信）Linux 桌面**上运行，因此做的是一次**真实移植**（Avalonia），而非重新打包 Windows 产物。

**关键约束（来自目标机采集报告 `uos_sysinfo_report_20260727_151207.txt`）**

| 项 | 值 | 对打包的影响 |
|---|---|---|
| 系统 | UOS Desktop 20 Pro (eagle)，Debian 系 | 走 `.deb` 优先（本次先出压缩包验证） |
| 架构 | x86_64 | `linux-x64` |
| 内核 / glibc | 4.19 / **glibc 2.28** | 产物链接 glibc 必须 ≤ 2.28（.NET 8 运行时要求 ≥ 2.23，满足） |
| 桌面 | Deepin / **X11（无 Wayland）** | Avalonia 走 X11 后端 |
| 语言 | zh_CN.UTF-8 | 需保证 CJK 字体回退 |
| 工具链 | 有 gcc 8.3，无 g++/cmake | 无关紧要——在 Windows 开发机交叉编译 |
| 磁盘 | `/` 剩 3.7GB，`/home` 787GB | 自包含运行时约 38MB，足够 |

---

## 2. 方案选型

- **Avalonia 11.3.18**（非 MAUI / Qt）：XAML 语法与 WPF 极相似，移植成本最低；官方 `linux-x64` X11 后端；自带 `TrayIcon`、`OpenFileDialog` 等跨平台控件；自带成像管线替代 `System.Drawing.Common`（后者在 Linux 会抛异常）。
- 在 **Windows 开发机交叉编译** `dotnet publish -r linux-x64 --self-contained`，产物自带 .NET 8 运行时，目标机无需安装 SDK/运行时。

---

## 3. 当前进度（全功能迁移，本次提交范围）

- 项目 `native/LanFlow.Linux`（Avalonia 11.3.18，TFM `net8.0`，`AssemblyName=LanFlow`）。
- **复用的纯 C# 业务/数据层（原样或极小改动）**
  - `ViewModels/MainViewModel.cs`（INotifyPropertyChanged，搜索/分组/排序/外观应用/保存，原样）
  - `Models/AppConfig.cs`（`ImageSource?`→`Avalonia.Media.IImage?`；`LauncherItem` 增加 `Command/Kind/Hotkey/IsEnabled`；`Settings`/`ThemeColors` 增加 `Clone()` 与 10 色主题字段）
  - `Services/ConfigStore.cs`（JSON 读写，落盘 `~/.config/LanFlow/config.json`，原子写，原样）
  - `Services/LauncherService.cs`（Linux 启动：可执行文件直接 `Process.Start`；URL/目录/`.desktop` 走 `xdg-open`；命令项走 `bash -c`）
- **重写的 Windows 专属服务（→ Linux 等价）**
  - `Services/ShellIconService.cs`：返回 `Avalonia.Media.IImage?`；解析 `.desktop` 的 `Icon=` 字段并在 freedesktop 图标主题目录查找；含静态 `ParseDesktop(path)` → `(Name, Exec, Icon)`。
  - `Services/ShortcutService.cs`：`.desktop` → 解析 `Exec`；显示名去 `.desktop` 后缀。
  - `Services/StartupService.cs`：开机启动写/删 `~/.config/autostart/lanflow.desktop`（识别 `Hidden` / `X-GNOME-Autostart-enabled`）。
  - `Services/HotkeyService.cs`：X11 全局热键（`XGrabKey` + 独立线程 `XNextEvent`），非 X11 会话静默降级返回 false。
- **UI（全部 Avalonia XAML 改写，含 WPF 的自定义模板）**
  - `App.axaml` / `App.axaml.cs`：Fluent 主题 + 动态画刷资源字典（PanelBrush/SurfaceBrush/…）；`ApplyThemeColors` 写资源字典；`SetupTray` 建托盘；`CreateTrayIcon` 用 `unsafe` 指针填充 32×32 BGRA 位图。
  - `MainWindow.axaml(.cs)`：DockPanel（搜索顶栏/底栏/分组栏/ItemsControl）；Tile/Card 两套 `DataTemplate`（首字符占位、图标、热键角标、删除按钮）；编辑模式拖拽重排、单击/双击启动、分组切换、设置/编辑/增删、LoadIcons、ApplyMetrics（写资源字典 IconSize/TextSize/CardSize/ShowTitle/ShowBadge/EditMode/ContentPadding/ItemMargin + Dock 切换）。
  - `Views/SettingsWindow.axaml(.cs)`：编辑 Settings 副本；10 色主题行、主题切换、自启动（`StartupService`）、热键规范（`HotkeyService.TryNormalize`）；`OnApplied` 回调。
  - `Views/EditItemWindow.axaml(.cs)`：`InitializeDialog(LauncherItem)` 编辑副本；`OpenFilePicker` 解析 `.desktop`；类型切换显示路径/命令。
  - `Views/EditGroupWindow.axaml(.cs)`：`GroupName` 属性 + `Confirmed`。
  - `Views/ColorPickerWindow.axaml(.cs)`：HSV 取色器（SV Canvas + 色相 Slider + HEX/RGB NumericUpDown）。
  - `Views/LabelToggle.axaml(.cs)`：`Title/Description/IsChecked` 样式属性 + `IsCheckedChanged` 事件。
  - `Views/Converters.cs`：`FirstCharConverter` / `NotNullConverter` / `NotEmptyConverter`。
- **`Windows 侧 dotnet build -c Release` 通过（0 错误）**；`dotnet publish -r linux-x64 --self-contained` 通过。
- **Windows 冒烟运行通过**：应用能完成 Avalonia 初始化、加载全部 XAML、创建 MainWindow 并进入托盘消息循环（无异常退出）。Linux 专属服务在无 X11/无 libX11 环境会优雅降级（热键停用、图标回退），故 Windows 冒烟可验证 UI 加载链路。

---

## 4. 已踩的坑（环境与 Avalonia 11 相关，勿重复）

1. **Avalonia 12.x 在本机不可用**：开发机 .NET SDK = 8.0.423（Roslyn 4.11）。Avalonia 12 的 XAML 源生成器要求 Roslyn ≥ 4.14，报 `CS9057` 且 `.axaml` 不编译。**已降到 Avalonia 11.3.18**（与 SDK 8 兼容）。Avalonia 12 需 .NET 9/10 SDK（本机未装）。
2. **`StartWithClassicDesktopLifetime` 是非泛型**：正确写法 `BuildAvaloniaApp().StartWithClassicDesktopLifetime(args)`，不要写 `<App>` 泛型重载。`AppBuilder.Instance` 的 setter 是 internal，不能赋值。
3. **`ControlTheme` 不能直接加入 `Styles` 集合**：App.axaml 里用 `<Style Selector="Button">` / `<Style Selector="Button:pointerover">` / `<Style Selector="ToggleButton">` / `<Style Selector="TextBox">` / `<Style Selector="ComboBox">` / `<Style Selector="Window">` 形式（而非 `<ControlTheme TargetType=...>`），否则运行时 `XamlX` 抛 `ControlTheme cannot be added to a Styles collection`。
4. **`ILockedFramebuffer` 无 `Memory`/`Span`**：托盘图标位图填充需 `AllowUnsafeBlocks=true` + `unsafe` 指针 `framebuffer.Address.ToPointer()`。
5. **`DataObject` / `DragDrop.DoDragDrop` / `DragEventArgs.Data` 已过时**：Avalonia 11 仍有兼容层（CS0618 警告），首参为 `PointerEventArgs`；功能可用，仅警告。
6. **NumericUpDown 值为 `decimal?`**：与 `double` 互转需显式 `(decimal)` / `(double)Value.GetValueNullable()`；Slider 为 `double`。
7. **`CapturePointer`/`ReleasePointerCapture` 在 Avalonia 11 的 `Border`/`Control` 上不存在**：删除指针捕获调用，依赖 `DragDrop.DoDragDrop` 自带行为。

---

## 5. 本次交付物（待拷贝到 UOS 验证）

位置：`E:\AI\LanFlow-main\release\linux\`

| 文件 | 大小 | 说明 |
|---|---|---|
| `LanFlow-linux-x64.tar.gz` | ~38.1 MB | 自包含目录压缩（推荐，Linux 原生） |
| `LanFlow-linux-x64.zip` | ~38.5 MB | 同上，便于 Windows 侧拷贝 |

压缩包内含：
- `LanFlow`（ELF 主程序，自包含 .NET 8 运行时）
- 全部 `.NET` 程序集 + 原生 `.so`（libcoreclr / libSkiaSharp / libHarfBuzzSharp / libSystem.Native 等）
- `lanflow.sh`：启动脚本（LF 行符，自动切目录、`chmod +x LanFlow` 后启动）
- `lanflow.desktop`：桌面入口模板（需把 `Exec=` 改为实际绝对路径后放入 `~/.local/share/applications/`）

> 注：`release/linux/` 未被 git 跟踪（构建产物），不会进仓库历史。

---

## 6. 在 UOS 测试机上验证步骤

```bash
# 1) 拷贝压缩包到测试机（任选其一），解压
mkdir -p ~/lanflow && tar -xzf LanFlow-linux-x64.tar.gz -C ~/lanflow
cd ~/lanflow

# 2) 确保可执行（NTFS 拷过来可能丢 +x；脚本已自动 chmod，再保险一次）
chmod +x LanFlow lanflow.sh
# 若 lanflow.sh 报 "bad interpreter"，先转换行符：
#   sed -i 's/\r$//' lanflow.sh

# 3) 运行（需 X11 会话 / 有显示器）
./lanflow.sh
# 或 ./LanFlow
```

预期：
- 启动后显示主窗口（Fluent 主题，圆角卡片/磁贴网格），顶部搜索栏 + 底部状态栏 + 左侧/顶部分组栏。
- 托盘出现 LanFlow 图标；关闭窗口后常驻托盘，点托盘可重新打开。
- 设置里可改 10 色主题、切换深浅色、设置全局热键（X11 会话下生效）、勾选开机启动（写入 `~/.config/autostart/lanflow.desktop`）。
- 编辑项目时文件选择走 Avalonia `OpenFileDialog`；选 `.desktop` 会解析出真实 Exec 与图标。

可能的问题与排查：
- **“error while loading shared libraries: libX11.so.6”**：`sudo apt install libx11-6`（UOS 一般已带）。
- **“version 'GLIBC_2.xx' not found”**：理论上不应发生（.NET 8 支持 glibc ≥ 2.23；SkiaSharp 基于 manylinux2014 ≈ glibc 2.17）。若出现，记下具体符号回报。
- **无显示器 / DISPLAY 未设**：报无法打开 display，属正常，需在图形会话下运行。
- **CJK 字体**：中文应正常显示（依赖系统字体）。若方块，安装 `fonts-noto-cjk` 或系统自带文泉驿。
- **全局热键不生效**：仅在 X11 会话有效；若在 Wayland/无 Display 会静默降级（状态栏提示「X11 会话不可用，全局热键已停用」）。

验证后请把结果（能否启动、窗口是否正常、热键/托盘/自启动/主题是否生效、报错文本）反馈，以便修复。

---

## 7. Windows 专属代码的 Linux 等价（已全部完成）

| 原文件 | 内容 | Linux 等价实现 | 状态 |
|---|---|---|---|
| `Services/ShellIconService.cs` | `SHGetFileInfo` + `System.Drawing.Icon` | freedesktop 图标主题查找 + `Avalonia.Media.IImage` | ✅ 完成 |
| `Services/HotkeyService.cs` | `RegisterHotKey`(user32) | X11 `XGrabKey` + `XNextEvent` 线程，非 X11 降级 | ✅ 完成 |
| `Services/StartupService.cs` | 注册表开机启动 | `~/.config/autostart/lanflow.desktop` | ✅ 完成 |
| `App.axaml.cs` 托盘 | `NotifyIcon` | `Avalonia.Controls.TrayIcon` + `NativeMenu` | ✅ 完成 |
| `MainWindow` | DWM 阴影 + Win32 命中测试拖拽 | Avalonia 原生装饰 + `DragDrop` 拖拽；阴影交给 WM | ✅ 完成 |
| `EditItemWindow` | `OpenFileDialog` / `.lnk` | Avalonia `OpenFileDialog` + `.desktop` 解析 | ✅ 完成 |
| `Views/*.xaml` ×5 | WPF XAML（含自定义模板） | Avalonia XAML 改写（Slider/ComboBox/TextBox 等） | ✅ 完成 |

---

## 8. 后续打包建议（验证通过后）

- 出 `.deb`：将自包含目录放入 `/opt/lanflow`，提供 `/usr/share/applications/lanflow.desktop` 与图标；可选 `dpkg-deb --build` 或 dotnet-packaging。
- 开机启动：写入 `~/.config/autostart/lanflow.desktop`（已由 `StartupService` 实现）。
- 版本号：建议 Linux 版从 `1.3.2` 起对齐 Windows 主线，或在 `csproj` 单设 `<Version>`。

---

## 9. 风险点（已知，待实机确认）

1. **全局热键**：Linux 无统一 API；X11 下 `XGrabKey` 可用（已验证降级逻辑）；Wayland 暂不支持（本机是 X11，可接受）。
2. **`.lnk` → `.desktop`**：Windows 快捷方式对应 Linux 桌面入口文件，已用 `.desktop` 解析替代；Windows 的 `.lnk` 在 Linux 无对应，需用户在 Linux 侧重新添加 `.desktop` 或可执行文件。
3. **外观差异**：Windows 那套无边框 + acrylic + DWM 阴影在 Linux 不可复现，已改用 Deepin 原生装饰窗口（更稳、与桌面一致）。
4. **图标回退**：若系统图标主题找不到 `.desktop` 的 `Icon`，使用首字符占位块（与无图标项一致），不崩溃。

---

## 10. UOS 实机首测问题与修复（2026-07-28）

用户已在 UOS 实机启动成功（UI 正常），但反馈以下症状，已在 `native/LanFlow.Linux` 修复并通过 `dotnet build -c Release` 0 错误：

| 现象 | 根因 | 修复 |
|---|---|---|
| 图标不能从文件管理器拖进窗口 / 拖过来没有焦点 | `ItemsControl` 只接了 `DragDrop.Drop`，没接 `DragDrop.DragOver`；X11 下没有 DragOver 设置 `DragEffects` 时外部拖放会被判为「禁止」而取消；且只处理内部 `"item"` 拖拽，完全不识别外部文件 | `ItemsControl` XAML 加 `DragDrop.DragOver="OnItemsDragOver"`；新增 `OnItemsDragOver`/`OnGroupTabDragOver` 在悬停时回 `DragDropEffects.Copy`；`OnItemsDrop`/`OnGroupTabDrop` 现支持 `DataFormats.FileNames`，把拖入的 `.desktop`/可执行文件/目录转为 `LauncherItem`（`ShellIconService.ParseDesktop` 取名称与图标） |
| 没有分组 / 新建分组也没用 | 新建的是**空分组**且唯一的入口是拖放（不通），整体像「没生效」；另外 `OnAddItem` 在 `SelectedGroup==null` 时会静默丢失添加 | 拖文件到主区：无分组时自动建「我的应用」分组；分组标签也接外部拖放；`OnAddItem` 无分组时自动建默认分组；`OnAddGroup`/`OnAddItem` 包 `Save()` 容错并写入 `StatusText` 反馈（如「已新建分组：新分组」「已添加 N 个项目到『x』」） |
| 设置透明度会卡死页面，但重启后设置成功 | 窗口创建时未开启透明视觉（`TransparencyLevelHint`）；运行时改 `Window.Opacity` 会触发 X11 窗口 visual 重建 → 卡死；而启动时只设一次不重建，所以重启后正常 | `MainWindow` 构造函数设 `TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent }`，窗口以 32 位 ARGB 视觉创建，运行时改透明度不再重建 → 不卡死（根背景 `PanelBrush` 不透明，opacity=1 时外观不变） |

> 注：`DataFormats.FileNames` 等 API 在 Avalonia 11.3 标 `[Obsolete]`（为 12 预留），但 11.3.18 仍可正常运行，故保留。
> 打包（`release/linux/LanFlow-linux-x64.*`）需用户手动 `dotnet publish -r linux-x64 --self-contained` 后重打（本次未自动打包，待用户确认）。

### 10.1 设置窗口滚动 / 默认热键失效（2026-07-28 二次反馈）

- **设置页面拉不到底、快捷键看不全**：`SettingsWindow` 固定 `Height="600"`，在 UOS 小屏上超出可视工作区，底部（快捷键区）与滚动条被裁掉且够不到。→ 构造函数用 `Screens.Primary.WorkingArea.Height * 0.9` 钳制 `MaxHeight`（下限 360 / 上限 900），并设 `CanResize="True"`，保证底部可见且可滚动。
- **默认热键没生效**：默认 `Alt+Space` 通常被 UOS/Deepin 窗口管理器占用（窗口菜单），`XGrabKey` 被动抓取静默失败；原 `Grab()` 未检查返回值。→ ① 默认热键改为 `Ctrl+Alt+Space`（避开 WM/IME 占用，IME 只占 `Ctrl+Space`）；`ConfigStore.Normalize` 对空热键与旧默认 `Alt+Space` 做一键迁移到新默认。② `HotkeyService.Grab()` 现检查 `XGrabKey` 返回（0=成功），`Register`/`TryRegister` 失败时设置 `LastError`（区分「X11 不可用」「被占用」），`EnableHotkey` 把 `LastError` 写入状态栏提示。UI 示例文案同步改为 `Ctrl+Alt+Space`。
- `dotnet build -c Release` 0 错误。

