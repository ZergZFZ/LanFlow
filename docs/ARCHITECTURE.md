# FlickD 架构与发布基线

- 基线版本：以 `CHANGELOG.md` 最新条目为准（2026-11-09 的 2.0.0 起由 LanFlow 更名为 FlickD）
- 正式客户端：`native/FlickD.Desktop`
- 技术栈：WPF + .NET 8（`net8.0-windows`）
- 支持平台：Windows 10/11 x64
- 状态：活跃 ｜ 最近更新：2026-11-09

## 1. 架构事实与决策

FlickD 当前只有一个正式客户端：Windows 原生 WPF 客户端。项目不以 WebView、Electron、Tauri、React、Node.js 或跨平台运行时作为发布依赖。`linux` 分支的 Avalonia 客户端是独立实验线，仍使用 LanFlow 命名与独立版本号，不纳入本文的 full / lite / ocr 发布通道。

当前设计选择的目的不是追求技术栈数量，而是保证以下能力稳定可用：Windows Shell 启动、资源管理器文件拖放、全局热键、系统托盘、开机自启、原生窗口效果、单文件轻量发布和自包含离线发布。

## 2. 当前代码结构

```text
native/
├── FlickD.Core/
│   ├── Models/             配置、分组、启动项、主题等数据模型
│   ├── Services/           配置读写及后续可复用的业务服务
│   └── ViewModels/         当前主界面绑定状态与已管理项目筛选
└── FlickD.Desktop/
    ├── Services/           热键、Shell 启动、快捷方式解析、图标、自启、更新
    ├── Views/              设置、分组、项目和颜色选择窗口
    ├── MainWindow.*        启动器主面板、拖放和交互编排
    └── App.*               生命周期、托盘、显示/隐藏和异常处理
```

> `FlickD.Core` 中部分现有模型仍使用 `FlickD.Desktop.*` 命名空间。这是历史兼容债务；新增领域代码应使用 `FlickD.Core.*`，但不得为了改命名空间而单独进行破坏性重构。

## 3. 运行时职责

| 层次 | 责任 | 不应承担的责任 |
|---|---|---|
| Core | 配置模型、分组/项目规则、导入预览与合并规则、可测试业务逻辑 | WPF 控件、窗口句柄、注册表和 Shell P/Invoke |
| Desktop Services | 热键、Shell、图标、快捷方式、自启、GitHub 更新等 Windows 适配 | UI 状态和复杂业务决策 |
| WPF Views | 呈现列表、对话框、用户输入、拖放反馈 | 直接解析外部文件或修改配置文件 |
| 应用编排 | 把用户动作组合为读取、预览、确认、保存与刷新 | 把平台差异散落到界面事件中 |

后续功能应优先把可验证规则放入 Core，再由 Desktop 适配 Windows 能力。不得让导入、搜索和配置迁移规则继续堆积在 `MainWindow.xaml.cs`。

## 4. 数据与配置

### 4.1 现有配置

配置路径：

```text
%AppData%\FlickD\config.json
```

核心结构：

```text
AppConfig
├── groups[]
│   ├── id / name / collapsed / sortMode
│   └── items[]
│       ├── id / name / path / icon / command / kind
│       ├── hotkey / isEnabled / useCount
└── settings
    ├── hotkey / theme / themeColors / customThemes / opacity
    ├── 布局、卡片、图标、文本和间距参数
    └── 开机自启、点击方式及显示选项
```

### 4.2 不可违反的规则

1. 保存通过临时文件后原子替换完成；
2. 外部工具不得直接编辑内部配置；
3. 新配置字段必须具有安全默认值；
4. 配置格式变更必须有版本、迁移和旧样本回归测试；
5. 导入预览、取消或校验失败阶段不得写入内存配置或磁盘配置；
6. 路径、快捷方式和图标解析失败必须可提示、可跳过，不能导致主窗口不可用。

## 5. 后续模块化边界

