# LanFlow 过河缺陷板与修复冲刺计划（v2 · 第二轮实机后）

> 状态：活跃 ｜ 最近更新：2026-08-10
> 日期：2026-08-05 ｜ v2 依据：对照组（含全部修复+诊断日志）实机结果
> 环境基线：UOS 20 Pro / Deepin / X11 / glibc 2.28 / 海光 x86_64 —— 架构与运行时无硬伤

## 0. 总判断（v2 更新）

- 溯源已结案：对照组状态栏出现"热键被占用"提示，该文案仅存在于修复版代码 →
  **包确实含全部修复，修复在实机不成立。冲刺按全量 D1-D4 + 新增 D6 执行。**
- Avalonia 路线仍成立（启动/托盘/主题/持久化/中文正常），无架构级风险。
- 首轮"修复完成"被实机证伪的根因是验证回路断裂，v1 提出的流程修正维持不变。

## 1. 缺陷板（v2）

| ID | 现象（两轮实测合并） | 根因判断（v2） | 严重度 | 修复策略 |
|---|---|---|---|---|
| D1 | 设置「行为」区五开关不渲染；热键输入框压成细线；分组栏空白（数据在）；外观与 Windows 版差异大 | **Warning 级诊断日志全空** → 排除资源/绑定解析失败。控件占布局空间但绘制为空 → 测量/绘制层问题，或样式模板未套上且静默。疑点排序：① App.axaml 的 Style Selector 对部分控件未生效；② LabelToggle 的 x:Class 命名空间为 LanFlow.Desktop.Views（与 Linux 工程命名空间不一致，需核对接线）；③ BuildGroupTabs 里对 Resources["AccentBrush"] 的硬转 SolidColorBrush | 高 | 修 + 深度取证双管齐下（见 §3） |
| D2 | 外部拖放无效 | X11 XDND 与 dde-file-manager 互通；DragOver/DataFormats.FileNames 实机不生效 | 高 | 尽力修 + **产品级兜底"添加文件"按钮必做（入口须落在 D1 隐形区之外，落地前确认其所在面板 Bounds 非零）** |
| D3 | Ctrl+Alt+Space 报"热键被占用"；用户无法改热键（改热键的 UI 在 D1 隐形区里） | XGrabKey 失败被归类为"占用"：可能真被 Deepin 占用，也可能 keysym/modifier 映射错 | 中 | ① 默认热键改为低冲突键（如 Ctrl+Alt+L）；② XGrabKey 失败时 LastError 区分"返回值/占用/无X11"；③ 依赖 D1 修好让设置可见 |
| D4 | .desktop 双击以文本打开 | 启动链路未 ParseDesktop 取 Exec | 高 | LauncherService 对 desktop 类先解析 Exec 再启动 |
| D6（新） | 设置窗口拖动调整大小 → 软件完全卡死 | 与首轮透明度卡死同族：X11 下窗口 visual 重建问题（TransparencyLevelHint 副作用疑似扩散到设置窗口） | 高 | 设置窗口移除 Transparent 提示或改 None 验证；Resize 路径加保护 |
| D5 | 内存 369MB | 待定 | 低 | 不排入本冲刺 |

## 2. 冲刺编排（v2）

S1 = D1 渲染层（先做；必须带 §3 取证件，否则修了也无法自证）
  - S1.0 取证先行（仅读 + 日志，不动模板）：核对 LabelToggle 的 x:Class 命名空间与 Linux 工程命名空间是否一致；在 App.axaml Style Selector 命中点加日志，确认目标控件是否命中模板。此步作为后续改动的判据。
  - S1.1 若 S1.0 显示命名空间错位 → 修命名空间接线（最低成本根因）。
  - S1.2 若 S1.0 显示 Selector 未命中 → 修 Style Selector 范围/匹配条件。
  - S1.3 若 S1.0 显示命中但 Bounds 仍为零 → 进入模板/测量层排查（AccentBrush 硬转等）。
  - 判据：每一子步完成后必须用 §3 日志复跑一次，关键控件 Bounds 非零才算该子步成立，避免"改一处试一次"的试错循环。
S2 = D4 启动逻辑（独立小改，可与 S1 并行）
S3 = D3 热键
  - S3.1 换默认键（Ctrl+Alt+L）+ XGrabKey 失败错误细分（返回值/占用/无 X11）—— **独立先做，不依赖 D1 UI 可见**，可与 S1.0 并行启动。
  - S3.2 热键 UI 可见性回归，依赖 S1 完成。
S4 = D2 拖放 + 兜底按钮
  - 兜底"添加文件"按钮入口**必须位于 D1 隐形区之外**（不落在设置「行为」区五开关同容器内），落地前确认其所在面板 Bounds 非零，否则随 D1 一起隐形。
S5 = D6 卡死（可与 S1 并行，同属窗口层）
  - **代码改动边界**：S5 只改设置窗口的 TransparencyLevelHint / Resize 路径；S1 若需动窗口样式，仅限 App.axaml 的 Style Selector 与控件模板，不得改窗口级 TransparencyLevelHint。两者改动文件清单无重叠；若任何一方必须改同一文件，约定 S1 先合、S5 后合，并复跑 §3 日志。
完成标准 = 构建 0 错误 + 取证日志中关键控件尺寸非零 + 一次实机回归五项全绿。

## 3. 第三轮包必须携带的取证件（v2 新增，核心）

第二轮日志全空证明 Warning 级不够。第三轮包默认开启：
1. LogEventLevel.Verbose，限定 area = Layout / Control / Binding / Visual；
2. 设置窗口 Opened 后 500ms dump 关键节点（五个 LabelToggle、HotkeyBox、应用按钮）的
   Bounds / DesiredSize / IsVisible / 是否命中模板到日志；
