# LanFlow 一期反馈分阶段原地重构 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 按路线 B 分四个独立阶段完成一期八项反馈，并在全部阶段通过后生成 Windows x64 Debug 包供用户手动验证。

**Architecture:** 保留现有 .NET 8/WPF、Core/Desktop 分层和设置预览事务；按“布局与图标、设置页视觉、非模态窗口、配置位置与悬停延迟”拆分为四份可独立审查的计划。每个阶段先增加失败测试或合同测试，再最小实现、回归验证和本地提交。

**Tech Stack:** C# 12、.NET 8、WPF、xUnit、System.Text.Json、Windows Explorer/OpenFolderDialog、PowerShell Debug 打包。

## Global Constraints

- 设计规格：`docs/superpowers/specs/2026-07-31-lanflow-phase1-feedback-design.md`。
- 不更换 WPF，不引入第三方 UI 框架或新 NuGet 依赖。
- 不恢复列表布局，不新增右侧分组栏。
- `itemSpacing` JSON 字段保持兼容；界面名称改为“列间距”。
- `layoutMode = "list"` 加载后必须得到 `grid`。
- 悬停延迟范围固定为 0–500ms，默认 100ms，步进 10ms。
- 配置位置迁移只迁移 `config.json`，重启后生效，源文件保留。
- 新增中文 C# 字符串优先使用 `\uXXXX`；XAML 和 Markdown 保持 UTF-8。
- 每个行为修复先写失败测试，再写最小实现；每阶段独立提交。
- 不推送远端；最终只做基础测试、Debug 构建和 Debug ZIP 打包。

---

## 本轮独立复核记录（2026-07-31）

本轮不采信 agent 的“已完成”结论，依据当前工作区代码、测试结果和构建结果逐项复核。结论如下：

| 阶段 | 复核状态 | 结论 |
|---|---|---|
| Phase 1：布局、列间距与图标刷新 | 自动化验证通过 | 已移除公开 `ListLayout` 与运行时列表分支；旧 `list` 配置归一化为 `grid`；布局切换会取消旧图标请求并重载可见图标。 |
| Phase 2：设置页视觉 | 自动化验证通过，待用户视觉验收 | 设置视觉/主题合同测试及构建通过；真实 85% 透明度下的视觉观感保留给用户手测。 |
| Phase 3：非模态设置窗口 | 自动化验证通过 | 保存会关闭非模态设置窗口；主窗口退出时会丢弃预览并关闭仍打开的设置窗口。 |
| Phase 4：配置位置与悬停延迟 | 自动化验证通过 | 配置位置已移至“性能与缓存”；locator 更新失败时会恢复目标目录原 `config.json`。 |
| 最终打包与手测 | Debug 包已生成 | 已完成基础启动烟雾、Windows x64 Debug 发布目录和 ZIP；交互/视觉手测待用户执行。 |

### 已确认完成的最终验证项

- Core Release 全量测试：68 passed / 0 failed。
- Desktop Release 全量测试：166 passed / 0 failed。
- Desktop Debug 构建：0 warnings / 0 errors。
- git diff --check：通过。

### 本轮修复完成后的状态（2026-07-31）

以下修复已由新增回归测试或全量测试覆盖：

1. 布局切换会取消旧图标请求，并在调度后重新加载当前可见项的图标；运行时代码不再保留列表布局分支。
2. 设置窗口保存后关闭；应用退出时显式关闭仍处于打开状态的设置窗口并丢弃未保存预览。
3. “配置位置”操作区已从“关于”移动到“性能与缓存”。
4. `config-location.json` 更新失败时，迁移服务会恢复目标目录原有 `config.json`；若目标原本无配置，则删除刚写入的新配置。
5. 已完成基础启动烟雾检查、Debug 发布与 ZIP 压缩。由于用户不在电脑前，未进行需人工观察的设置并排、透明度和交互视觉验收；这些保留在最终手测清单中。

## 计划拆分与执行顺序

### Phase 1：布局模式、列间距与图标刷新

文档：`docs/superpowers/plans/2026-07-31-lanflow-phase1-01-layout-icons-spacing.md`

交付：

- 设置页只保留网格和卡片；
- 旧 `list` 配置迁移为 `grid`；
- 网格/卡片切换后取消旧图标请求并重新加载可见图标；
- 列间距文案和实际水平间距一致。

依赖：无。

完成门：Core 与 Desktop 相关测试通过，Desktop Release 构建通过，提交信息 `fix: stabilize grid and card layout switching`。

### Phase 2：设置页视觉和基础可用性

文档：`docs/superpowers/plans/2026-07-31-lanflow-phase1-02-settings-visual-polish.md`

交付：

- 设置页与主页共享语义资源；
- 左侧导航选中/焦点轮廓完整；
- 透明度 0、85、100 均完整显示；
- 设置页在约 85% 透明度下保持层级清晰。

依赖：Phase 1 的设置 XAML 已移除列表选项。

完成门：设置页合同测试和主题合同测试通过，Desktop Release 构建通过，提交信息 `style: align settings window with main surface`。

