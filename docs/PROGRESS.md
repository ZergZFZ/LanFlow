# LanFlow Linux 进度与交接（round14，已提交 · 版本 1.4.9）

> 本文档供其他 agent / 开发者接手时快速对齐：任务来源、已完成改动、关键判断、踩坑记录、当前状态与待办。
> 最近更新：2026-08-14（分支 `linux`，已提交并推送）

## 1. 任务来源

round13 实机通过后，用户提出 4 项 UI/健壮性优化：

- **问题 1**：设置按钮、编辑按钮等按钮太紧凑，左右边框几乎与文字重叠，需统一美化按钮风格与大小。
- **问题 2**：任务栏（托盘）图标仍是黑框，不是软件图标。
- **问题 3**：托盘图标右键新增「重启软件」，以防快捷键失效时还要手动去启动。
- **问题 4**：快捷键时不时注册失败，需再次优化注册逻辑。

## 2. 根因（已定位）

- **按钮紧凑**：`App.axaml` 全局 Button 模板里 `Border` 未绑定 `Padding`，导致按钮 `Padding` 属性不生效、文字紧贴边框；且按钮是白底深字（`#FFFFFF/#1E2533`），与深色主题不搭。
- **托盘黑框**：`App.axaml.cs` 的 `CreateTrayIcon()` 返回的是纯色方块位图（`#35405E`），不是随包发布的 `lanflow.png`（512×512 ARGB）。
- **快捷键偶发失败**：`HotkeyService.OnXError` 读 `XErrorEvent.error_code` 的偏移错误——x86_64 下应为 **32**（type 4 + pad 4 + display 8 + resourceid 8 + serial 8），代码却读偏移 **24**（serial 低字节），导致 BadAccess(10) 被当成随机的 serial 字节，偶发「误报失败/误报成功」。

## 3. 已完成改动（均未提交）

### 3.1 按钮美化（问题 1）
- `App.axaml`：Button 模板 `Border` 补 `Padding="{TemplateBinding Padding}"`；配色改为主题感知（`Background=SurfaceBrush` / `Foreground=TextPrimaryBrush` / `BorderBrush=SurfaceBorderBrush`），hover=`HoverBrush`、pressed=`AccentBrush`；`Padding=16,0`、`Height=34`。

### 3.2 托盘图标（问题 2）
- `App.axaml.cs`：`CreateTrayIcon()` 改为加载 `lanflow.png` 并 `CreateScaledBitmap` 缩放到 32×32（`BitmapInterpolationMode.HighQuality`）；失败回落纯色方块 `CreateSolidIcon()`。

### 3.3 托盘「重启软件」（问题 3）
- `App.axaml.cs`：`SetupTray` 菜单新增「重启软件」（在「显示/隐藏」与「退出」之间）。
- `MainWindow.axaml.cs`：新增 `Restart()`——`Process.Start(Environment.ProcessPath)` 拉起新进程后 `Quit()` 退出当前进程。

### 3.4 快捷键优化（问题 4）
- `HotkeyService.cs`：`OnXError` 错误码偏移 24 → 32，正确判定 BadAccess 占用。
- `MainWindow.axaml.cs`：注册失败后启动自愈定时器（`DispatcherTimer` 每 15s 用 `TryRegister` 重试，成功即停并清状态栏提示）；`Quit` 停定时器；设置变更重注册失败也进入自愈。

## 4. 关键判断 / 决策

- **版本升 1.4.9**：round14 收口时与 Windows 主线对齐，`Version`/`AssemblyVersion`/`FileVersion` 同步 1.4.9。
- **重打 `.deb`**：`lanflow_1.4.9_amd64.deb`（round14 产物）。
- **D6 冻结已排除**（round13 确认）：恢复 `Transparent` 后设置窗口缩放不卡死。
- **`CardSize` 字段保留未删**（历史判断，维持）。

## 5. 当前状态