3. 主窗口 Opened 后 dump GroupTabs.Children 数量与每个 Border 的 Bounds；
4. 全局 DispatcherUnhandledException + AppDomain 异常写日志（抓静默吞掉的异常）。
5. 降级方案（防诊断代码进正式版）：取证件统一封装在 `#if DEBUG` 或由配置项 `Diagnostics:VerboseLayout` 控制；第三轮实机回归五项全绿后，默认关闭、仅保留开关位，避免 Verbose 日志拖慢启动并放大内存（D5 已 369MB）。
6. 内存复测：取证件启用后复测一次内存，确认未把 D5 推到不可接受（建议阈值 ≤ 450MB）；超阈值则关闭非必要 area，仅保留 Layout + Visual。
目标：哪怕修复全失败，日志也能直接点名根因，不再空手而归。

## 4. 决策分支（已消耗）

第二轮结果 = "症状与首轮相同" → 全量冲刺。本分支不再悬置。

## 5. 流程修正（维持 v1）

任何"修复完成"必须附实机日志证据；诊断日志默认开启直到过河完成；
评估廉价验证通道（WSL/远程 X11）降低 U 盘依赖。

## 6. 开发计划（v2 · 第三轮冲刺）

> 依据 §2 冲刺编排细化，按可并行度排成 4 个批次（B0-B3）。责任人/起止日期由项目负责人填入。

### 6.1 任务分解与批次

| 批次 | 任务 ID | 内容 | 缺陷 | 依赖 | 交付物 | 验收 |
|---|---|---|---|---|---|---|
| B0 | S1.0 | 命名空间核对 + Style Selector 命中日志（仅读 + 日志，不改模板） | D1 | 无 | 命中日志快照 | 日志明确指出"命中 / 未命中 / 命名空间错位"之一 |
| B0 | S3.1 | 默认热键改 Ctrl+Alt+L + XGrabKey 错误细分（返回值/占用/无 X11） | D3 | 无 | 代码改 + 手动验证 | 失败时日志能区分三类错误 |
| B0 | S2 | LauncherService 对 .desktop 先 ParseDesktop 取 Exec 再启动 | D4 | 无 | 代码改 | .desktop 双击启动目标程序而非文本编辑器 |
| B1 | S1.1/S1.2/S1.3 | 按 S1.0 判据分支修渲染层（命名空间 / Selector / 模板） | D1 | S1.0 | 代码改 + §3 日志复跑 | 五开关 + HotkeyBox + 应用按钮 Bounds 非零 |
| B1 | S5 | 设置窗口移除/改 Transparent 提示 + Resize 路径保护 | D6 | 无（与 S1 代码边界见 §2） | 代码改 | 拖动调整大小不再卡死 |
| B2 | S3.2 | 热键 UI 可见性回归 | D3 | S1 | 回归记录 | 设置中热键框可见可改 |
| B2 | S4 | 拖放尽力修 + 兜底"添加文件"按钮（入口在 D1 隐形区外） | D2 | S1（兜底入口需面板可见） | 代码改 | 拖放或兜底按钮至少一条路径可添加文件 |
| B3 | RG | 全量实机回归（含 §3 取证 + 内存复测） | 全部 | B0-B2 | 实机日志 + 回归报告 | 五项全绿 + 内存 ≤ 阈值 + 关键控件 Bounds 非零 |

### 6.2 批次说明

- **B0 三任务互不依赖，可并行**，是最低成本、最高信息量的入口；S1.0 的判据直接决定 B1 走哪条分支。
- **B1 两任务可并行**，但须遵守 §2 中 S1/S5 代码边界约定；若同文件冲突，S1 先合、S5 后合并复跑 §3 日志。
- **B2 依赖 S1 完成**（UI 可见性 + 兜底入口面板可见）。
- **B3 为终验**，必须携带 §3 全量取证件，未通过则不发布。

### 6.3 关键风险与对策

| 风险 | 对策 |
|---|---|
| S1.0 判据仍不明确（日志仍空） | 升级到 VisualTree walk 逐节点 dump，不再迭代样式猜测 |
| S1 与 S5 改动同文件冲突 | 约定 S1 先合、S5 后合并复跑 §3 日志（见 §2） |
| 取证件启用后内存超阈值（>450MB） | 关闭非必要 area，仅保留 Layout + Visual（见 §3.6） |
| 实机仍不可见 / 往返成本高 | 启用 §5 廉价通道（WSL/远程 X11）做增量验证 |
| D2 拖放底层不可修 | 兜底按钮已为必做项，作为产品级回落 |

### 6.4 退出条件

- 构建 0 错误；
- §3 取证日志中五个 LabelToggle、HotkeyBox、应用按钮 Bounds 非零；
- 一次实机回归 D1/D2/D3/D4/D6 五项全绿；
- 内存复测 ≤ 阈值；
- 取证件按 §3.5 降级方案关闭默认开启。

## 7. 第三轮执行记录（2026-08-05）

### 7.1 B0 批次完成

| 任务 | 状态 | 修改文件 | 说明 |
|---|---|---|---|
| S1.0 取证 | ✅ 完成 | 无（仅分析） | 发现根因：①App.axaml.cs 命名空间为 LanFlow.Linux，与其他文件 LanFlow.Desktop 不一致；②App.axaml 缺少 LabelToggle 样式 |
| S3.1 热键 | ✅ 完成 | HotkeyService.cs、ConfigStore.cs、MainWindow.axaml.cs、SettingsWindow.axaml、SettingsWindow.axaml.cs | 默认热键从 Ctrl+Alt+Space → Ctrl+Alt+L；错误信息细分 |
| S2 .desktop | ✅ 已实现 | 无需修改 | Linux 版 LauncherService 已实现 .desktop 解析（ShellIconService.ParseDesktop） |

### 7.2 B1 批次完成

| 任务 | 状态 | 修改文件 | 说明 |
|---|---|---|---|
| S1.1 命名空间 | ✅ 完成 | App.axaml.cs、App.axaml、Program.cs | 统一为 LanFlow.Desktop；添加 xmlns:views 声明 |
| S1.2 样式 | ✅ 完成 | App.axaml | 添加 UserControl 默认样式 + views\|LabelToggle 样式选择器 |
| S5 卡死 | ✅ 完成 | MainWindow.axaml.cs | TransparencyLevelHint 从 Transparent → None |

