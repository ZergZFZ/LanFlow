# LanFlow Linux 架构与发布规范

> 状态：活跃 ｜ 最近更新：2026-08-13
> 版本：1.4.8（round11）｜ 目标系统：UOS 20 Pro / Deepin（x86_64，glibc ≥ 2.28，X11）
> 关联文档：[LanFlow-Linux基线.md](LanFlow-Linux基线.md)（功能/UI 基线）、[LanFlow-Linux任务清单.md](LanFlow-Linux任务清单.md)（B1–B5）、[LanFlow-缺陷板与修复冲刺计划.md](LanFlow-缺陷板与修复冲刺计划.md)（D1–D17）、[RELEASE-NOTES.md](RELEASE-NOTES.md)（随包发布说明）

## 1. 技术栈与定位

LanFlow Linux 版是 Windows WPF 客户端（`native/LanFlow.Desktop`，在 windows 分支演进）的 **Avalonia 移植**：

- UI：Avalonia 11.3.18（FluentTheme + 自定义样式/token），AXAML 声明式界面；
- 运行时：.NET 8 自包含 `linux-x64` 单文件发布，目标机无需预装 .NET；
- 平台：X11（UOS/Deepin 桌面），glibc ≥ 2.28（UOS 20 基线硬约束）；
- 共享逻辑：`native/LanFlow.Core`（与 Windows 版共享配置模型与业务服务，物理副本随分支同步维护）。

## 2. 工程结构

```text
native/
├── LanFlow.Core/           与 Windows 共享的配置模型/服务（net8.0 纯 C#）
├── LanFlow.Linux/          Avalonia 客户端（Linux 正式交付物）
├── LanFlow.Desktop/        Windows WPF 客户端（本分支保留；Windows 线在 windows 分支演进）
├── LanFlow.Core.Tests/     自动化测试（随分支保留）
└── LanFlow.Desktop.Tests/  自动化测试（随分支保留）
```

## 3. 运行时职责

| 层 | 责任 |
|---|---|
| Core | `AppConfig` 模型、`ConfigStore`（原子写、版本迁移 v0→v1、`LANFLOW_CONFIG_DIR` 换位置）、`MainViewModel` 等平台无关逻辑 |
| Linux 平台适配（`native/LanFlow.Linux/Services/`） | `HotkeyService`（X11 `XGrabKey` 单线程模型 + 错误回路，字母/数字键可靠）、`ShellIconService`（freedesktop 图标主题 + 自研轻量 SVG 渲染器）、`StartupService`（`~/.config/autostart/`）、`LauncherService`（`.desktop` ParseDesktop → Exec，失败回退 xdg-open）、托盘（Avalonia `TrayIcon`） |
| Views（AXAML） | 主窗口、设置 8 分类、编辑窗口、右键菜单等 |

## 4. 数据与配置

- 配置路径：`~/.config/LanFlow/config.json`，可用 `LANFLOW_CONFIG_DIR` 环境变量覆盖；
- 配置含 `version` 字段（当前 1）；读取失败不静默覆盖为空配置；
- 保存通过临时文件后原子替换；
- `.deb` 卸载不触碰用户数据目录（dpkg 不清理 home）。

## 5. 发布规范

### 5.1 两个发布形态

| 形态 | 资产 | 用途 |
|---|---|---|
| tar.gz | `LanFlow-linux-x64-roundN.tar.gz` | U 盘离线分发首选；解压后双击 `LanFlow` 或 `./lanflow.sh` |
| .deb | `lanflow_{version}_amd64.deb` | UOS/Deepin 双击安装；应用菜单出现 LanFlow；装入 `/opt/lanflow` |

### 5.2 包内容

- **tar.gz（单文件发布）**：`PublishSingleFile=true` + `IncludeNativeLibrariesForSelfExtract=true`；解压后仅 `LanFlow`（ELF，内嵌运行时）+ `lanflow.sh` + `lanflow.png` + `install-lanflow.sh`。
- **.deb**：`/opt/lanflow`（与同 round tar.gz **相同二进制**及辅助文件）+ `/usr/share/applications/lanflow.desktop` + `/usr/share/icons/hicolor/256x256/apps/lanflow.png` + `/usr/share/doc/lanflow/copyright` + `postinst`（补可执行位 + 刷新桌面/图标缓存，全部 `|| true` 容错）。

### 5.3 构建与打包（Windows 开发机）

- 构建：`dotnet build native/LanFlow.Linux/LanFlow.Linux.csproj -c Release`（0 错误；现存警告均为过时 API 类，属已知）；
- tar.gz 必须用 GNU tar 加 `--mode=755` 打包（Windows bsdtar 不保留 Unix 可执行位，round4 踩坑记录）；
- .deb：无 dpkg-deb 时用 GNU tar（`--owner=0 --group=0 --mode=755`）生成 control/data 归档 + bsdtar `--format=ar` 组合；`control` 的 `Version` 必须与 csproj `Version` 一致；
- 同一 round 的 tar.gz 与 .deb 必须包含**相同二进制**（发布前校验 SHA256 一致）。

### 5.4 发布前校验清单

1. 构建 0 错误；
2. 版本一致性：csproj `Version` = .deb `control` `Version` = 资产文件名；
3. 包结构核验：ar 三成员（debian-binary 2.0 / control.tar.gz / data.tar.gz）、data 载荷与权限（root/0、755）、ELF 魔数、tar 内可执行位；
4. 二进制一致性：tar.gz 与 .deb 内 `LanFlow` SHA256 相同；
5. glibc 基线：SkiaSharp + `SkiaSharp.NativeAssets.Linux` 钉住 **2.88.9**，不得引入要求 glibc ≥ 2.29 的 3.x 系原生库（D7/D8 教训）；
6. UOS 实机终验（本机不可代验）：.deb 安装/卸载（TEST-CARD-deb）、外部拖放、渲染细节、热键触发复核。

## 6. 更新机制（取舍）

Linux 版**不做应用内更新**：发布说明（RELEASE-NOTES.md）+ 手动解压替换；`.deb` 版本号递增重装即升级，`~/.config/LanFlow` 数据保留。

## 7. 已知约束（决策记录）

| 项 | 结论 |
|---|---|
| 热键 | 推荐字母/数字 + 修饰键（Ctrl+Alt+Q 等）；符号键在中文布局不保证（D13 结案） |
| 图标 | SVG 经自研轻量渲染器恢复（round9）；保持 SkiaSharp 2.88.9 基线，不引入 3.x 系原生库 |
| 拖放 | 外部拖放为 X11 平台行为（D2）；「添加文件」兜底按钮为产品级回落 |
| 外观 | Deepin 原生装饰窗口，不与 Windows 无边框 + 阴影强行对齐 |
| 内存 | 目标 ≤ 450MB（取证启用期间，D5 收口） |
