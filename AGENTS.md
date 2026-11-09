# AGENTS.md — 自动代理红线（每次开工必须遵守）

> 自动开发代理（Agent）在开始任何任务前必须阅读本文件并遵守。
> 完整规范见 `CONTRIBUTING.md`；本文件是机器可读的强制红线摘要，二者冲突时以本文件红线为准并报告差异。

## 分支职责（不可违反）

| 分支 | 用途 | 允许的提交类型 |
|---|---|---|
| `windows`（推送 `origin/windows`） | Windows（WPF）功能开发 | `feat:` `fix:` `refactor:` `perf:` `test:` `docs:` 等开发性提交 |
| `linux`（推送 `origin/linux`） | Linux（Avalonia）开发 | 同上 |
| `main`（推送 `origin/main`） | **仅打包与发布** | 仅 `merge` 提交、版本号 bump（`build:` / `chore(release):`）、annotated tag、发布基础设施 |

> ⚠️ `dev` 远端（LanFlow-dev.git）已归档为只读，**不要向其推送**；开发与发布统一走 `origin`（LanFlow.git）。

## 禁止（RED LINE）

- ❌ 在 `main` 上直接提交 `feat:` / `fix:` / `refactor:` / `perf:` / `test:` / `style:`。
- ❌ 在 `main` 上改动业务代码（`native/**`、`linux/native/**`），`*.csproj` 版本号除外。
- ❌ 把 Linux 代码合入 `windows`；把 Windows 代码合入 `linux`。
- ❌ 在 `main` 上提交过程文档（`docs/archive/**`、PRD、反馈、方案、调试记录）、构建产物（`artifacts/`、`release/`、`bin/`、`obj/`、`.build/`）。
- ❌ 提交残留冲突标记（`<<<<<<<` / `=======` / `>>>>>>>`）。
- ❌ 未构建验证就推送 `main`。

## main 上唯一允许的动作

1. 发布合并：`git merge windows --no-ff` 或 `git merge linux --no-ff`。
2. 版本号 bump：仅改 `*.csproj` 的 `Version` / `AssemblyVersion` / `FileVersion`（三者必须一致），提交类型 `build:` 或 `chore(release):`。
3. 打 annotated tag：`git tag -a vX.Y.Z -m "..."`。
4. 发布基础设施维护（AGENTS.md、CI、git 钩子、打包脚本、README/CHANGELOG 等长期说明）。

## 每次开工 Pre-flight（强制）

1. `git branch --show-current` —— 确认当前分支。
2. `git status --porcelain` —— 确认工作区干净（无未提交改动）。
3. 判断本次任务归属：**Windows 开发 / Linux 开发 / 打包发布**，先切到对应分支再动手。
4. 涉及 `main` 时，确认改动仅限“main 上唯一允许的动作”列表。

## 提交前自检（强制）

- 冲突标记：`git grep -nE '^(<<<<<<<|=======|>>>>>>>)' -- .` 必须无输出。
- 版本一致性：`Version` ↔ `AssemblyVersion` ↔ `FileVersion` 三者一致（尾随 `.0` 视为等价）；**具体版本号以 csproj 当前值为准，不在本文件写死**（由 `scripts/guard-main-release.sh` 动态校验）。
- 本地构建（Windows）：`dotnet build native/FlickD.Desktop/FlickD.Desktop.csproj -c Release --no-restore`；Linux 同理。
- 本地测试：`dotnet test native/FlickD.Core.Tests/FlickD.Core.Tests.csproj -c Release` 与 `native/FlickD.Desktop.Tests/FlickD.Desktop.Tests.csproj` 同理，须全绿。
- 推 `main` 前：本地钩子已生效（`git config core.hooksPath .githooks`），且 CI（`.github/workflows/main-branch-guard.yml`）的发布守卫与 Windows build/test 两个 job 全部通过。