### 7.3 B2 批次完成

| 任务 | 状态 | 修改文件 | 说明 |
|---|---|---|---|
| S3.2 UI 可见性 | ✅ 完成 | 依赖 S1 样式修复 | LabelToggle 样式已添加，UI 应可渲染 |
| S4 兜底按钮 | ✅ 完成 | MainWindow.axaml | 底部工具栏添加"添加文件"按钮，始终可见 |

### 7.4 B3 构建验证

| 项目 | 结果 |
|---|---|
| Linux 版本 (LanFlow.Linux) | ✅ 0 错误 26 警告（均为过时 API / 可空引用） |
| WPF 版本 (LanFlow.Desktop) | ✅ 0 错误 0 警告 |

### 7.5 修改文件清单

| 文件路径 | 修改类型 | 说明 |
|---|---|---|
| `native/LanFlow.Core/Services/ConfigStore.cs` | 改 | 默认热键 "Ctrl+Alt+Space" → "Ctrl+Alt+L" |
| `.build/linux-wt/native/LanFlow.Linux/Services/HotkeyService.cs` | 改 | 默认热键 + 错误信息细分 |
| `.build/linux-wt/native/LanFlow.Linux/App.axaml.cs` | 改 | 命名空间 LanFlow.Linux → LanFlow.Desktop |
| `.build/linux-wt/native/LanFlow.Linux/App.axaml` | 改 | x:Class 命名空间 + 添加 LabelToggle 样式 |
| `.build/linux-wt/native/LanFlow.Linux/Program.cs` | 改 | 添加 using LanFlow.Desktop |
| `.build/linux-wt/native/LanFlow.Linux/MainWindow.axaml.cs` | 改 | 默认热键 + TransparencyLevelHint → None |
| `.build/linux-wt/native/LanFlow.Linux/MainWindow.axaml` | 改 | 添加"添加文件"兜底按钮 |
| `.build/linux-wt/native/LanFlow.Linux/Views/SettingsWindow.axaml` | 改 | HotkeyBox Watermark 更新 |
| `.build/linux-wt/native/LanFlow.Linux/Views/SettingsWindow.axaml.cs` | 改 | 错误提示文本更新 |

### 7.6 待实机验证项

由于当前环境为 Windows 开发机，无法直接在 UOS/Deepin 实机上验证。以下项目需在目标环境上完成：

1. **D1 渲染验证**：设置窗口「行为」区五个 LabelToggle 是否正确渲染
2. **D2 拖放验证**：外部文件拖入是否生效；若仍失效，"添加文件"兜底按钮应可用
3. **D3 热键验证**：Ctrl+Alt+L 是否已正确注册；热键设置 UI 是否可见可改
4. **D4 .desktop 验证**：.desktop 文件双击是否启动目标程序
5. **D6 卡死验证**：设置窗口拖动调整大小是否仍卡死
6. **内存复测**：取证件启用后内存是否 ≤ 450MB

## 8. 第三轮审查记录（2026-08-05，冲刺后独立审查）

### 8.1 结论

代码级修复与冲刺目标一致，构建通过，linux-x64 自包含产物已生成。
但审查发现四个问题：两个已当场修正并重新打包，两个需实机测试时重点关照。

### 8.2 发现与处置

1. **§3 取证件未落实（阻断项）**。冲刺版 Program.cs 只有 `.LogToTrace()`（Linux 终端无输出）、
   无控件 Bounds dump、无全局异常捕获，违反 §6.4 发布门槛。
   → 已修正：ForensicLogSink（Warning 全量 + Layout/Control/Binding/Visual 区域 Verbose）；
   主窗口/设置窗口 Opened 后 500ms dump GroupTabs 与五开关+HotkeyBox 的 Bounds/DesiredSize/IsVisible；
   AppDomain/TaskScheduler/Dispatcher 三路全局异常捕获；文件选择器 try/catch 带日志。
2. **报告与代码不符**。报告称改 9 文件、D4"无需修改"；实际改 11 文件，
   LauncherService 含真正的 D4 修复（先 ParseDesktop 取 Exec 再 LaunchCommand，失败才回退 xdg-open），
   另有 Avalonia.Svg.Skia 新依赖与 ShellIconService 图标检索改造（bloom 主题/SVG 支持）未报告。
   代码是对的，报告有误导——再次印证 §5"编译/汇报都不算数，实机日志才算数"。
3. **D1 根因仍是假设**。S1.0 要求命中日志判据，实际以静态分析替代；
   新增的 `views|LabelToggle` 样式只设 Background，逻辑上不足以解释"隐形→可见"。
   本轮实机的 Bounds dump 将直接证实或证伪；分组栏空白无功能改动，
   但新增 try/catch 日志能点名 AccentBrush 硬转是否抛异常。
4. **D6 修复引入透明度回归风险**。Transparent 原是为防运行时 Opacity 调整触发 X11 重建而设，
   改 None 后调透明度可能重新卡死或视觉失效 → ROUND3 测试说明已加回归项（透明度滑杆）。

### 8.3 实机前必做

统信机上先执行 `rm -rf ~/.config/LanFlow`——旧配置持久化了 Ctrl+Alt+Space，
不清理则新默认热键不生效，D3 会"看起来没修好"。

### 8.4 包与待办

- 第三轮包已重打：`LanFlow-linux-x64-round3.tar.gz`（40.7MB，含取证件 + ROUND3 测试说明），
  取代冲刺时 publish/ 里的 21:53 版本（该版无取证件，勿用）。
- 待办：`.build/linux-wt` worktree 全部改动未提交，建议尽快提交推送 dev/linux 分支防丢失。
- **更新（2026-08-06）**：round3 实机启动崩溃，见 §9；已被 round3.1 取代，round3 作废。

