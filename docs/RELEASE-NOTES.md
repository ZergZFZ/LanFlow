# LanFlow Linux 发布说明

> 状态：活跃 ｜ 最近更新：2026-08-10

> 更新机制决策（基线 §5）：Linux 版**不做应用内更新**，采用「发布说明 + 手动解压替换」。本文件即发布说明，随每个测试包分发（`RELEASE-NOTES.md`）。

## 1. 当前版本

| 项 | 值 |
|---|---|
| 包名 | `LanFlow-linux-x64-roundN.tar.gz`（N = 轮次，见 §3） |
| 目标系统 | UOS 20 Pro / Deepin（x86_64，glibc ≥ 2.28，X11） |
| 运行时 | .NET 8 自包含（无需预装 .NET） |
| 源码分支 | github.com/ZergZFZ/LanFlow（linux 分支） |

## 2. 安装与更新（手动解压替换）

1. **备份**：若已安装旧版，先备份数据目录 `~/.config/LanFlow/config.json`（新版配置不兼容旧版时的恢复用）。
2. **解压**：`tar -xzf LanFlow-linux-x64-roundN.tar.gz -C ~/LanFlow`（或任意目录）。
3. **替换**：删除/覆盖旧目录中同名文件后放入新文件；若目录结构不变可直接覆盖解压。
4. **授权**（若包内含 `lanflow.desktop`）：重新执行 `chmod +x lanflow.sh run-lanflow.sh LanFlow`。
5. **启动**：双击 `lanflow.sh` 或从应用菜单/终端运行。
6. **验证**：终端启动并查看日志，日志含 `[LanFlow]` 行即正常；首次运行可执行 `./collect-env.sh` 留存环境快照。

> 配置位置：`~/.config/LanFlow/config.json`（B5-4 换位置后以当时文档为准）。

## 3. 版本轮次约定

- 每轮测试包 = 一个有效 commit + 取证日志；轮次号递增（如 round3.11 为缺陷板结案轮）。
- 发布说明随包更新：新增功能、修复项、已知问题见下。
- 测试卡：包内 `TEST-CARD-rN.md` 为本轮验收清单，U 盘拷贝至目标机逐项勾测。
- **打包权限注意**：必须用 GNU tar（Git Bash 自带）加 `--mode=755` 打包，否则 Windows bsdtar 不保留 Unix 可执行位，目标机 `./lanflow.sh` 会报「没有那个文件或目录」（round4 首包踩坑，已修正；复现修复命令见下）。

```bash
# Windows 上正确打包（Git Bash 的 tar 才支持 --mode）
"D:\Dev\tools\git\usr\bin\tar.exe" --mode=755 -czf LanFlow-linux-x64-roundN.tar.gz -C <pkg_dir> .
```

## 4. 已知限制（与 Windows 版差异）

| 项 | 说明 |
|---|---|
| 更新 | 无应用内更新；手动解压替换（本文件 §2） |
| 热键 | 推荐字母/数字 + 修饰键（Ctrl+Alt+Q 等）；符号键在中文布局不保证（D13 结案） |
| 图标 | SVG 图标因 glibc 2.28 兼容回退暂缺（D8）；PNG/系统图标可用 |
| 外观 | Deepin 原生装饰窗口；无 Windows 无边框+阴影效果 |
| 内存 | 目标 ≤450MB（启用取证期间，D5 收口） |

## 5. 本轮变更（round5，2026-08-10，包 final-b5）

> 本轮无 B 批次新增，均为 VM 验证中发现问题的修复；验收点见包内 `TEST-CARD-r4.md`。

| 类别 | 内容 |
|---|---|
| 修复 | 任务栏图标消失延迟：`Opened` 事件每次唤回把失焦抑制覆盖为 2s 的 bug（改仅首次）；轮询 400→150ms；唤回抑制 800→600ms；隐藏后跳过 X 查询 |
| 修复 | 配置目录统一解析（B3-6）：`LANFLOW_CONFIG_DIR` 覆盖跟随到设置页性能路径显示 |
| 修复 | B2-1 失焦隐藏：hotkey 唤回被吞、hide=true 启动即隐藏（见基线 round5-b2） |
| 已知限制 | Deepin 唤回抢焦期（~300ms）内点桌面偶发被 WM 吞焦（需再点一次）；Topmost 抢焦成功率平台波动 |
| 源码 commit | linux 分支 `7f14d50`（文档基线 `909be23`，ConfigDir `7b809eb`） |

## 6. 本轮变更（round6，2026-08-10，包 final-r6）

> 用户实机反馈的三大基础体验问题修复；验收点见包内 `TEST-CARD-r6.md`。

| 类别 | 内容 |
|---|---|
| 修复 | 设置页开关「看起来不能动」：选中/未选中背景色差异几乎不可见 + 圆点不移动，点击后无任何视觉反馈。重做 ToggleButton 模板——圆点随 IsChecked 平滑滑动（ThicknessTransition），选中态背景切换 AccentBrush，hover 微反馈 |
| 修复 | 浅色主题下设置窗口白底白字：`ApplyThemeColors` 切换画刷时同步 `RequestedThemeVariant = Light`（否则 FluentTheme 保持 Dark，控件默认前景仍为白色系）；设置窗口补 `Foreground` 兜底 |
| 修复 | 透明度滑块「失效」感知：滑块无任何数值/说明反馈，且分层模式仅项目区内容透明、窗口背景不变。新增滑块实时百分比显示 + 透明模式说明文字 |
| 说明 | 分层透明 = 仅项目区内容半透明（顶部搜索栏/底部按钮栏/分组栏保持不透明）；整窗透明 = 整个窗口半透明（X11 走 `_NET_WM_WINDOW_OPACITY`） |
| 源码 commit | linux 分支 `0ae1a81`（round6 三大基础体验修复） |

## 7. 本轮变更（round7，2026-08-10，包 r7）

> round6 三大基础体验问题 UOS 实机全部通过（D14–D16 结案）；本轮修复实机后发现的
> 主题保存回退逻辑缺陷（D17）。验收点见包内 `TEST-CARD-r7.md`。

| 类别 | 内容 |
|---|---|
| 结案 | round6 三大问题（开关视觉反馈 / 浅色白底白字 / 透明度滑块）UOS 实机全部通过 |
| 修复 | 主题保存回退深色（D17）：选浅色保存后再次进入设置，改动非主题项再保存会回退深色。根因——切换主题时未同步「主题配置名称」文本框，`OnConfirm` 用残留的旧名称（默认"深色"）覆盖 `ThemeProfile`，下次进入按"深色"恢复选中项导致主题重置。修复：`OnProfileChanged` 同步 `ThemeProfileBox.Text` |
| 源码 commit | linux 分支 `9132a6a`（D17 主题保存回退修复） |