- **已提交并推送**（round13+round14 + 版本 1.4.9）：`MainWindow.axaml`、`MainWindow.axaml.cs`、`SettingsWindow.axaml`、`SettingsWindow.axaml.cs`、`App.axaml`、`App.axaml.cs`、`HotkeyService.cs`、`LanFlow.Linux.csproj`、`docs/PROGRESS.md`、`docs/RELEASE-NOTES.md`、`.gitignore`、`tools/run-vm-test.*`、`tools/vm-verify.sh`。
- 已提交历史：`d778f2f`、`49cda30`、`9ed1cd8`（round12）。
- `.build/`、`artifacts/` 已加入 `.gitignore`（本地构建/VM 共享产物，不入库）。
- 发布产物：`release/LanFlow-linux-x64-round14.tar.gz` + `release/lanflow_1.4.9_amd64.deb`。

## 6. 产物路径

| 产物 | 路径 |
|---|---|
| tar.gz 发布包 | `E:\AI\LanFlow-main\release\LanFlow-linux-x64-round14.tar.gz` |
| 解压目录（VM 共享直连） | `E:\AI\LanFlow-main\.build\linux-wt\publish\final-r14\`（VM 里对应 `/mnt/hgfs/lanflow/final-r14/`） |

打包（Windows 侧；GNU tar 在 DSH 沙箱下不可用，改用 .NET `System.Formats.Tar` 打包工具 `tools/packtool/`）：
```bash
dotnet publish native/LanFlow.Linux/LanFlow.Linux.csproj -c Release -r linux-x64 --self-contained -o .build/linux-wt/publish/final-r14
dotnet run --project tools/packtool/pack.csproj -- .build/linux-wt/publish/final-r14 release/LanFlow-linux-x64-round14.tar.gz
```
> 包内需含 `RELEASE-NOTES.md`（`vm-verify.sh` 的 B4-2 检查项）与 `lanflow.desktop` / `collect-env.sh`（从 `release/usb-test/` 拷入）。
> **dpkg 兼容性（两次踩坑，务必保持）**：packtool 必须输出 **USTAR**（`TarEntryFormat.Ustar`，非 PAX——UOS dpkg 报「不支持的 PAX tar 头部类型 'x'」）+ **目录条目**（先写目录再写文件——否则全新安装报「没有那个文件或目录」）。
> **.deb 的 .desktop（第三次踩坑）**：`.deb` 的 `/usr/share/applications/lanflow.desktop` 必须用 `tools/packtool/debian/lanflow.desktop`（`Exec=/opt/lanflow/LanFlow` + `Icon=/opt/lanflow/lanflow.png`），**不要**用 `release/usb-test/lanflow.desktop`（那是 U 盘模板，`Exec` 是占位符、`Icon` 被注释，装上后菜单无图标）。tar.gz 的 `lanflow.desktop` 才是 U 盘模板（用户自行改路径）。

## 7. 待办

- [x] 提交 round13+round14 改动（版本 1.4.9）。
- [x] VM 复测：A/B/D/F 通过，C/G/H 部分通过，E 托盘重启未自动验证；见 `release/TEST-CARD-r14.md`「结果汇总」与「VM 自动化验证记录」。
- [x] 把 `.build/`、`artifacts/` 加入 `.gitignore`。
- [x] 升版本 1.4.9 并重打 `.deb`。
- [ ] （遗留）UOS 实机终验：C 按钮视觉细节 / D 托盘精细外观 / E 托盘重启 / G 分层透明·2×2 预览（VM 不可信项）。

## 8. 关键文件定位

| 文件 | 说明 |
|---|---|
| `native/LanFlow.Linux/App.axaml` | 全局样式（Button 模板/配色、ToggleButton、TextBox 等） |
| `native/LanFlow.Linux/App.axaml.cs` | `CreateTrayIcon`、`SetupTray`（托盘菜单）、`ApplyThemeColors` |
| `native/LanFlow.Linux/MainWindow.axaml` | 主窗口布局（顶栏/底栏按钮、ItemsHost） |
| `native/LanFlow.Linux/MainWindow.axaml.cs` | `EnableHotkey`/自愈、`Restart`、`Quit`、`ApplyMetrics` |
| `native/LanFlow.Linux/Services/HotkeyService.cs` | X11 全局热键（错误码偏移、抓取变体、单线程模型） |
| `native/LanFlow.Linux/Views/SettingsWindow.axaml(.cs)` | 设置面板（滑块单行 + 置顶预览 + 2×2 预览） |