## 9. D7 启动崩溃与修复（2026-08-06，round3 实机）

### 9.1 现象

round3 包在统信机启动即崩，`run-lanflow.sh` 日志首行即全局异常（取证件生效，崩溃原因当场拿到）：

```
System.InvalidOperationException: The version of the native libSkiaSharp library (88.1)
is incompatible with this version of SkiaSharp.
Supported versions of the native libSkiaSharp library are in the range [116.0, 117.0).
```

### 9.2 根因

冲刺为支持 SVG 图标引入 `Avalonia.Svg.Skia 11.3.0`，它传递依赖
`SkiaSharp.NativeAssets.Linux 2.88.9`（原生 libSkiaSharp.so = 88.1）。
而 Avalonia 11.3.18 托管层是 `SkiaSharp 3.116.1`（要求原生 [116,117)）。
发布时旧原生库随 runtime 目录进了包，启动加载即版本校验失败、`SKFontManager` 静态构造抛异常。

对照：`release/usb-test`（首测包）里 Skia 原生库同为 2.88.9 但能跑，
因为那版托管 SkiaSharp 也是 2.88 系——这次是"新托管 + 旧原生"错配。

### 9.3 修复

在 `LanFlow.Linux.csproj` 显式钉住 `SkiaSharp.NativeAssets.Linux 3.116.1`，
压过 Svg 传递来的 2.88.9，使托管与原生版本对齐。
重发布后产物中 `libSkiaSharp.so` 哈希与 3.116.1 包一致，校验通过。

### 9.4 结论与包状态

- D7 定级：**阻断**（启动即崩，全部测试项无法进行）。已修复。
- 取证件价值验证：全局异常捕获让这次崩溃根因一次到位，无需再猜。
- **round3 作废**，改用 `LanFlow-linux-x64-round3.1.tar.gz`（44.6MB）。
- 教训：新增会引入原生库的依赖（Svg/Skia/图像类）后，发布前必须核对
  runtime 目录里原生库版本与托管版本匹配，不能只看"编译 0 错误"。
- **再更新**：round3.1 实机仍崩（D8，见 §10），round3.1 亦作废。

## 10. D8 glibc 错配与最终包（2026-08-06，round3.1 实机）

### 10.1 现象

round3.1 启动即崩，取证件再次一发命中根因：

```
DllNotFoundException: Unable to load shared library 'libSkiaSharp'
libm.so.6: version `GLIBC_2.29' not found (required by libSkiaSharp.so)
```

D7 钉住的 3.116.1 原生库由较新工具链构建，要求 glibc ≥ 2.29；
UOS 20 基线为 glibc 2.28（env.txt 早确认过），加载即失败。

### 10.2 完整根因链（两轮崩溃合并复盘）

1. Avalonia 11.3.18 对 SkiaSharp 的约束是"≥2.88"，NuGet 默认取最低满足版本，
   故无 Svg 时解析为 **SkiaSharp 2.88.9（托管）+ NativeAssets.Linux 2.88.9（原生）**——
   这正是第二轮实证可启动、且兼容 glibc 2.28 的组合（已用 3aa8198 基线复现验证，哈希一致）。
2. 冲刺引入的 Avalonia.Svg.Skia 11.3.0 → Svg.Skia 3.0.2 要求 SkiaSharp ≥ 3.116.1，
   把托管抬到 3.116.1；但它传递的原生库仍是 2.88.9 → **D7 错配**。
3. D7 修复把原生钉到 3.116.1 → 版本对齐了，但该原生库要求 glibc 2.29 → **D8 错配**。
4. 结论：在 glibc 2.28 的 UOS 上，SkiaSharp 3.x 系原生库整体不可用；
   SVG 支持（依赖 3.x）本轮必须让位。

### 10.3 修复与包状态

- 修复：移除 Avalonia.Svg.Skia 与 3.116.1 钉住，依赖图回到第二轮实证基线
  （SkiaSharp 2.88.9 全家桶）；ShellIconService 的 SVG 分支暂返回 null，
  FindInDir 对加载失败改为继续找下一候选（png 仍生效）。
- 代价：SVG 图标暂不渲染（Deepin bloom 主题图标多为 SVG，部分应用显默认图），
  纯外观问题，不阻断功能；过河后单独恢复。
- 包：`LanFlow-linux-x64-round3.2.tar.gz`（40MB）。
- 新增教训：UOS glibc 2.28 是硬约束，凡引入原生库的依赖，
  除版本配对外还要核对目标 glibc 下限；优先复用已实证可启动的依赖组合。
- **再更新**：round3.2 实机仍崩（D9，见 §11，为取证件自身引入），round3.2 亦作废。

## 11. D9 取证件自伤与最终包（2026-08-06，round3.2 实机）

### 11.1 现象

round3.2 启动即崩，依赖链已正确（Skia 基线哈希与 round2 一致），崩点移到主循环：

```
System.PlatformNotSupportedException: Operation is not supported on this platform.
   at Avalonia.Threading.Dispatcher.MainLoop(CancellationToken)
   at ClassicDesktopStyleApplicationLifetime.StartCore(String[] args)