### Phase 3：非模态单实例设置窗口

文档：`docs/superpowers/plans/2026-07-31-lanflow-phase1-03-modeless-settings-window.md`

交付：

- 设置窗口使用 `Show()` 而不是 `ShowDialog()`；
- 主页和设置页可同时操作；
- 设置窗口单实例，优先右侧、其次左侧、最后限制到工作区；
- 应用、放弃、继续编辑语义不变。

依赖：Phase 2 的窗口尺寸和导航布局已稳定。

完成门：定位算法、关闭流程和架构合同测试通过，Desktop Release 构建通过，提交信息 `feat: open settings as a modeless companion window`。

### Phase 4：配置位置迁移与悬停延迟

文档：`docs/superpowers/plans/2026-07-31-lanflow-phase1-04-config-location-hover-delay.md`

交付：

- 显示、打开、更换和恢复配置位置；
- locator 解析、原子迁移、目标备份和失败回滚；
- 悬停切换延迟 0–500ms 可实时预览；
- 迁移成功提示重启。

依赖：Phase 3 的非模态设置窗口生命周期。

完成门：Core 迁移测试、分组协调器测试和设置页合同测试通过，提交信息 `feat: add config migration and hover delay settings`。

## 最终验证与 Debug 打包

- [x] **Step 1: 确认工作区只包含计划内产品改动和用户反馈文档**

Run:

```powershell
git status --short
git diff --check
```

Expected：没有未解决冲突、尾随空格或计划外生成文件；`docs/fankui/` 是否纳入提交由用户文件状态决定，不擅自暂存。

- [x] **Step 2: 运行 Core 全量测试**

Run:

```powershell
dotnet test native\LanFlow.Core.Tests\LanFlow.Core.Tests.csproj -c Release
```

Expected：0 failed。

- [x] **Step 3: 运行 Desktop 全量测试**

Run:

```powershell
dotnet test native\LanFlow.Desktop.Tests\LanFlow.Desktop.Tests.csproj -c Release
```

Expected：0 failed。

- [x] **Step 4: 构建 Debug 桌面程序**

Run:

```powershell
dotnet build native\LanFlow.Desktop\LanFlow.Desktop.csproj -c Debug
```

Expected：Build succeeded，0 errors。

- [x] **Step 5: 做一次本机启动烟雾检查**

Run:

```powershell
dotnet run --project native\LanFlow.Desktop\LanFlow.Desktop.csproj -c Debug
```

Expected：主窗口可启动；打开设置后两窗口均可操作；关闭应用后检查日志中无未处理异常。完成观察后正常退出程序。

- [x] **Step 6: 发布 Windows x64 framework-dependent Debug 目录**

Run:

```powershell
$commit = (git rev-parse --short HEAD).Trim()
$out = "artifacts\debug\$commit\LanFlow-1.3.8-debug-$commit-win-x64"
dotnet publish native\LanFlow.Desktop\LanFlow.Desktop.csproj -c Debug -r win-x64 --self-contained false -o $out
```

Expected：发布目录包含 `LanFlow.exe`、依赖 DLL 和运行时配置文件。

- [x] **Step 7: 写入 Debug 构建说明并压缩**

Run:

```powershell
$commit = (git rev-parse --short HEAD).Trim()
$root = "artifacts\debug\$commit"
$out = "$root\LanFlow-1.3.8-debug-$commit-win-x64"
@"
LanFlow 1.3.8 Debug Build
Commit: $commit
Platform: win-x64
Configuration: Debug
Validation: Core tests, Desktop tests, Debug build, startup smoke test
"@ | Set-Content -LiteralPath "$out\DEBUG-BUILD.txt" -Encoding utf8
Compress-Archive -LiteralPath $out -DestinationPath "$root\LanFlow-1.3.8-debug-$commit-win-x64.zip" -Force
```

Expected：生成 `artifacts\debug\<commit>\LanFlow-1.3.8-debug-<commit>-win-x64.zip`。

- [x] **Step 8: 输出手测清单，不推送远端**

手测清单必须包括：

1. 网格/卡片反复切换图标；
2. 顶部/左侧分组布局；
3. 列间距 0、8、32、64；
4. 透明度 85% 和 100%；
5. 设置窗口并排、重复打开和关闭决策；
6. 悬停延迟 0、100、200、500ms；
7. 配置目录迁移、重启加载和恢复默认；
8. 日志文件位置和最后一次启动结果。
### 最终执行记录（2026-07-31）

- Core Release：69 passed / 0 failed。
- Desktop Release：171 passed / 0 failed。
- Desktop Debug build：0 warnings / 0 errors。
- 基础启动烟雾：Debug 进程启动后存活 6 秒，未写入新的 `%LOCALAPPDATA%\LanFlow\crash.log`。
- 发布目录：`artifacts/debug/5365680/LanFlow-1.3.8-debug-5365680-win-x64/`。
- ZIP：`artifacts/debug/5365680/LanFlow-1.3.8-debug-5365680-win-x64.zip`（已核对包含可执行文件、依赖、运行时配置和 `DEBUG-BUILD.txt`）。
- 未推送远端；工作区保留未提交修改，供用户继续手测与审阅。
---

