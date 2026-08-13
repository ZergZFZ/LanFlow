# LanFlow Linux 任务清单（B1–B5）

> 状态：活跃 ｜ 最近更新：2026-08-13
> 依据：[LanFlow-Linux基线.md](LanFlow-Linux基线.md)（Windows 对照差距与批次）；缺陷板 D1–D13 已结案。
> 约定：每批验收闭环 = 构建 0 错误 → Headless 探针复验 → 实机包 + 取证日志 → 更新本清单状态与完成记录。

## 1. 状态约定

- 状态取值：**待办 / 进行中 / 已完成**；完成后在 §3 完成记录登记 commit + 测试包 + 实机结果。
- 执行顺序：按批次 B1 → B5；批内按 ID 依赖顺序；B1（功能闭环）为当前优先。

## 2. 任务清单

### B1 功能闭环（已完成代码，实机待验证）

| ID | 任务 | 说明与验收标准 | 依赖 | 状态 |
|---|---|---|---|---|
| B1-1 | 应用批量添加 | 文件/文件夹多选添加；"从目录导入"扫描目录内 `.desktop`/可执行文件。验收：一次导入 ≥2 个目标全部入库且可启动 | 无 | 已完成 |
| B1-2 | 新加自动命名 | `.desktop` 解析 Name（复用 ParseDesktop）；可执行文件取文件名去扩展名；命令取命令名。验收：添加后名称自动填充，无需手改 | B1-1 | 已完成 |
| B1-3 | 拖放收口 | 外部拖放（D2）尽力修复；兜底"添加文件"按钮保留并支持批量。验收：至少一条路径可批量添加；实机取证 | 无 | 已完成 |
| B1-4 | 分组/项目排序 | 编辑模式下移/上移按钮（分组标签、组内项目），排序持久化。验收：排序后重启保留；探针复验 | 无 | 已完成 |
| B1-5 | 搜索行为核对 | 跨全部分组筛选、空查询保持当前分组、键盘上下键/Enter/Esc。验收：探针核对行为与 Windows 一致（可能已具备，仅核对） | 无 | 已完成 |

### B2 交互体验（已完成代码，实机待验证）

| ID | 任务 | 说明与验收标准 | 依赖 | 状态 |
|---|---|---|---|---|
| B2-1 | 窗口失焦隐藏 | 设置开关：失焦自动隐藏（托盘常驻）。验收：开关生效可关、不误吞输入 | B3-6 | 已完成 |
| B2-2 | 编辑模式交互 | 排序按钮 + 删除 + 状态提示完整 | B1-4 | 已完成 |
| B2-3 | 空状态提示 | 分组无项目 / 搜索无结果时显示引导文案 | 无 | 已完成 |
| B2-4 | 分组悬停切换 | 点击/悬停可配 + 悬停延迟设置 | 无 | 已完成 |
| B2-5 | 动画偏好 | 跟随系统 / 开 / 关（Linux 简化为开/关） | 无 | 已完成 |
| B2-6 | 搜索键盘操作 | 上下键选择、Enter 启动、Esc 清空 | B1-5 | 已完成 |

### B3 设置与 UI