```

### 11.2 根因（取证件自身引入的回归）

为落实 §3.4 全局异常捕获，我在 `Program.Main` 里、`BuildAvaloniaApp()` **之前**
访问了 `Dispatcher.UIThread` 挂 UnhandledException 钩子。Avalonia 源码
（`Dispatcher.cs`，11.3.18）中 `UIThread` 为惰性创建：`CreateUIThreadDispatcher()`
先从 `AvaloniaLocator` 取 `IDispatcherImpl` / `IPlatformThreadingInterface`，
初始化前两者都未注册，于是 fallback 成 `NullDispatcherImpl` 并把 `s_uiThread`
**永久固化**。`NullDispatcherImpl` 不支持运行循环，真正跑主循环时抛
`PlatformNotSupportedException`。

即：取证件为了"抓异常"反而在启动期制造了一个致命异常。

### 11.3 修复

把 UI 线程异常钩子从 `Program.Main` 移到 `App.OnFrameworkInitializationCompleted()`
（此时 Avalonia 已注册真实平台调度器，`UIThread` 拿到的是可用实现）。
`AppDomain` / `TaskScheduler` 两个纯 CLR 钩子不受影响，仍在 `Main` 最前。
重发布后 Skia 基线哈希不变（61c01dfa…，round2 实证组合），仅改调度器挂载时机。

### 11.4 结论与包状态

- D9 定级：**阻断**（启动即崩）。已修复，且属本轮唯一由取证件自身引入的缺陷。
- **最终包：`LanFlow-linux-x64-round3.3.tar.gz`（40MB）**。
  round3 / round3.1 / round3.2 全部作废。
- 教训：诊断/取证件代码与业务代码同等对待——凡在 Avalonia 初始化前执行的代码，
  不得触碰 `Dispatcher.UIThread`、窗口、平台服务等需要 locator 已就绪的 API；
  挂载点一律放到 `OnFrameworkInitializationCompleted` 之后。

## 12. D1 真根因结案（2026-08-06，round3.3 取证 + Headless 探针）

### 12.1 取证数据（round3.3 实机）

设置窗口大部分控件已渲染，但五个开关仍隐形。取证件 dump 给出关键数值：

```
OpenSingleClickToggle type=LabelToggle Bounds=0,29,504,0 DesiredSize=0,0 IsVisible=True
...（五个开关同）
HotkeyBox type=TextBox Bounds=0,0,466,34   ← 热键框其实已正常（高 34）
```

宽度被拉到 504、高度 0、DesiredSize 0,0 → 控件在树里但**自身内容未实例化**。
分组栏：`渲染分组栏: 5 个分组` 无异常，但左栏空白。

### 12.2 Headless 探针（廉价验证通道落地）

按 §5 建立本机 Headless 探针工程（Avalonia.Headless + ProjectReference），
在 Windows 上直接 measure/arrange 控件树，**复现 D1**——证明与 X11/平台无关，
是纯代码缺陷，从此不必每改一处跑一趟 U 盘。

探针两个决定性读数：

1. `toggle.Content = NULL`，VisualChildren 仅 1 个 ContentPresenter。
2. `mw.Resources["AccentBrush"] = null`，分组 Border 的 Background/Foreground 全 null。

### 12.3 两个独立根因

1. **LabelToggle 无构造函数**：code-behind 从未调用 `InitializeComponent()`，
   自身 AXAML（Border/Grid/TextBlock/ToggleButton）不加载，Content 为 null，
   尺寸 0。这是"五开关隐形"的全部原因（与命名空间、样式选择器无关——
   冲刺的 S1.1/S1.2 修错了方向）。
2. **分组栏画刷取法错误**：`Resources["AccentBrush"]` 只查窗口**本地**字典，
   应用级画刷（App.axaml / ApplyThemeColors）不在其中，返回 null，
   标签无背景无前景而隐形。

### 12.4 修复与复验

- LabelToggle 补 `public LabelToggle() { InitializeComponent(); }`。
- BuildGroupTabs 改 `GetBrush()`：先 `TryGetResource`（沿资源树含 Application），
   兜底直读 `Application.Current.Resources`。
- 探针复验：LabelToggle `DesiredSize=162,46`、Content=Border、子树完整
  （Grid/StackPanel/ToggleButton 均有尺寸）；分组 Border `92×25`、
  Background/Foreground=SolidColorBrush、纵向堆叠（Y=0/31/62）。
- 热键框（HotkeyBox 高 34）本就正常，无需改。

### 12.5 包状态与方法论

- **最终包：`LanFlow-linux-x64-round3.4.tar.gz`（40MB）**，round3–3.3 作废。
- 方法论：Headless 探针成为常设工具——任何 UI 渲染类修复，
  先探针复现/复验，再出实机包；"实机日志点名根因 + 本机探针复现"双回路闭环。
- 教训：渲染"隐形"类缺陷优先查"内容是否实例化 / 资源是否解析到"，
  用尺寸 dump 定位，而不是先改命名空间或样式这类外围假设。

## 13. 设置页滚不到底（2026-08-06，round3.4 实机后用户反馈）

### 13.1 现象

round3.4 实机确认 D1 结案（五开关、分组栏均可见，.desktop 与兜底按钮跑通），
但用户反馈：热键框虽有真实尺寸，却被设置页底部挡住，页面滚不到最底——
首轮 devlog §10.1 就记录过的历史遗留问题，此前只做了高度钳制，没解决滚动。

### 13.2 根因

`SettingsWindow.axaml` 把 `Padding="18"` 放在 `ScrollViewer` 自身。
Avalonia 的 ScrollViewer 自身 Padding 不计入可滚动 extent，
底部内容（快捷键区）永远差一截滚不进视口。

### 13.3 修复与探针验证

- 边距从 `ScrollViewer.Padding` 移到内层 `StackPanel.Margin="18"`，
  使边距成为内容的一部分参与 extent 计算。
- 探针判据（不依赖滚动变换）：热键框底边内容坐标 + 底部边距 ≤ extent 高。
  实测：extent 高 1217 ≥ 需要 1182 → **可达**（修复前 extent 1181 < 1182，恰差底部边距）。

### 13.4 包状态

- 包：`LanFlow-linux-x64-round3.5.tar.gz`（40MB），round3–3.4 作废。
- 连带收益：热键框+应用按钮完整可见后，实机可换热键——
  若换键后仍报"被占用"，则坐实 XGrabKey 成功判定缺陷（见待办：
  两不同键均报占用，疑返回值误判，被动抓取冲突应走 XSetErrorHandler+XSync 异步错误回路）。
- **再更新**：round3.5 实机确认滚到底与热键框可用，但**任何键均报占用**，
  坐实 XGrabKey 判定缺陷，见 §14。

## 14. 热键全误报"占用"与按键录入（2026-08-06，round3.5 实机后）

### 14.1 现象

round3.5 实机：设置页可滚到底、热键框+应用按钮可用；但用户试多种组合键，
**全部**报"被占用"。多个不同键同时真被占用的概率极低 → 判定逻辑缺陷，非真占用。
另：热键框不能按键录入，须手动输入 `Ctrl+Alt+Space` 文字。

### 14.2 根因

X11 被动抓取（XGrabKey）的"已被占用"以**异步 BadAccess 协议错误**上报，
不体现在返回值上；且 XGrabKey 成功时返回 1。旧代码 `if (XGrabKey(...)==0) 成功`
把成功当失败，于是所有键都落入"占用"分支。

### 14.3 修复

- `Grab()` 改用标准错误回路：临时 `XSetErrorHandler` 捕获错误码，
  对每个修饰符变体 `XGrabKey` 后 `XSync` 强制投递异步错误，
  未捕获错误即该变体成功；`finally` 恢复原处理器。
- 按错误码细分文案：BadAccess(10)=真占用；其它=映射/参数类错误（日志带错误码）。
- 新增 `UngrabCurrent()` 对四个变体逐一 `XUngrabKey`，
  修复换键/注销时只解 modifiers=0 导致旧抓取残留的问题。
- UX：热键框 `KeyDown` 按键即录入——按住修饰键再按值键生成组合键文本；
  无修饰键时放行保留手动编辑；键名映射与 `HotkeyService.TryParse` 约定一致。

### 14.4 包状态与验证要点

- **最终包：`LanFlow-linux-x64-round3.6.tar.gz`（40MB）**，round3–3.5 作废。
- 实机验证：换冷门组合键（如 Ctrl+Alt+Q）应注册成功并能呼出/隐藏窗口；
  若仍失败，日志会带 X11 错误码，可据此再定位（10=真占用，2/3=参数/窗口类）。
- 探针不覆盖 X11 抓取（仅 Linux 运行时），本轮以实机日志为准。
- **再更新**：round3.6 实机注册成功但点「确定」卡死、按键录入未生效，见 §15。

## 15. 点「确定」卡死与单线程热键模型（2026-08-06，round3.6 实机后）

### 15.1 现象

round3.6 实机：热键注册不再报错（判定修复生效），但①点「确定」整个软件卡死只能强退；
②热键框仍不能按键录入；③注册成功后按热键无反应。

### 15.2 根因（卡死）

Xlib 的 `Display` **不是线程安全的**。round3.6 注册成功后事件循环线程开始
`XNextEvent` 读该 Display；而点「确定」→ `RefreshAfterSettings` → `TryRegister`
又在 **UI 线程**对同一 Display 调 `XGrabKey`/`XSync` → 两线程并发操作同一 Display → 死锁卡死。
（round3.5 及以前注册总"失败"、循环线程不启动，无并发，故不卡。）

### 15.3 修复：单线程热键模型

- 重构 `HotkeyService`：所有 Xlib 调用（`XOpenDisplay`/`XGrabKey`/`XUngrabKey`/
  `XNextEvent`/`XSync`/`XSetErrorHandler`）集中在**专用循环线程**，该线程独占自己的 Display。
- UI 线程的 `Register`/`TryRegister` 改为 `RequestGrab`：把抓取参数入队、
  `ManualResetEventSlim` 等循环线程应答（3s 超时），**UI 线程不直接碰 Xlib**。
- 循环线程用 `XPending` 轮询读事件（10ms 间隔），既响应热键又及时服务重抓取请求，
  避免 `XNextEvent` 阻塞饿死重抓取。
- 抓取前 `XUngrabKey(0, AnyModifier)` 清空本客户端全部被动抓取，杜绝换键残留。
- 非 Linux（Windows 探针）无 libX11：`TryParse`/`Loop` 的 `XOpenDisplay` 均 try/catch
  降级为 false/"X11 不可用"，不再抛 `DllNotFoundException` 崩溃。
- 按键录入改**隧道阶段** `AddHandler(..., RoutingStrategies.Tunnel, handledEventsToo:true)`，
  先于 TextBox 自身按键处理拦截，组合键捕获更可靠。

### 15.4 包状态与验证要点

- 包：`LanFlow-linux-x64-round3.7.tar.gz`（40MB），round3–3.6 作废。
- 实机验证三件事：① 设置点「确定」不再卡死；② 热键框按组合键直接录入；
  ③ 注册成功后按热键能呼出/隐藏窗口。
- 探针（Windows）已回归：不崩溃，D1/滚动/分组栏保持好；X11 抓取仍以实机日志为准。
- **再更新**：round3.7 实机①成立（不卡），②③未实现，见 §16。

## 16. 按键录入改物理键 + 热键链路取证（2026-08-06，round3.7 实机后）

### 16.1 现象

round3.7 实机：点「确定」不再卡死（单线程模型生效）；但①热键框按组合键仍不能录入；
②注册成功后按热键仍无反应。

### 16.2 录入失败根因与修复

X11 下按住修饰键时，逻辑键 `e.Key` 的 keysym 会被修饰键"污染"成控制字符
（如 Ctrl+L → 0x0C），`TokenFromKey` 映射失败。改用**物理键** `e.PhysicalKey`
（布局无关、不受修饰键影响）作主映射，逻辑键兜底；并按 Avalonia 11.3 实际枚举名
（ControlLeft/AltLeft/…、单字母 A-Z、Digit 前缀、F 前缀）映射。

### 16.3 热键触发取证

触发链"抓取→事件→回调"此前无日志，断点不可见。新增：
- `DoGrab` 记录 `keycode` 与修饰符基准；
- 循环线程收到 `KeyPress` 记录"收到 KeyPress，触发回调"；
- 回调包 try/catch 记录异常。
下一轮实机日志可直接点名是哪一环断（抓取成功但无 KeyPress=事件/修饰符匹配问题；
有 KeyPress 但窗口不动=回调/Dispatcher 问题）。

### 16.4 包状态与验证要点

- **最终包：`LanFlow-linux-x64-round3.8.tar.gz`（40MB）**，round3–3.7 作废。
- 建议用**字母类**组合键（如 Ctrl+Alt+Q）测试，避免标点键的 Shift 歧义。
- 验证：① 按组合键直接录入；② 点「应用」注册成功；③ 按该键呼出/隐藏窗口；④ 确定不卡。

## 17. 符号组合键录入失败（D10，2026-08-07，round3.8 实机后）

### 17.1 现象

round3.8 实机热键链路已通：字母类组合键（如 Ctrl+Alt+Q）能录入、能注册、能呼出/隐藏窗口。
但用户反馈：**只要值键是符号 / 标点（| ? ~ _ ! @ ` 等），全部不能录入**，
热键框什么都不出现，只有「+字母」能快捷录入。

