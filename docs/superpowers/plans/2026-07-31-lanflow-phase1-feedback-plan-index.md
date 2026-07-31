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

- [ ] **Step 1: 确认工作区只包含计划内产品改动和用户反馈文档**

Run:

```powershell
git status --short
git diff --check
```

Expected：没有未解决冲突、尾随空格或计划外生成文件；`docs/fankui/` 是否纳入提交由用户文件状态决定，不擅自暂存。

- [ ] **Step 2: 运行 Core 全量测试**

Run:

```powershell
dotnet test native\LanFlow.Core.Tests\LanFlow.Core.Tests.csproj -c Release
```

Expected：0 failed。

- [ ] **Step 3: 运行 Desktop 全量测试**

Run:

```powershell
dotnet test native\LanFlow.Desktop.Tests\LanFlow.Desktop.Tests.csproj -c Release
```

Expected：0 failed。

- [ ] **Step 4: 构建 Debug 桌面程序**

Run:

```powershell
dotnet build native\LanFlow.Desktop\LanFlow.Desktop.csproj -c Debug
```

Expected：Build succeeded，0 errors。

- [ ] **Step 5: 做一次本机启动烟雾检查**

Run:

```powershell
dotnet run --project native\LanFlow.Desktop\LanFlow.Desktop.csproj -c Debug
```

Expected：主窗口可启动；打开设置后两窗口均可操作；关闭应用后检查日志中无未处理异常。完成观察后正常退出程序。

- [ ] **Step 6: 发布 Windows x64 framework-dependent Debug 目录**

Run:

```powershell
$commit = (git rev-parse --short HEAD).Trim()
$out = "artifacts\debug\$commit\LanFlow-1.3.8-debug-$commit-win-x64"
dotnet publish native\LanFlow.Desktop\LanFlow.Desktop.csproj -c Debug -r win-x64 --self-contained false -o $out
```

Expected：发布目录包含 `LanFlow.exe`、依赖 DLL 和运行时配置文件。

- [ ] **Step 7: 写入 Debug 构建说明并压缩**

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

- [ ] **Step 8: 输出手测清单，不推送远端**

手测清单必须包括：

1. 网格/卡片反复切换图标；
2. 顶部/左侧分组布局；
3. 列间距 0、8、32、64；
4. 透明度 85% 和 100%；
5. 设置窗口并排、重复打开和关闭决策；
6. 悬停延迟 0、100、200、500ms；
7. 配置目录迁移、重启加载和恢复默认；
8. 日志文件位置和最后一次启动结果。