以下是增量目标，不是一次性目录重写要求。

```text
FlickD.Core/
├── Models/                 稳定领域模型
├── Import/                 Manifest DTO、校验、预览、合并计划
├── Search/                 已管理项目匹配、排序与键盘选择规则
├── Configuration/          配置版本与迁移规则
└── Services/               不依赖 WPF 的应用服务

FlickD.Desktop/
├── Services/               Windows 平台适配器
├── Views/                  WPF 窗口与控件
├── ViewModels/             展示状态和命令
└── Composition/            服务装配与应用编排
```

工作区检索只以 `AppConfig` 中的已管理项目为输入，不得在查询过程中扫描开始菜单、用户目录或全盘文件，也不得依赖 Everything 等外部服务。Everything、资源管理器和开始菜单属于 FlickD 外部的查找工具；用户通过拖放、手动添加或 `import-manifest` 将明确选择的目标带入工作区。搜索匹配、排序和键盘选择规则进入 Core，WPF 只负责接收输入、显示结果和转发启动动作。

## 6. 发布与更新规范

### 6.1 两个固定发布通道

GitHub 历史发布从 v1.3.4 起稳定使用以下资产约定；应用内更新服务也按此命名匹配通道：

| 通道 | 构建配置 | GitHub 资产名 | 交付方式 | 前置条件 |
|---|---|---|---|---|
| full | `win-x64.pubxml`，`SelfContained=true` | `FlickD-{version}-full.zip` | 下载后完整解压，在压缩包根目录运行 `FlickD.exe` | 无需预装 .NET 运行时 |
| lite | `win-x64-lite.pubxml`，`SelfContained=false` | `FlickD-{version}-lite.exe` | 单文件直接运行 | .NET 8 Windows Desktop Runtime x64 |
| ocr | `tools/Build-OcrBundle.ps1` | `FlickD-{version}-ocr.zip` | 应用内下载或"从文件导入" | 可选本地离线取字组件，默认不安装 |

> OCR 组件（`-ocr.zip`）是按需可选资产，主程序启动阶段不检查、不加载它；用户在「设置 → 扩展与 OCR」手动开启后才下载或导入。**2.1.0 起 full 包不再内嵌 `ocr/` 目录**，两条通道都通过应用内下载或本地文件导入获得组件；`OcrBundleLocator` 保留对应用目录 `ocr/` 的查找，仅为让 2.0.x 老安装升级后不丢失其目录中已内置的组件。

**兼容壳已移除**：2.0.0 / 2.0.1 的 full 包为让 1.6.x 更新器完成一次在线升级，曾附带 `LanFlow.exe` 兼容壳（`native/FlickD.CompatShell`，约 10 MB）。该过渡期已于 2.1.0 结束：工程删除，发布包不再包含 `LanFlow.exe`，更新器只识别 `FlickD.exe`。旧安装目录里残留的 `LanFlow.exe` 不会被更新器删除，可手动清理。

历史版本中 lite 曾使用 `-lite.zip`，仅为旧版本更新兼容保留；**新版本禁止继续发布 lite ZIP**。full 曾出现 `-full-win-x64.zip`，**新版本统一使用 `-full.zip`**。

### 6.2 为什么 full 必须是 ZIP

full 虽启用了 `PublishSingleFile`，但自包含 WPF 发布仍会带有原生运行时依赖。`IncludeNativeLibrariesForSelfExtract=false` 时，这些文件保留在发布目录中。因此：

- full 必须压缩整个发布目录，而不是只复制 `FlickD.exe`；
- ZIP 解压后的根目录必须直接包含 `FlickD.exe` 和所需依赖，不能多套一层版本目录；
- 不得手工删除依赖 DLL、运行时文件或更新服务识别 full 通道所需的原生文件；
- 任何 full 包均必须在未安装 .NET Desktop Runtime 的干净 Windows x64 环境验证启动。

### 6.3 打包命令与目录约定