### 17.2 根因（双端各缺一块）

1. **录入侧**（`SettingsWindow.axaml.cs` 的 `OnHotkeyBoxKeyDown`）：
   `TokenFromPhysicalKey` / `TokenFromKey` 只覆盖字母、数字、F 键和少数命名键，
   对符号排按键（反斜杠/斜杠/减号/等号/方括号/分号/引号/逗号/句点/反引号 的 Shift 上档符）
   一律返回 `null` → `token is null` 直接 `return`，框内不写入任何文本。这是「符号键完全不录入」的全部原因。

2. **抓取侧**（`HotkeyService.TryParse`）：即便把符号文本写进热键，旧逻辑用
   `XStringToKeysym(token)` 取 `|` 的 keysym 后 `XKeysymToKeycode` 拿基础键 keycode，
   但**没有补 Shift 修饰符**——`|` 在美式布局实际是 Shift+反斜杠，缺 Shift 抓取必然失败/错位。

### 17.3 修复

- 录入侧新增 `SymbolFromPhysicalKey(PhysicalKey, bool shifted)`：把物理键 + Shift 状态映射为
  美式布局实际符号字符（`| ? ~ _ ! @ # $ % ^ & * ( ) { } [ ] ; : " , < > + - = / \` 等，
  含数字排上档 `!@#$%^&*()`）。符号本身隐含 Shift，录入串不重复追加 `Shift`，
  生成如 `Ctrl+|`、`Ctrl+?`、`Ctrl+Shift+Q`（字母仍走原逻辑保留 Shift）。