| ID | 任务 | 说明与验收标准 | 依赖 | 状态 |
|---|---|---|---|---|
| B3-1 | 设置 8 分类重构 | 外观/布局/分组/透明/交互/启动/性能/关于，对齐 Windows 结构 | 无 | 已完成 |
| B3-2 | 透明度双模式 | 分层/整窗 + 百分比输入 + 恢复 85% | B3-1 | 已完成 |
| B3-3 | 设计 token 体系 | Neutral 色阶 + Accent(#5B67D6) + 间距/圆角/字号，替换动态画刷 | 无 | 后置（非阻断） |
| B3-4 | 主题自定义命名 | 自定义主题方案名称字段 | B3-1 | 已完成 |
| B3-5 | 关于页 | 版本/开源/更新说明 | B3-1 | 已完成 |
| B3-6 | 性能页 | 清空图标缓存、配置位置显示 | B3-1 | 已完成 |

### B4 系统集成

| ID | 任务 | 说明与验收标准 | 依赖 | 状态 |
|---|---|---|---|---|
| B4-1 | 右键菜单 | 项目右键：打开/编辑/删除/移动（对齐 Windows Shell 菜单，按 Linux 能力裁剪） | 无 | 已完成 |
| B4-2 | 发布说明文档化 | 更新机制取舍落地：发布说明 + 手动解压替换 | 无 | 已完成 |
| B4-3 | import-manifest | 按取舍：B1 批量选择先行，契约后置（无外部工具对接则不排） | B1-1 | 不排（基线 §5 决策） |

### B5 性能与工程

| ID | 任务 | 说明与验收标准 | 依赖 | 状态 |
|---|---|---|---|---|
| B5-1 | 图标缓存上限 | LRU 256 项，超限淘汰 | 无 | 已完成 |
| B5-2 | 项目区虚拟化 | 大数据量滚动性能（参考 Windows VirtualizingWrapPanel 思路） | 无 | 受限评估后不实施（见 §5 记录） |
| B5-3 | 内存优化 | 内存 ≤450MB（D5 收口） | B5-2 | 部分完成（LRU 有界化，实机验证后置） |
| B5-4 | 配置迁移 | 配置版本号 + 受控迁移 + 换位置 | 无 | 已完成 |
| B5-5 | .deb 安装包 | dpkg-deb 打包：`DEBIAN/control` + 应用目录 `/opt/lanflow` + 菜单项 `/usr/share/applications` + postinst 刷新桌面/图标缓存。验收：UOS 双击安装、应用菜单出现、图标正常、`dpkg -r lanflow` 卸载干净。数据目录 `~/.config/LanFlow` 不受卸载影响 | 无 | 已完成（2026-08-11 Windows 构建出包，最终定名 `lanflow_1.4.8_amd64.deb`（Version 1.4.8 = round11 产物），ar/control/data 结构验证通过；UOS 实机安装验证待做） |

## 3. 完成记录

| 批次 | 完成 commit | 测试包 | 实机结果 |
|---|---|---|---|
| B1 | 89dcd65 | `LanFlow-linux-x64-b1.tar.gz`（38.1MB） | 批量添加/目录导入/自动命名/排序/搜索通过；**拖放仍不可用**（D2 遗留，兜底按钮可用） |
| B1（探针复验） | 89dcd65（代码未变） | — | 2026-08-10 Headless 探针复验：批量添加/自动命名/去重/空分组自动建/排序持久化 **10 项全 PASS**；外部拖放为 X11 平台行为，实机待验证（round7 包已含收口代码） |
| B2 | 886e90a | —（随最终包） | VM 验证通过（round5-b2）：失焦隐藏/空状态/悬停切换/动画偏好 |
| B3 | 7e5b4a9 | —（随最终包） | VM 验证通过（round5-b3）：8 分类/透明双模式/主题命名/关于/性能页 |
| B4 | d7b7173 | —（随最终包） | VM 验证通过（round5-b4）：右键菜单 5 项 + RELEASE-NOTES |
| B5 | 690061f | —（随最终包） | VM 验证通过（round5-pass）：300 项目启动/内存 193MB/首启落盘/换位置 |
| 最终包 | 690061f | `LanFlow-linux-x64-round4.tar.gz`（39.9MB） | VM 全部 PASS；待 UOS 实机终验（拖放/渲染细节，见测试基线） |

## 4. 变更记录

- 2026-08-07：建单（基于基线 B1–B5，任务可执行化 + 验收标准 + 依赖）。
- 2026-08-07：B1 代码完成——批量添加（多选+目录导入）、自动命名（.desktop Name/文件名）、拖放收口（兜底按钮批量+去重）、分组/项目排序（编辑模式上下移+持久化）、搜索行为核对（已具备，未改动）。构建 0 错误，出包 b1，实机待验证。
- 2026-08-07：B2 代码完成——失焦隐藏（Deactivated+模态守卫）、空状态提示、分组悬停切换（含延迟）、动画偏好开关（存储+UI，动画效果后置）、搜索键盘（Enter/上下键/Esc）。构建 0 错误。
- 2026-08-07：B3 代码完成——设置 8 分类（外观/布局/分组/透明/交互/启动/性能/关于）、透明度双模式（分层/整窗 + 百分比 + 恢复 85%）、主题命名、关于页（版本/开源）、性能页（清空图标缓存/配置位置/打开目录）、图标缓存静态化共享清空。构建 0 错误。B3-3 设计 token 后置（非阻断）。
- 2026-08-07：B4 完成——项目右键菜单（打开/编辑/删除/上移/下移，code-behind 动态 ContextMenu，绕过 XAML 事件绑定限制）、发布说明文档化（docs/RELEASE-NOTES.md，手动解压替换）、B4-3 import-manifest 按基线 §5 决策不排。构建 0 错误。
- 2026-08-07：B5 完成——图标缓存 LRU 256 项上限（ShellIconService，线程安全）；配置版本号 + 受控迁移（v0→v1）+ 换位置（LANFLOW_CONFIG_DIR 环境变量）；B5-2 虚拟化受限评估不实施（见 §4 记录）；B5-3 部分完成（依托 LRU 有界化，内存实机验证后置）。构建 0 错误。
- 2026-08-07：最终打包——`LanFlow-linux-x64-round4.tar.gz`（39.9MB，B1–B5 全量，commit 690061f）；生成测试基线文件 `docs/LanFlow-Linux测试基线.md`；包内附 TEST-CARD-r4.md + RELEASE-NOTES.md。待 UOS 实机验证。
- 2026-08-10：B1 功能闭环探针复验（Headless）——批量添加（3 文件入库）、自动命名（.desktop Name=微信/WPS Office、脚本 my-tool.sh→my-tool）、重复拖入去重、无分组自动建组、上移/下移排序 + 重载持久化，**10 项全 PASS**。代码未改动（89dcd65 已含），外部拖放属 X11 平台行为，实机用 round7 包验证。
- 2026-08-10：B3-6 修复完成——ConfigStore 提取静态 `ResolveConfigDirectory()`（含 LANFLOW_CONFIG_DIR 覆盖），SettingsWindow 性能页复用该解析替代硬编码路径（7b809eb）。VM 验证：覆盖目录 config 落盘 PASS；设置窗口打开 PASS（根因：桌面遮挡窗口，清理后正常）；性能页路径显示以源码链路确认，UI OCR 留实机。构建 0 错误。
- 2026-08-11：B5-5 .deb 打包完成——Windows 上无 dpkg-deb，改用 GNU tar（`--owner=0 --group=0 --mode=755`）生成 control.tar.gz/data.tar.gz + bsdtar `--format=ar` 组合出包。ar 三成员、data.tar.gz（/opt/lanflow 4 文件 + desktop + 图标 + copyright，root/0 755）、control.tar.gz（control/md5sums/postinst）、debian-binary=2.0 结构验证全部通过。产物放 `release\`（发布包唯一存放位置规则）。UOS 实机安装验证待做。
- 2026-08-13：发布前校验——.deb 最终定名 `lanflow_1.4.8_amd64.deb`（Version 1.4.8 = round11 产物，与 `round11.tar.gz` 同二进制，SHA256 `78afe0dc…`）；构建 0 错误（31 警告均为已知过时 API 类）；RELEASE-NOTES 补 round11 段落、测试基线 §1 更新至 round11、ARCHITECTURE.md 重写为 Avalonia 版发布规范。仍待 UOS 实机：.deb 安装验证（TEST-CARD-deb 10 项）、拖放/渲染终验、热键复核。

## 5. B5-2 虚拟化受限评估记录

- **结论**：不实施（记录留档）。
- **依据**：Avalonia 无开箱 VirtualizingWrapPanel；ItemsRepeater（experimental）与现有拖放/编辑按钮/空状态叠加改造风险高；启动器个人场景项目量通常 < 数百，WrapPanel 全量渲染可接受。
- **替代**：图标 LRU 有界化（B5-1）已收敛主要内存增长点；若实机大数据量滚动出现卡顿，再评估 ItemsRepeater + UniformGridLayout。

## 6. B5-5 .deb 安装包方案（2026-08-10 规划）

> 目标：UOS/Deepin 用户可双击安装、进应用菜单、图标正常；与现有 tar.gz 包共存（tar.gz 仍为 U 盘离线首选）。

### 6.1 包结构

```
lanflow_<ver>_amd64.deb
├── DEBIAN/
│   ├── control          # 元数据
│   └── postinst         # 安装后刷新桌面/图标缓存
└── opt/
│   └── lanflow/         # 解压后的完整应用目录（自包含 .NET，同 round 包）
├── usr/share/applications/lanflow.desktop
└── usr/share/icons/hicolor/256x256/apps/lanflow.png
```

### 6.2 control 文件要点

```
Package: lanflow
Version: 0.1.0（随轮次递增，如 0.7.0 = round7）
Architecture: amd64
Maintainer: LanFlow
Section: utils
Description: LanFlow 轻量启动与整理工具（UOS/Deepin）
Depends: （留空——自包含运行时，无需外部依赖；若目标机无 libgtk 需评估）
```

### 6.3 postinst 要点

```sh
#!/bin/sh
set -e
update-desktop-database /usr/share/applications >/dev/null 2>&1 || true
gtk-update-icon-cache /usr/share/icons/hicolor >/dev/null 2>&1 || true
chmod +x /opt/lanflow/LanFlow /opt/lanflow/lanflow.sh
```

### 6.4 构建与验证

- **构建**：目标机/VM 上 `dpkg-deb -b <stage_dir> lanflow_<ver>_amd64.deb` 最稳妥；**Windows 实测可用** GNU tar（`--owner=0 --group=0 --mode=755`）产出 control.tar.gz/data.tar.gz + bsdtar `--format=ar` 组合（debian-binary=2.0），已出包验证结构（2026-08-11）。
- **安装验证**：`sudo apt install ./lanflow_<ver>_amd64.deb` → 菜单出现 LanFlow → 启动正常 → `[LanFlow]` 日志。
- **卸载**：`sudo dpkg -r lanflow` → /opt/lanflow 与菜单项删除；`~/.config/LanFlow` 数据保留（dpkg 不碰 home）。
- **升级**：版本号递增重装即可，配置自动保留。

### 6.5 风险

| 风险 | 影响 | 缓解 |
|---|---|---|
| postinst 在无 gtk 缓存工具的系统报错 | 安装中断 | `|| true` 容错已内置 |
| /opt 权限 | 普通用户不可写应用目录 | 应用数据在 `~/.config`，/opt 只读可接受 |
| 图标缺失 | 菜单无图标 | 用现有托盘图标生成的 256px PNG 兜底 |
