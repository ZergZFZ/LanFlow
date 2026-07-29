# LanFlow 架构与发布基线

- 基线版本：v1.3.7
- 正式客户端：`native/LanFlow.Desktop`
- 技术栈：WPF + .NET 8（`net8.0-windows`）
- 支持平台：Windows 10/11 x64
- 更新日期：2026-07-29

## 1. 架构事实与决策

LanFlow 当前只有一个正式客户端：Windows 原生 WPF 客户端。项目不以 WebView、Electron、Tauri、React、Node.js 或跨平台运行时作为发布依赖。

当前设计选择的目的不是追求技术栈数量，而是保证以下能力稳定可用：Windows Shell 启动、资源管理器文件拖放、全局热键、系统托盘、开机自启、原生窗口效果、单文件轻量发布和自包含离线发布。

## 2. 当前代码结构

```text
native/
├── LanFlow.Core/
│   ├── Models/             配置、分组、启动项、主题等数据模型
│   ├── Services/           配置读写及后续可复用的业务服务
│   └── ViewModels/         当前主界面绑定状态与已管理项目筛选
├── LanFlow.Desktop/
│   ├── Services/           热键、Shell 启动、快捷方式解析、图标、自启、更新
│   ├── Views/              设置、分组、项目和颜色选择窗口
│   ├── MainWindow.*        启动器主面板、拖放和交互编排
│   └── App.*               生命周期、托盘、显示/隐藏和异常处理
└── LanFlow.Core.Tests/     Core 自动化测试（应作为后续功能的必要基线）
```

> `LanFlow.Core` 中部分现有模型仍使用 `LanFlow.Desktop.*` 命名空间。这是历史兼容债务；新增领域代码应使用 `LanFlow.Core.*`，但不得为了改命名空间而单独进行破坏性重构。

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
%AppData%\LanFlow\config.json
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
LanFlow.Core/
├── Models/                 稳定领域模型
├── Import/                 Manifest DTO、校验、预览、合并计划
├── Search/                 已管理项目匹配、排序与键盘选择规则
├── Configuration/          配置版本与迁移规则
└── Services/               不依赖 WPF 的应用服务

LanFlow.Desktop/
├── Services/               Windows 平台适配器
├── Views/                  WPF 窗口与控件
├── ViewModels/             展示状态和命令
└── Composition/            服务装配与应用编排
```

工作区检索只以 `AppConfig` 中的已管理项目为输入，不得在查询过程中扫描开始菜单、用户目录或全盘文件，也不得依赖 Everything 等外部服务。Everything、资源管理器和开始菜单属于 LanFlow 外部的查找工具；用户通过拖放、手动添加或 `import-manifest` 将明确选择的目标带入工作区。搜索匹配、排序和键盘选择规则进入 Core，WPF 只负责接收输入、显示结果和转发启动动作。

## 6. 发布与更新规范

### 6.1 两个固定发布通道

GitHub 历史发布从 v1.3.4 起稳定使用以下资产约定；应用内更新服务也按此命名匹配通道：

| 通道 | 构建配置 | GitHub 资产名 | 交付方式 | 前置条件 |
|---|---|---|---|---|
| full | `win-x64.pubxml`，`SelfContained=true` | `LanFlow-{version}-full.zip` | 下载后完整解压，在压缩包根目录运行 `LanFlow.exe` | 无需预装 .NET 运行时 |
| lite | `win-x64-lite.pubxml`，`SelfContained=false` | `LanFlow-{version}-lite.exe` | 单文件直接运行 | .NET 8 Windows Desktop Runtime x64 |

历史版本中 lite 曾使用 `-lite.zip`，仅为旧版本更新兼容保留；**新版本禁止继续发布 lite ZIP**。full 曾出现 `-full-win-x64.zip`，**新版本统一使用 `-full.zip`**。

### 6.2 为什么 full 必须是 ZIP

full 虽启用了 `PublishSingleFile`，但自包含 WPF 发布仍会带有原生运行时依赖。`IncludeNativeLibrariesForSelfExtract=false` 时，这些文件保留在发布目录中。因此：

- full 必须压缩整个发布目录，而不是只复制 `LanFlow.exe`；
- ZIP 解压后的根目录必须直接包含 `LanFlow.exe` 和所需依赖，不能多套一层版本目录；
- 不得手工删除依赖 DLL、运行时文件或更新服务识别 full 通道所需的原生文件；
- 任何 full 包均必须在未安装 .NET Desktop Runtime 的干净 Windows x64 环境验证启动。

### 6.3 打包命令与目录约定

在仓库根目录执行；发布版本号必须先与 `LanFlow.Desktop.csproj` 中的 `Version`、`AssemblyVersion` 和 `FileVersion` 一致。

```powershell
$version = '1.3.7'
$project = 'native/LanFlow.Desktop/LanFlow.Desktop.csproj'
$dist = "artifacts/$version"

# full：自包含发布目录，再压缩目录内部文件
$fullDir = "$dist/full"
dotnet publish $project -c Release -p:PublishProfile=win-x64 -o $fullDir
Compress-Archive -Path "$fullDir/*" -DestinationPath "$dist/LanFlow-$version-full.zip" -Force

# lite：框架依赖的单文件可执行程序
dotnet publish $project -c Release -p:PublishProfile=win-x64-lite -o "$dist/lite"
Copy-Item "$dist/lite/LanFlow.exe" "$dist/LanFlow-$version-lite.exe" -Force
```

发布物只上传：

```text
LanFlow-{version}-full.zip
LanFlow-{version}-lite.exe
```

不要上传 `bin/`、`obj/`、未压缩 full 临时目录、PDB 或中间安装脚本。

### 6.4 发布前验证

每次 Release 至少执行：

1. `dotnet build native/LanFlow.Desktop/LanFlow.Desktop.csproj -c Release`；
2. 运行 Core 自动化测试；
3. 检查 full ZIP 根目录包含 `LanFlow.exe`，并保留完整发布依赖；
4. 检查 lite 资产是一个可执行的 `LanFlow-{version}-lite.exe`；
5. 用全新解压目录启动 full，并验证托盘、热键、添加/启动项目、设置与退出；
6. 在具备 .NET 8 Windows Desktop Runtime x64 的环境启动 lite，并验证相同核心流程；
7. 在两条通道中检查更新，确认 full 只匹配 `-full.zip`、lite 优先匹配 `-lite.exe`；
8. 确认 GitHub Tag、Release 标题、资产文件名和程序集版本完全一致。

### 6.5 更新兼容规则

`UpdateService` 以运行目录中的 WPF 原生依赖识别 full 通道；若无法识别，则读取 `channel.txt`，最终默认 lite。更新服务下载的 full ZIP 会整体覆盖安装目录，lite EXE 会被统一命名为 `LanFlow.exe` 后覆盖。

因此不得更改上述资产后缀、不得把 full 发布为单个 EXE、也不得将 full ZIP 包装成额外顶层目录，否则应用内更新可能无法正确选择或覆盖文件。

## 7. 验证策略

- **Core 单元测试**：导入格式、路径规范化、去重、合并、取消、失败回滚和配置迁移；
- **Desktop 手工回归**：热键、托盘、关闭隐藏、拖放、编辑、启动、主题、更新；
- **发布验证**：full/lite 分别在对应前置条件下启动与更新；
- **配置样本回归**：使用真实用户配置副本验证旧数据可读取、保存和升级。

任何会修改 `AppConfig`、发布通道或更新规则的变更，都必须同时更新本文和对应测试。