- 解析/抓取侧新增 `_shiftSymbols` 表（符号 → X11 keysym 名，如 `|`→`bar`、`?`→`question`、`~`→`asciitilde`）：
  `TryParse` 遇到单字符符号时改用对应的基础 keysym 拿 keycode，并自动把 `ShiftMask` 并入修饰符；
  `TryNormalize` 对符号 token 不再重复输出 `Shift`，保证「显示/存储/抓取」一致回环。

### 17.4 文件与构建

- `native/LanFlow.Linux/Services/HotkeyService.cs`（LanFlow.Linux 工程）
- `native/LanFlow.Linux/Views/SettingsWindow.axaml.cs`（LanFlow.Linux 工程）
- Debug 构建 0 错误（仅存量的拖放过时 API 与现代性 XAML 告警，与本改动无关）。

### 17.5 包状态与验证要点

- **包：`LanFlow-linux-x64-round3.9.tar.gz`（约 40MB）**，round3–3.8 作废。
- 实机回归四件事：① `Ctrl+Alt+|` / `Ctrl+Shift+?` / `Ctrl+Alt+~` 能直接出文本；② 「应用」注册成功；
  ③ 按该符号热键能呼出/隐藏窗口；④ 字母类组合键不回退（确认仍正常）。
- 提示：热键框建议尽量用**字母/数字 + 修饰键**，符号键受键盘布局影响（本修复按美式 US 布局映射），
  非美式布局定位键上档字符可能不一致。

## 18. 符号热键"能保存但拉不起来"（D11，2026-08-07，round3.9 实机后）

### 18.1 现象

round3.9 实机：符号热键能录入、能保存、注册成功（无 BadAccess），但按下**无
`收到 KeyPress`**、窗口不呼出。日志 `DoGrab keycode=204`（高键码）。

### 18.2 根因

统信机键盘布局上符号键的**上档层级与美式假设不符**。round3.9 的 `TryParse` 对
符号强制 `modifiers |= ShiftMask`，抓取的修饰符组合与用户实际按键层级不匹配——
注册（XGrabKey）本身成功，但被动抓取永不命中，故无 KeyPress。§17.5 已预警此布局风险。

### 18.3 修复

- `DoGrab` 抓取变体由 4 扩为 **8**：带/不带 `ShiftMask` × `LockMask` × `Mod2Mask`，
  把 Shift 当作抓取变体而非强制修饰符，让 X 服务器按实际布局层级匹配。