在仓库根目录执行；发布版本号必须先与 `FlickD.Desktop.csproj` 中的 `Version`、`AssemblyVersion` 和 `FileVersion` 一致。

```powershell
# 一键打包：脚本自读 csproj 版本，不需要传版本号
powershell -ExecutionPolicy Bypass -File tools/Build-ReleaseAssets.ps1

# 复核当前待发布版本的三条资产（版本必须与 csproj / CHANGELOG 一致）
powershell -ExecutionPolicy Bypass -File tools/Verify-ReleaseAssets.ps1 -Version <version>

# CI / 提交前：只校验 publish 目录形状，无需 OCR 素材与网络
powershell -ExecutionPolicy Bypass -File tools/Verify-ReleaseAssets.ps1 -PublishShape `
    -FullDir <full 发布目录> -LiteDir <lite 发布目录>
```

> `-Version` 会同时校验 csproj `Version` 与 `CHANGELOG.md` 最新正式条目，因此**只适用于当前待发布版本**；历史版本资产形状不同（2.0.x 含兼容壳、内嵌 `ocr/` 与 PDB），不要用本脚本复核，其静态复核结论见 `docs/performance/R4-baseline.md` §8。若目标版本的三条资产已存在（通常意味着已发布），打包脚本会拒绝覆盖，确需重打包须显式加 `-Force`。

`Build-ReleaseAssets.ps1` 依次执行：

1. 从 `FlickD.Desktop.csproj` 读 `<Version>`，前置核对 `CHANGELOG.md` 最新正式条目并拒绝覆盖已存在资产；
2. `dotnet publish -p:PublishProfile=win-x64`（full）与 `win-x64-lite`（lite）；
3. 删除发布目录中的 `*.pdb`，确认无 `LanFlow.exe` 与 `ocr/`（调用 `-PublishShape` 自检）；
4. `Compress-Archive` 整目录为 `-full.zip`，复制单文件为 `-lite.exe`；
5. 调用 `Build-OcrBundle.ps1` 生成 `-ocr.zip`（不传 `-AppVersion`，脚本自读 csproj）；
6. 为三条资产生成 `<name>.sha256` sidecar，最后执行 `Verify-ReleaseAssets.ps1 -Version` 全量自检。

两个脚本的 PowerShell 语法与 `-PublishShape` 正反例由 CI 的 `release-scripts` job 强制执行，脚本改动必须通过该 job。

发布物只上传：

```text
FlickD-{version}-full.zip
FlickD-{version}-lite.exe
FlickD-{version}-ocr.zip
```

不要上传 `bin/`、`obj/`、未压缩 full 临时目录、PDB 或中间安装脚本。

### 6.4 发布前验证

每次 Release 至少执行：

1. `dotnet build native/FlickD.Desktop/FlickD.Desktop.csproj -c Release`；
2. 运行 `native/FlickD.Core.Tests` 与 `native/FlickD.Desktop.Tests` 两个 xunit 测试工程（CI 的 `build-test` job 使用同一口径，必须全绿）；
3. 用 `Verify-ReleaseAssets.ps1 -Version <version>` 复核三条资产：full ZIP 根目录直接含 `FlickD.exe` 与完整发布依赖，且**不含** `LanFlow.exe`、`*.pdb` 与 `ocr/`；三条 SHA-256 与 sidecar 一致；lite 是合法 PE 且 `FileVersion` 与版本一致；
4. 确认 GitHub Release 三条资产均带 `digest`：更新器 2.1.0 起采用 fail-closed 校验，缺 digest 会拒绝覆盖安装目录。可用 `gh api repos/ZergZFZ/LanFlow/releases/latest --jq '.assets[] | .name + " | " + (.digest // "NONE")'` 检查；
5. 用全新解压目录启动 full，并验证托盘、热键、添加/启动项目、设置与退出；
6. 在具备 .NET 8 Windows Desktop Runtime x64 的环境启动 lite，并验证相同核心流程；
7. 在两条通道中检查更新，确认 full 只匹配 `-full.zip`、lite 优先匹配 `-lite.exe`，且下载校验失败时不会覆盖安装目录；
8. 确认 GitHub Tag、Release 标题、资产文件名和程序集版本完全一致；
9. 确认 `-ocr.zip` 内 `manifest.json` 的路径均为 `runtime/`、`models/`、`vcredist/` 前缀，且 12 个文件的 SHA-256 与 `contentHash` 校验通过（可用 `OcrManifestVerifier` 或解压后逐文件比对）。

> 第 5–7 项为实机 GUI 验证，无人值守环境无法执行；2.0.1 发布时即未完成，需在本机补验。

### 6.5 更新兼容规则

`UpdateService` 以运行目录中的 WPF 原生依赖识别 full 通道；若无法识别，则读取 `channel.txt`，最终默认 lite。更新服务下载的 full ZIP 会整体覆盖安装目录，lite EXE 会被统一命名为 `FlickD.exe` 后覆盖。

**2.1.0 起只认单一主程序名 `FlickD.exe`**，不再回退 `LanFlow.exe`；旧安装目录中残留的兼容壳不参与更新，可手动清理。

- **下载完整性**：使用 GitHub Release API 的 `digest`（`sha256:<64 hex>`）边下载边计算 SHA-256，并比对 `size`。摘要缺失、格式非法、大小不符或哈希不符一律取消更新、不覆盖安装目录，提示用户手动下载；因此发布前必须确认资产带 `digest`（§6.4 第 4 项）。
- **覆盖与回滚**：覆盖脚本先把整个安装目录 `xcopy` 到临时备份；`xcopy` 部分失败或新进程 5 秒内未启动时整体回滚；回滚本身失败则保留备份目录并在 `%TEMP%\FlickDUpdateFailure.txt` 写出提示，不会删除备份。成功后清理临时下载目录，避免 full 包（约 90 MB）残留在 `%TEMP%`。

因此不得更改上述资产后缀、不得把 full 发布为单个 EXE、也不得将 full ZIP 包装成额外顶层目录，否则应用内更新可能无法正确选择或覆盖文件。

### 6.6 2.0.0 更名迁移

| 对象 | 旧位置 | 新位置 | 迁移方式 |
|---|---|---|---|
| 配置 | `%APPDATA%\LanFlow\config.json` | `%APPDATA%\FlickD\config.json` | 首次启动复制，旧目录保留 |
| 配置定位 | `%APPDATA%\LanFlow\config-location.json` | `%APPDATA%\FlickD\config-location.json` | 优先复制，避免自定义目录用户读到空配置 |
| 开机自启 | Run 值 `LanFlow` | Run 值 `FlickD` | 启动时按当前 exe 重建并删除旧值 |
| 单实例 | `Local\LanFlow.SingleInstance` | `Local\FlickD.SingleInstance` | 过渡期同时探测旧互斥量，接入旧管道唤醒 |
| OCR 组件 | `%APPDATA%\LanFlow\ocr` | `%APPDATA%\FlickD\ocr` | 不迁移：旧组件引用 `LanFlow.Core`，更名后二进制不兼容，需重新下载一次；旧目录保留可手动删除 |
| 日志 | `%LOCALAPPDATA%\LanFlow` | `%LOCALAPPDATA%\FlickD` | 不迁移，仅切换写入位置 |


## 7. 验证策略

- **手工回归**：导入、搜索、配置读写、热键、托盘、关闭隐藏、拖放、编辑、启动、主题、更新等核心流程；
- **发布验证**：full/lite 分别在对应前置条件下启动与更新；
- **配置样本回归**：使用真实用户配置副本验证旧数据可读取、保存和升级。

任何会修改 `AppConfig`、发布通道或更新规则的变更，都必须同时更新本文和变更日志（`CHANGELOG.md`）。
