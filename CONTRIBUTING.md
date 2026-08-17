# LanFlow 开发与发布规范

本规范用于固定 LanFlow 的开发线和发布线，避免开发提交、过程资料或构建产物直接进入正式分支。

## 分支职责

| 位置 | 用途 |
| --- | --- |
| 本地 `windows` | Windows 功能开发分支；必须跟踪 `dev/windows`。 |
| 远端 `dev/windows` | Windows 开发集成线，保存已提交的开发成果。 |
| 远端 `origin/main` | 正式发布线，只保存可发布的干净代码和必要说明。 |

`windows` 是本地分支名，`dev/windows` 是它的远端跟踪分支；两者共同构成 Windows 开发线。

## 固定流程

1. 所有 Windows 功能、修复和测试先在本地 `windows` 开发并提交。
2. 经基础验证后，先推送到开发仓库：`git push dev windows:windows`。
3. 正式发布时，从干净的 `main` 创建临时发布工作树，将已确认的产品代码同步为发布快照。
4. 在发布工作树完成测试、Release 构建和差异检查后，提交到 `main`，创建版本标签并推送到 `origin`。
5. 发布完成后删除临时发布工作树；日常开发继续留在 `windows` / `dev/windows`。

禁止将日常开发分支直接推送到 `origin/main`。`main` 只能接收经过发布整理和验证的干净快照。

## main 的保留范围

`main` 可以保留：

- 产品源码、必要测试源码和构建工具；
- `README.md`、`CHANGELOG.md`、许可证和本规范等长期项目说明；
- 与正式版本直接相关的配置。

`main` 不得包含：

- `artifacts/`、构建日志、临时截图和测试数据；
- `docs/fankui/`、`docs/superpowers/` 等反馈、方案和过程记录；
- PRD、临时设计稿、调试记录或其他开发过程文档；
- 本地运行配置、凭据或用户数据。

## 自动代理红线

自动开发代理（Agent）开工前必须阅读根目录 `AGENTS.md`（分支红线摘要，比本文件更简短、更强制）。Agent 框架应自动注入该文件；若未注入，任务开始时必须显式要求 Agent 先读 `AGENTS.md` 再动手。

## 本地防护钩子（推荐安装）

```powershell
git config core.hooksPath .githooks
```

`pre-push` 钩子会拦截「把 `feat:`/`fix:`/`refactor:`/`perf:`/`test:`/`style:` 等开发性提交直接推送到 `main`」的操作（按 main 的 first-parent 链检查，不影响 merge 与版本号 bump）。

CI 守卫（`.github/workflows/main-branch-guard.yml`，调用 `scripts/guard-main-release.sh`）会在 PR / 推送 `main` 时自动检查：冲突标记、业务代码越界改动、csproj 版本号一致性。

## 发布前最小检查

```powershell
# 在临时发布工作树中执行
dotnet test native\LanFlow.Core.Tests\LanFlow.Core.Tests.csproj -c Release
dotnet test native\LanFlow.Desktop.Tests\LanFlow.Desktop.Tests.csproj -c Release
dotnet build native\LanFlow.Desktop\LanFlow.Desktop.csproj -c Release --no-restore
git diff --check
```

通过后再执行提交、创建标签和推送。