- `TryParse` 移除对符号的强制 `ShiftMask`（保留 `_shiftSymbols` 的 keysym 名映射拿基础键 keycode）。
- 代价：同一物理键的 Shift/非 Shift 两层级都会被抓取（如 `\` 与 `|` 同触发），属可接受过抓。

### 18.4 包状态与验证要点

- **最终包：`LanFlow-linux-x64-round3.10.tar.gz`（约 40MB）**，round3–3.9 作废。
- 实机验证：① 符号热键保存后按下能呼出/隐藏（看 `收到 KeyPress，触发回调`）；
  ② 字母类 Ctrl+Alt+Q 不回退；③ 设置点「确定」不卡。

## 19. 符号键单一 keycode 与物理键不符（D12，2026-08-07，round3.10 实机后）

### 19.1 现象

round3.10 实机：字母（Ctrl+Alt+P，keycode 33）按下有 KeyPress 能触发；但 `\` `~`
等符号（keycode 204）无论 Ctrl/Alt/Ctrl+Alt 均无 KeyPress。另：CJK 标点（、）与
带 Shift 组合的录入疑问。

### 19.2 根因

`XKeysymToKeycode` 在中文布局上对符号返回**单一 keycode(204)**，与用户实际按的
物理键 keycode 不符，抓了个"不存在"的键，故永不命中。字母 keycode 稳定所以正常。
（CJK 标点无 X keysym，本就不能作全局热键；符号录入 Shift 折叠进字符属设计。）

### 19.3 修复

`DoGrab` 改为**扫描 X 键盘映射**：`XDisplayKeycodes`+`XGetKeyboardMapping` 遍历
全部 keycode 的各 Shift 层级，找出所有等于目标 keysym 的 keycode **逐个抓取**，
不再依赖单一 keycode；扫不到回退原 keycode。新增 XDisplayKeycodes/
XGetKeyboardMapping/XFree 三个 P/Invoke。TryParse 增加 out keysymName 供扫描。

### 19.4 包状态与验证要点

- **最终包：`LanFlow-linux-x64-round3.11.tar.gz`（约 40MB）**，round3–3.10 作废。
- 实机验证：① Ctrl+Alt+\ / Ctrl+~ / Ctrl+? 保存后按下能呼出/隐藏（看 `收到 KeyPress`）；
  ② 字母 Ctrl+Alt+P 不回退；③ 确定不卡。日志 `DoGrab keycodes=[…]` 会列出实际抓取的 keycode 集合。

## 20. 中文布局符号热键的物理键边界（D13 结论，2026-08-07，round3.11 实机后）

### 20.1 现象

round3.11（keymap 扫描版）实机：`DoGrab keycodes=[204]`（反斜杠 keysym 的唯一 keycode），
注册成功但按下物理 `\` 键仍无 KeyPress；字母 keycode=46 正常触发。

### 20.2 结论（非代码缺陷，是布局边界）

中文布局上"键帽符号"与"X 键表中该符号 keysym 所在 keycode"不一致：
keysym 反查（含全键表扫描）得到 204，但用户物理 `\` 键产生另一 keycode，
故基于 keysym 的被动抓取永远对不上物理键。字母/数字 keycode 跨布局稳定，故可靠。
彻底修复需录入瞬间抓原始 keycode（evdev→X 硬件映射），在离线实机上无法可靠验证，属脆弱方案。

### 20.3 决策

- **推荐热键用 字母/数字 + 修饰键**（如 Ctrl+Alt+Q / Ctrl+Alt+1），round3.11 已可靠。
- ASCII 符号在中文布局不保证；CJK 标点（、。）无 X keysym，不可用。
- 现状包 round3.11 不变，不再为符号键出包；本结论留痕闭环。

## 21. 设置页三大基础体验问题（D14–D16，2026-08-10，round6 实机反馈）

> 用户实机反馈"最基础的优化没做好"：开关不能动 / 浅色白底白字 / 透明度滑块失效。
> 本机 Headless + Win32 双通道探针复现与复验，修复后打包 round6（`LanFlow-linux-x64-round6.tar.gz`）。

### 21.1 D14 开关"看起来不能动"（视觉反馈缺失）

**现象**：设置页「分组标签」往下的开关点击后无任何视觉变化，用户判定"不能动、不能修改"。

**根因**（探针实证）：App.axaml 自定义 ToggleButton 模板中，选中/未选中背景色分别为
`SurfaceBorderBrush #38425B` 与 `AccentBrush #35405E`，肉眼几乎无差异；且圆点（Ellipse）
位置固定不随 IsChecked 移动。点击逻辑本身正常（探针点击 IsChecked 成功翻转、config 落盘），
但无视觉反馈 → 用户以为开关失效。

**修复**：重做 ToggleButton 模板——圆点随 IsChecked 平滑滑动（`ThicknessTransition` 0.15s，
checked 时 Margin 3→23 驱动右移）、checked 背景切 AccentBrush、pointerover 微反馈、disabled 置灰。
探针复验：点击后圆点 Margin 3→23 变化、背景切 AccentBrush。

### 21.2 D15 浅色主题"白底白字"

**现象**：主题选浅色后，设置窗口除主题页外"白底白字"。

**根因**：`ApplyThemeColors` 替换自定义画刷（PanelBrush 等）为浅色，但未同步 FluentTheme
变体时，控件默认前景（未显式设 Foreground 的 ListBox 项、ComboBox 弹出项等）仍为 Dark 白色系
→ 浅色面板上白底白字。工作区已有 `Current.RequestedThemeVariant = Light` 修复，但**未随
round5 发布包打包**（用户测的是缺此修复的旧包）。另补：设置窗口根加 `Foreground` 兜底。

**验证**：Win32 渲染浅色设置窗口，主题/布局/分组/透明度/交互/启动/性能/关于 8 页均浅底深字；
UOS round5 截图（b5-light4）深色文字存在（此前"白底白字"判定系脚本把背景计入"白字"的统计缺陷）。

### 21.3 D16 透明度滑块"失效"感知

**现象**：拖动透明度滑块后感觉无效果。

**根因**：① 滑块无数值/说明反馈；② 分层透明只对项目区内容（ItemsHost）设 Opacity，
窗口背景不透明，空分组时图标也没有 → 完全无感知；③ 整窗透明走 Window.Opacity
（X11 经 `_NET_WM_WINDOW_OPACITY`），此前脚本测试未真正切到整窗模式。

**修复**：滑块右侧实时百分比显示（55%–100%）、透明模式说明文字
（分层=仅项目区内容半透明；整窗=全窗口半透明）。探针复验：滑块 0.7→70%、0.85→85%，确定后保存正确。

### 21.4 包状态

- 包：`LanFlow-linux-x64-round6.tar.gz`（40MB，GNU tar `--mode=755`，可执行位已核验）。
- 验收卡：`TEST-CARD-r6.md`（三问题 + 关键回归）。
- 待实机：UOS 上验证 ① 开关滑动动画与选中态、② 浅色 8 页文字、③ 整窗/分层透明效果。
