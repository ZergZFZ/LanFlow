# LanFlow Linux 进度与交接（round12）

> 本文档供其他 agent / 开发者接手时快速对齐：任务来源、已完成改动、关键判断、踩坑记录、当前未提交状态与待办。
> 最近更新：2026-08-14（分支 `linux`）

## 1. 任务来源

`release/TEST-CARD-deb-2.md` 末尾两条补充问题：

- **问题 11**：卡片/平铺模式参照 Windows 布局优化；当前布局导致标题文字几乎看不见。
- **问题 12**：设置「布局与项目」相关设置改为滑块 + 实时预览；当前用数字框看不到图标/文字观感，调试反复试错。

## 2. 根因（已定位）

- **标题看不见**：Linux 侧 `MainWindow.axaml` 平铺模板标题 `MaxWidth` 绑到 `IconSize`（44px）被截成 1~2 字；卡片模板用正方形 `CardSize`（108×108）且标题 `MaxWidth=CardSize`，实际可用列宽只有约 44px。Windows 正确做法是磁贴固定 `CardWidth/CardHeight`（108×96）、标题两行换行居中；卡片用独立宽高、标题占满剩余宽度。
- **设置不可预览**：`SettingsWindow.axaml` 布局项用 `NumericUpDown`，改完要关窗回主界面才看得到，且无样张预览。

## 3. 已完成改动

### 3.1 布局修复（问题 11）——已提交 `d778f2f`
- `native/LanFlow.Linux/MainWindow.axaml`
  - 平铺模板：磁贴固定 `Width/Height = CardWidth/CardHeight`；标题 `MaxWidth` 由 `IconSize` → `CardWidth`，加 `TextWrapping=Wrap + TextAlignment=Center + MaxLines=2`。
  - 卡片模板：`CardSize` 拆成 `CardWidth/CardHeight`；标题去掉 `MaxWidth=CardSize`。
- `native/LanFlow.Linux/MainWindow.axaml.cs`：`ApplyMetrics` 注册 `CardWidth/CardHeight`，移除 `CardSize`。

### 3.2 设置滑块 + 实时预览（问题 12）——已提交 `d778f2f`
- `native/LanFlow.Linux/Views/SettingsWindow.axaml`：「布局与项目」面板
  - `NumericUpDown` → `Slider`（卡片宽度 48–320 / 卡片高度 48–240 / 图标 24–72 / 文字 10–18 / 项目间距 0–64 / 行间距 0–80 / 内容边距 6–40），每项右侧实时数值。
  - 顶部新增实时预览区 `PreviewHost`。
- `native/LanFlow.Linux/Views/SettingsWindow.axaml.cs`：新增 `OnLayoutSliderChanged` / `RefreshLayoutValues` / `RefreshPreview` / `BuildPreviewTile` / `BuildPreviewCard` / `BuildPreviewIcon` / `GetThemeBrush`；`InitializeState` / `OnLayoutChanged` / `OnConfirm` 同步改为读写滑块。

### 3.3 CRLF 行尾修复——已提交 `d778f2f`
- 新增 `.gitattributes`：`*.sh` / `*.desktop` 强制 `eol=lf`。
- 根因：Windows `core.autocrlf=true` 检出时把脚本转成 CRLF，shebang 变 `#!/bin/sh\r`，目标机报「解释器错误: /bin/sh^M」。仓库 blob 本就 LF，故 `.sh` 无需内容提交，`.gitattributes` 即持久修复。

### 3.4 设置按钮无响应修复——**尚未提交**
- 根因：7 个 `Slider` 在 XAML 里直接挂 `ValueChanged="OnLayoutSliderChanged"`；`InitializeComponent()` 解析期 `Minimum` 把默认 `Value(0)` 强转到下限，在其余具名控件未建好时触发 `RefreshLayoutValues` 空引用 → `SettingsWindow` 构造失败 → 点「设置」没反应。
- 修复：
  - `SettingsWindow.axaml`：去掉 7 个滑块的 `ValueChanged`。
  - `SettingsWindow.axaml.cs`：构造函数里 `InitializeState()` 之后用代码统一 `+= OnLayoutSliderChanged`（此时所有控件已创建，无空引用风险）。

## 4. 关键判断 / 决策

- **对齐 Windows**：磁贴与卡片统一用 `CardWidth/CardHeight` 作为单元尺寸（同 Windows `ApplyItemMetrics`），不再用 Linux 独有的正方形 `CardSize`。
- **`CardSize` 字段保留未删**：Windows 侧同样保留且未用，保持一致，避免破坏旧配置 JSON。
- **版本未升级**：仍为 `1.4.8`；round12 只是 UI 修复测试轮次。
- **未重打 `.deb`**：需先定版本号是否升级，再按 GNU tar `--owner=0 --group=0 --mode=755` + bsdtar `--format=ar` 流程出包。

## 5. 当前状态

- 已提交：`d778f2f`（5 文件：`MainWindow.axaml`、`MainWindow.axaml.cs`、`SettingsWindow.axaml`、`SettingsWindow.axaml.cs`、`.gitattributes`）。
- 未提交：`SettingsWindow.axaml`、`SettingsWindow.axaml.cs`（3.4 设置按钮修复）。
- 未跟踪（会话前就存在，勿误提交）：`artifacts/`、`tools/`。
- 未推送（提交/推送逐次确认）。

## 6. 产物路径

| 产物 | 路径 |
|---|---|
| tar.gz 发布包 | `E:\AI\LanFlow-main\release\LanFlow-linux-x64-round12.tar.gz`（40.5MB） |
| 解压目录（VM 共享直连） | `E:\AI\LanFlow-main\.build\linux-wt\publish\final-r12\`（VM 里对应 `/mnt/hgfs/lanflow/final-r12/`） |

打包命令（Windows 侧，Git Bash 的 GNU tar 才支持 `--mode`）：
```bash
dotnet publish native/LanFlow.Linux/LanFlow.Linux.csproj -c Release -r linux-x64 --self-contained -o .build/linux-wt/publish/final-r12
tar --mode=755 -czf release/LanFlow-linux-x64-round12.tar.gz -C .build/linux-wt/publish/final-r12 .
```
> 包内需含 `RELEASE-NOTES.md`（`vm-verify.sh` 的 B4-2 检查项）与 `lanflow.desktop` / `collect-env.sh`（从 `release/usb-test/` 拷入）。

## 7. 待办

- [ ] 提交 3.4 设置按钮修复（建议 `fix(linux): 设置窗口滑块事件初始化期空引用修复`）。
- [ ] 推送（需用户逐次确认）。
- [ ] UOS20 实机 / VM 复测：设置按钮弹出、滑块实时预览、平铺/卡片标题可见。
- [ ] （可选）决定是否升版本并重打 `.deb`。

## 8. 关键文件定位

| 文件 | 说明 |
|---|---|
| `native/LanFlow.Linux/MainWindow.axaml` | 平铺/卡片 DataTemplate（布局尺寸、标题换行） |
| `native/LanFlow.Linux/MainWindow.axaml.cs` | `ApplyMetrics` 资源注册、`OnOpenSettings` |
| `native/LanFlow.Linux/Views/SettingsWindow.axaml` | 设置面板（滑块 + 预览区） |
| `native/LanFlow.Linux/Views/SettingsWindow.axaml.cs` | 滑块/预览接线、`InitializeState`/`OnConfirm` |
| `native/LanFlow.Core/Models/AppConfig.cs` | `Settings` 模型（`CardWidth/CardHeight` 已存在） |
| `native/LanFlow.Core/ViewModels/MainViewModel.cs` | `ApplyAppearance` 的数值 clamp 范围 |