## 设置页视觉复核补充（2026-07-31）

根据手动验收反馈，对设置页进行了第二轮可视化复核和最小修复。以下三项已完成并已由自动化与离屏截图共同校对：

- [x] **设置页与主界面主题同步**：`MainWindow` 通过 `Application.Current.Resources` 应用动态主题资源，不再只更新主窗口私有资源。因此设置窗口可以同步使用当前的深色、自定义配色和半透明相关语义 token。
- [x] **透明度数值编辑器完整显示**：透明度行改为固定列布局，数值输入区扩展到 72px，内边距收窄为 8px；在离屏截图中已确认 `100 %` 完整显示。
- [x] **左侧分类选中高亮完整显示**：侧栏由 184px 调整为 200px；分类列表改为侧栏内 `Stretch` 布局并移除负外边距，避免选中状态越界裁切。

### 本轮新增回归合同测试

`native/LanFlow.Desktop.Tests/SettingsWindowContractTests.cs` 新增并验证：

1. 侧栏分类列表不再使用固定宽度与负外边距造成溢出；
2. 透明度输入框与百分号预留足够布局空间；
3. 动态主题资源的更新目标为应用级资源，以覆盖主窗口之外的设置窗口。

### 本轮验证记录

- SettingsWindow 定向合同测试：16 passed / 0 failed（修复前新增三项测试均按预期失败，修复后通过）。
- Core Release 全量测试：69 passed / 0 failed。
- Desktop Release 全量测试：174 passed / 0 failed。
- Desktop Debug 构建：0 warnings / 0 errors。
- 可视化复核：已生成自定义深色主题设置页离屏截图；确认顶部/侧栏/内容卡片使用同一主题 token、左侧选中高亮完整、`100 %` 未截断。
- Debug 启动烟雾：新启动的 Debug 进程持续存活 6 秒并拥有窗口句柄，随后已只停止本次测试进程。
- `git diff --check`：通过。

### 本轮本地 Debug 交付物

由于先前 Debug 发布目录中的 `LanFlow.Core.dll` 正被一个既存、非本轮启动的 LanFlow 进程占用，本轮没有强制终止该进程或覆盖原包；已生成独立的修复包：

- 发布目录：`artifacts/debug/5365680/LanFlow-1.3.8-debug-5365680-win-x64-settings-ui-fix/`
- ZIP：`artifacts/debug/5365680/LanFlow-1.3.8-debug-5365680-win-x64-settings-ui-fix.zip`

ZIP 已核对包含 `LanFlow.exe`、`LanFlow.dll`、`LanFlow.Core.dll`、`LanFlow.runtimeconfig.json` 与 `DEBUG-BUILD.txt`。未提交、未推送远端。


---

## 启动首屏空白修复复核（2026-07-31）

- [x] **问题复现与根因定位**：首组配置已包含 8 个项目，且 `ItemList.Items.Count` 在启动时已为 8；但 `VirtualizingWrapPanel` 加入可视树的首轮布局中，`ItemContainerGenerator` 仍为 `NotStarted`，已实现容器数为 0。首次没有发生可供面板消费的后续集合/布局刷新，因此页面会保持空白；切换分组会触发 `VisibleItems` 的 `Reset`，从而生成项目容器。
- [x] **最小修复**：在 `ItemList.Loaded` 后将初始化操作排入 `DispatcherPriority.ContextIdle`；在该阶段重新取得虚拟化面板、同步既有 `ItemsSource` 绑定、调用 `RefreshVisibleItems()` 发送集合重置，并使 `ItemList` 与面板重新测量/排列。这样不伪造分组切换，也不改动用户当前选择状态。
- [x] **回归约束**：`MainWindowArchitectureContractTests.MainWindow_QueuesAnInitialVirtualizedLayoutRefreshAfterItemListLoads` 覆盖首次虚拟化刷新必须在 `ItemList.Loaded` 后执行，以及绑定同步、集合刷新和布局无效化的必要顺序。

### 本轮验证记录

- Core Release tests：**69 passed / 0 failed**。
- Desktop Release tests：**175 passed / 0 failed**。
- Debug build：**0 warnings / 0 errors**。
- UI Automation（使用实际本地配置）：无临时诊断的 Debug 二进制连续冷启动 **5 次**，每次 `ItemList` 均生成 **8** 个 `ListItem` 子项。
- UI Automation（从新的 Debug 发布目录启动）：连续冷启动 **3 次**，每次 `ItemList` 均生成 **8** 个 `ListItem` 子项。
- 可视化证据：`artifacts/ui-debug/startup-first-screen-items-visible.png`（首个分组“Agent集合”在窗口初显时已显示项目卡片）。
- 新本地 Debug 包（未提交、未推送）：`artifacts/debug/5365680/LanFlow-1.3.8-debug-5365680-startup-first-screen-fix-verified.zip`。
