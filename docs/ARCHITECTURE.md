# LanFlow 架构说明

> 状态：活跃 ｜ 最近更新：2026-07-27

## 当前阶段

项目处于桌面启动器的双客户端迁移期：

- `src/` + `src-tauri/`：现有 Tauri/React 原型，继续保留以兼容现有开发与验证流程。
- `native/LanFlow.Desktop/`：正式版 Windows 原生客户端，使用 `WPF + .NET 8`，不依赖 WebView、Node.js 或浏览器控件。

正式版 UI 的新增功能应优先落在 WPF 客户端；Tauri 版只做必要的兼容性维护，直到原生版完成发布替换。

## 原生客户端

### 技术边界

- 目标平台：Windows 10/11 x64。
- UI：WPF 原生 `TextBox`、`ListBox`、`ListView`、`ContextMenu`、`MessageBox`、模态 `Window`。
- 系统能力：Windows Shell 启动、系统托盘、`Alt+Space` 全局快捷键、资源管理器文件拖放。
- 不使用 WebView、HTML、CSS、React、Tauri 或 Node.js 作为原生客户端界面依赖。

### 配置兼容性

原生客户端继续使用：

```text
%AppData%\com.lanflow.desktop\config.json
```

其模型兼容现有配置字段：

- `groups[]`：`id`、`name`、`collapsed`、`items[]`
- `items[]`：`id`、`name`、`path`、`icon`
- `settings`：`hotkey`、`theme`、`opacity`

配置通过临时文件后原子替换保存，避免异常退出损坏用户数据。

### 目录职责

```text
native/LanFlow.Desktop/
├── Models/        JSON 兼容数据模型
├── Services/      配置存储、Shell 启动、全局快捷键
├── ViewModels/    WPF 绑定状态与搜索筛选
├── Views/         原生编辑分组和启动项的模态窗口
├── MainWindow.*   原生启动器主窗口
└── App.*          生命周期、托盘及显示/退出行为
```

### 当前已实现能力

- 读取和安全保存既有配置；
- 分组选择、搜索、创建、重命名、删除；
- 启动项添加、编辑、删除、双击或右键启动；
- 资源管理器将文件拖入项目列表；
- 原生确认框、编辑对话框和右键菜单；
- 系统托盘、关闭隐藏、托盘退出和 `Alt+Space` 唤起；
- 独立 `win-x64` 单文件发布。

### 构建与发布

```powershell
$env:PATH = 'C:\Program Files\dotnet;' + $env:PATH
dotnet build native\LanFlow.Desktop\LanFlow.Desktop.csproj -c Debug
```

发布：

```powershell
dotnet publish native\LanFlow.Desktop\LanFlow.Desktop.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishReadyToRun=true -o native\LanFlow.Desktop\publish\win-x64
```

输出可执行文件：`native\LanFlow.Desktop\publish\win-x64\LanFlow.exe`。

## 后续发布替换条件

原生版在替换 Tauri 版前，需要完成：

1. 分组和启动项的内部拖放排序；
2. 全局快捷键设置持久化与冲突提示；
3. 程序/快捷方式图标提取和显示；
4. 设置窗口、主题和透明度；
5. 安装器、升级、签名和 Windows Defender/SmartScreen 发布验证；
6. 对既有真实配置的迁移和回归测试。
