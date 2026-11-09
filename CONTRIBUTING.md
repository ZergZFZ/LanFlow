# FlickD 开发与发布规范

> 本规范与根目录 `AGENTS.md` 保持一致；**二者冲突时以 `AGENTS.md` 红线为准并报告差异**。
> 工程代号曾为 LanFlow，2.0.0 起正式更名为 FlickD；GitHub 仓库地址 `ZergZFZ/LanFlow` 保持不变。

## 分支职责

| 分支 | 用途 | 推送目标 | 允许的提交类型 |
| --- | --- | --- | --- |
| `windows` | Windows（WPF / `native/FlickD.*`）功能开发 | `origin/windows` | `feat:` `fix:` `refactor:` `perf:` `test:` `docs:` 等开发性提交 |
| `linux` | Linux（Avalonia / `linux/native/LanFlow.*`）开发 | `origin/linux` | 同上 |
| `main` | **仅打包与发布** | `origin/main` | 仅 merge、版本号 bump（`build:` / `chore(release):`）、annotated tag、发布基础设施 |

> ⚠️ `dev` 远端（`LanFlow-dev.git`）已归档为**只读**，不要向其推送。开发与发布统一走 `origin`（`LanFlow.git`）。
> 禁止把 Linux 代码合入 `windows`，或把 Windows 代码合入 `linux`。

## 固定流程

1. 所有功能、修复与测试先在 `windows`（或 `linux`）开发并提交，推到 `origin` 的同名分支。
2. 日常开发由 CI（`.github/workflows/main-branch-guard.yml` 的 `build-test` job）执行 Release 构建与两个测试工程；本地也须自行验证。
3. 正式发布时切到干净的 `main`，执行 `git merge windows --no-ff`（或 `git merge linux --no-ff`），只做发布合并。
4. 如需版本号 bump，仅改 `*.csproj` 的 `Version` / `AssemblyVersion` / `FileVersion`（三者必须一致），提交类型 `build:` 或 `chore(release):`。
5. 完成 Release 构建、测试与发布资产验证后，创建 annotated tag `git tag -a vX.Y.Z -m "..."`，再推送 `origin/main` 与 tag。

禁止把日常开发提交直接推到 `origin/main`。`main` 只接收经过发布整理与验证的干净状态。

## main 的保留范围

`main` 可以保留：

- 产品源码、必要测试源码与构建工具；
- `README.md`、`CHANGELOG.md`、`AGENTS.md`、`CONTRIBUTING.md`、许可证等长期项目说明；
- `docs/ARCHITECTURE.md`、`docs/DEVELOPMENT-ROADMAP.md`、导入清单契约等长期规范；
- 与正式版本直接相关的配置、CI 与发布基础设施。

`main` 不得包含：

- `artifacts/`、`release/`、`bin/`、`obj/`、`.build/`、`TestResults/` 等构建产物与测试输出；
- `docs/archive/**`、`docs/plans/**`、PRD、反馈、方案、调试记录等过程文档；
- 本地运行配置、凭据、用户数据、临时截图与分析脚本。

> 注：`main` 上目前仍存有 2.0.0 之前合入的历史归档（`docs/archive/**`）与 `Launcher-PRD.md`，是发布守卫正则漏检期间的遗留，留待后续发布整理清理；本规范生效后不得再新增。

## 自动代理红线

自动开发代理（Agent）开工前必须阅读根目录 `AGENTS.md`（强制红线摘要）。每次开工的 pre-flight、提交前自检和 main 允许动作均以该文件为准；若框架未自动注入，任务开始时必须显式要求先读。

## 本地防护钩子与 CI

```powershell
git config core.hooksPath .githooks
```

- `.githooks/pre-push`：拦截把 `feat:`/`fix:`/`refactor:`/`perf:`/`test:`/`style:` 等开发性提交直接推到 `main`（按 main 的 first-parent 链检查，不影响 merge 与版本 bump）。
- `.github/workflows/main-branch-guard.yml`：在 `windows` / `main` 的 push 与 PR 上运行。
  - `guard` job（仅 main 事件）：调用 `scripts/guard-main-release.sh`，检查冲突标记、csproj 版本一致性、main 上直接开发性提交、以及 PR **新增**的越界文件（`docs/archive/**`、PRD、构建产物等；main 上已跟踪的历史归档允许随发布整理修改）。
  - `build-test` job（`windows-latest`）：Release 构建 + 两个 xunit 测试工程，是推 `main` 前的硬闸门。

## 发布前最小检查

```powershell
dotnet build native\FlickD.Desktop\FlickD.Desktop.csproj -c Release --no-restore
dotnet test native\FlickD.Core.Tests\FlickD.Core.Tests.csproj -c Release
dotnet test native\FlickD.Desktop.Tests\FlickD.Desktop.Tests.csproj -c Release
git diff --check
```

`scripts/guard-main-release.sh` 由 CI 在 main 的 push / PR 事件执行（按 `origin/main..HEAD` 判断，勿在 `windows` 分支直接复跑，否则会把开发性提交误判为越界）。

打包命令、full / lite / ocr 三条资产通道与逐项发布验证见 `docs/ARCHITECTURE.md` §6。通过后再执行合并、版本 bump、创建标签与推送。
