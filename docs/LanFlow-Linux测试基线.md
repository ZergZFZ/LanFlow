# LanFlow Linux 测试基线

> 状态：活跃 ｜ 最近更新：2026-08-10

> 本文件记录当前有效测试基线：包、commit、覆盖范围与验收点。每次出包更新此处；实机结果回填后在此登记。

## 1. 当前基线（round5）

| 项 | 值 |
|---|---|
| 测试包 | `publish/final-b5/`（自包含 linux-x64，含 B5-3 首启落盘 + 热键注册修复） |
| 源码 commit | 待提交（linux 分支；B5-3 首启落盘 + libX11 加载修复） |
| 运行时 | Avalonia 11.3.18 / .NET 8 自包含 linux-x64（glibc ≥ 2.28，X11） |
| 测试卡 | 包内 `TEST-CARD-r4.md`（B1–B5 全量回归） |
| 发布说明 | 包内 `RELEASE-NOTES.md` |
| 状态 | VM 验证全部 PASS；待 UOS 实机终验（拖放/渲染细节） |

## 2. 覆盖范围（B1–B5）

| 批次 | commit | 内容 | 实机结果 |
|---|---|---|---|
| B1 | 89dcd65 | 批量添加/自动命名/拖放收口/排序/搜索核对 | 批量添加通过（名称正确）；目录导入通过；自动命名通过；排序通过（UI 待统一优化）；搜索键盘通过；**拖放仍不可用**（D2 遗留，兜底按钮可用） |
| B2 | 886e90a | 失焦隐藏/空状态/悬停切换/动画偏好/搜索键盘 | VM 验证通过：空状态（空分组/搜索无结果）PASS；悬停切换 PASS；动画偏好 PASS（设置含开关）；失焦隐藏 PASS（修复"hide=true 启动即隐藏+热键唤回被吞"，见 round5-b2 记录） |
| B3 | 7e5b4a9 | 设置 8 分类/透明双模式/主题命名/关于/性能页 | VM 验证通过（round5-b3，2026-08-10）；B3-6 配置目录统一解析修复见 round5-b3.6（7b809eb） |
| B4 | d7b7173 | 右键菜单/发布说明文档化 | VM 验证通过（round5-b4，2026-08-10） |
| B5 | 690061f | 图标 LRU 256/配置版本迁移/换位置 | VM 验证通过（round5-pass，2026-08-10）：300 项目启动存活、内存 193MB ≤450MB、首启即落盘 version:1、LANFLOW_CONFIG_DIR 换位置生效 |

## 3. 验收点速览（详见包内 TEST-CARD-r4.md）

- B1：多选批量入库可启动、目录导入、自动命名、排序持久化、搜索键盘闭环。
- B2：失焦隐藏开关、空状态文案、悬停延迟切换、动画偏好。
- B3：8 分类设置页、透明双模式 + 恢复 85%、主题命名、关于/性能页。
- B4：项目右键菜单 5 项可用、RELEASE-NOTES 随包。
- B5：≥300 项目启动内存有界、内存 ≤450MB、config 含 `"version": 1`、`LANFLOW_CONFIG_DIR` 换位置（可选）。

## 4. 实机验证流程（UOS）

1. U 盘拷贝包 → 解压至目标目录。
2. `./lanflow.sh > /tmp/lanflow-run.log 2>&1 &`（首次可先 `bash collect-env.sh` 留存环境快照）。
3. 按 TEST-CARD-r4.md 逐项勾测，失败项截图 + 日志 + config.json 返回。
4. 结果回填至 §2「实机结果」列并更新缺陷板/基线。

## 5. 历史基线

- round3.11（缺陷板 D1–D13 结案轮，已作废，由 round4 取代）。

## 6. VM 自动化测试环境（2026-08-07 新增）

> 目标：替代 U 盘往返，VM 冒烟测试自动化。

| 项 | 值 |
|---|---|
| 虚拟化 | VMware Workstation 17.5.2（E:\VMware） |
| 客户机 | UOS 20 e-hwe 1070 amd64（镜像 `D:\uos-desktop-20-e-hwe-1070-amd64-202408.iso`） |
| VM 位置 | `E:\UOS DATE\UOS 20.vmx`（4 核 / 4GB / 100GB 磁盘 / NAT） |
| 登录凭据 | test/111、root/111（vmrun 远程登录用） |
| open-vm-tools | apt 安装；共享 `/mnt/hgfs/lanflow` ⇄ `publish\` |
| 一键测试 | `powershell -ExecutionPolicy Bypass -File tools\run-vm-test.ps1 -Round <轮次>` |
| 结果位置 | `publish\results\<轮次>\`（run.log / lanflow-lines.txt / forensics.txt / env.txt） |

### 可信边界

- **VM 可信**：启动/崩溃/日志取证/内存/glibc 兼容（VM 与实机同为 glibc 2.28）。
- **VM 可信（2026-08-10 起）**：热键注册——根因修复（libX11 加载）后 VM 实测 `Ctrl+Alt+L` 注册成功；后续实机仍需复核（键盘布局差异仍可能影响触发）。
- **VM 不可信**：拖放、渲染细节 → 实机终验保留。

### 已知约束

- 教育版与专业版同 1070 HWE 内核，仅授权/预装差异；实机终验仍用专业版。
- 旧专业版 ISO（`E:\uos-desktop-20-professional-hwe-1070-amd64-202408.iso`）引导区损坏已删除，勿复用。
- **libX11 库名坑（2026-08-10 定位）**：UOS/Debian 系仅安装 `libX11.so.6`（SONAME 带版本号），`[DllImport("libX11")]` 会 DllNotFoundException → 热键注册永远报"热键格式无效"（实机/VM 均复现）。已用 `NativeLibrary.SetDllImportResolver` 按 `libX11.so.6 → libX11.so → libX11` 兜底加载。

### VM 验证记录（round4-vm，2026-08-07，final-b5 包）

| 验收点 | 结果 | 说明 |
|---|---|---|
| 基础启动 + `[LanFlow]` 日志 | PASS | 进程存活、日志取证正常 |
| 取证 Bounds 输出 | PASS | `MainWindow Bounds=0,0,780,540` |
| B5-1 300 项目启动 | PASS | 预置 300 项 config 后启动存活 |
| B5-2 内存 ≤450MB | PASS | **300 项目实测 191MB**（阈值 450MB） |
| B5-3 config version=1 | 行为差异 | 首启**不自动落盘** config（`Load` 只读、`Save` 仅操作触发）；`version:1` 在首次落盘后生效 |
| B5-4 换位置 | 行为差异 | `LANFLOW_CONFIG_DIR` 代码已实现（ConfigStore 读环境变量）；落盘需一次 UI 操作触发 Save |
| B4-2 RELEASE-NOTES.md 随包 | PASS | 包内存在 |

> B5-3/B5-4 判定为**应用行为**而非缺陷：配置懒落盘设计。是否改为"首启即落盘"需产品决策。

### VM 验证记录（round5-pass，2026-08-10，修复后复验）

> 修复内容：① B5-3 首启即落盘（`MainViewModel` 构造后 `Save`）；② 热键注册根因修复（libX11 DllImportResolver）。

| 验收点 | 结果 | 说明 |
|---|---|---|
| 热键注册（根因修复） | PASS | 日志 `[LanFlow][hotkey] DoGrab keycodes=[46] mod=0xC` + `全局热键注册成功: Ctrl+Alt+L`；修复前：`热键格式无效：Ctrl+Alt+L`（根因见 §6 已知约束 libX11 库名坑） |
| B5-3 config version=1 | PASS | 首启即生成 `~/.config/LanFlow/config.json` 且 `version:1` |
| B5-4 换位置 | PASS | `LANFLOW_CONFIG_DIR=/tmp/lf` 首启即落盘生效 |
| B5-1 300 项目启动 | PASS | 存活 |
| B5-2 内存 ≤450MB | PASS | 300 项目实测 193MB |
| B4-2 RELEASE-NOTES.md 随包 | PASS | 包内存在 |

> round4-vm 的 B5-3/B5-4 行为差异已按产品要求改为**首启即落盘**，本轮复验转 PASS。

### VM 验证记录（round5-b2，2026-08-10，B2 交互体验）

> 验证方式：xdotool 交互 + captureScreen 截图 + tesseract OCR 取证。config 预置 3 分组（开发工具/空分组/系统工具）。

| 验收点 | 结果 | 说明 |
|---|---|---|
| B2-1 失焦隐藏 | PASS（修复后） | `hideOnDeactivate=true` 完整闭环：启动可见 → 切走隐藏 → 热键唤回。**两轮修复**：① 原实现 hide=true 时启动即隐藏（Show 后焦点未稳触发 Deactivated）+ 热键唤回被紧随失焦吞掉 → 加"启动 2s / 唤回 800ms 失焦抑制"；② 用户实机反馈"呼出后点桌面不隐藏、多次切焦点才触发" → 根因：Deactivated 依赖窗口曾激活，快捷键呼出在 Deepin 上可能未获焦（从未激活故不触发）→ 改为 **XGetInputFocus 主动轮询**（400ms，焦点 `_NET_WM_PID`≠本进程即隐藏）+ 唤回短暂 Topmost 抢焦点。VM 复测：呼出后点桌面即隐藏、点窗口内不误隐藏、切走即隐藏 |
| B2-2 空状态 | PASS | 空分组显示"该分组暂无项目 / 点击「添加文件」或进入编辑模式添加"；搜索无结果显示"未找到匹配的项目"（OCR 取证） |
| B2-3 悬停切换 | PASS | `groupSwitchMode=hover` + 延迟 300ms，悬停 tab 自动切换（用户实机确认） |
| B2-4 动画偏好 | PASS | 设置窗口可打开，"交互"分类含"启用动画/悬停切换/失焦自动隐藏"开关；AnimationMode config 读写正常 |

> 备注：用户手动初测"失焦隐藏失败"系 config 开关为 false（调测时改回），非功能失效；开关绑定与保存逻辑已确认正常。

### VM 验证记录（round5-b3，2026-08-10，B3 设置与 UI）

> 验证方式：xdotool 键盘导航（End/Up 切换分类）+ ffmpeg x11grab 截图 + tesseract OCR（裁剪区域）+ PIL 像素差异。config 预置 `layeredOpacity=0.85 / wholeWindowOpacity=0.7` 便于滑块对比。

| 验收点 | 结果 | 说明 |
|---|---|---|
| 8 分类切换 | PASS | 外观与主题/布局与项目/分组标签/透明度/交互/启动与快捷键/性能/关于 8 面板全部切换正确（各面板特征词 OCR 命中） |
| 透明度双模式+恢复85% | PASS | 切"整窗透明"后滑块 85%→70%（面板区域差异 634px）；"恢复 85%"后滑块回位（差异再变） |
| 主题命名 | PASS | "外观与主题"面板含主题配置名称字段 + 10 项自定义颜色（主文本等 OCR 命中） |
| 性能页 | PASS | 配置路径显示 `~/.config/LanFlow/config.json`；"清空缓存"按钮点击生效（状态文本出现，面板 diff 1362px） |
| 关于页 | PASS | 版本号 + 源码地址（ZergZFZ）OCR 命中 |

> 验证技术备注：① `xdotool search --name` 中文窗口名匹配不可用（getwindowname 正常），改按进程 pid + 窗口几何识别；② tesseract 对全屏图（含桌面/Dock）OCR 布局错乱导致坐标失真，须先裁剪单窗口或右侧面板区域；③ 分类切换用键盘 End/Up 导航规避坐标定位；④ 验证脚本 `publish/run-vm-b3.sh` + `b3-img.py` 可复用。

### VM 验证记录（round5-b3.6，2026-08-10，B3-6 配置目录统一解析）

> 触发：round5-b3 后复查发现 SettingsWindow 性能页硬编码 `~/.config/LanFlow`，与 ConfigStore 的 `LANFLOW_CONFIG_DIR` 覆盖不一致（B3-6 遗留）。
> 修复（7b809eb）：ConfigStore 提取静态 `ResolveConfigDirectory()`（环境变量覆盖默认目录）；SettingsWindow 性能页 `ConfigDir` 复用该方法。

| 验收点 | 结果 | 说明 |
|---|---|---|
| LANFLOW_CONFIG_DIR 落盘 | PASS | `LANFLOW_CONFIG_DIR=/tmp/lf-cfgdir-test` 启动：config 落盘到覆盖目录，默认目录未生成 |
| 设置窗口打开 | PASS | 清理遮挡窗口后右上角按钮点击成功（`[取证] SettingsWindow Bounds=0,0,760,600`） |
| 性能页路径显示覆盖值 | PASS（源码链路确认） | `ConfigPathText.Text = Path.Combine(ConfigDir, "config.json")`，`ConfigDir = ConfigStore.ResolveConfigDirectory()`；ResolveConfigDirectory 返回覆盖路径已实测 → 显示必然为覆盖路径。UI 渲染 OCR 取证留实机终验 |

> 技术备注：① **打开设置窗口前必须清理遮挡窗口**（复用 run-vm-b3.sh §4：windowkill 非保留窗口）——此前多轮"设置按钮点击无效"系桌面其他窗口遮挡右上角按钮，非应用问题；② ffmpeg x11grab 在 KWin 下抓不到设置窗口内容（截图全蓝，含已打开窗口时），统信截图 Ctrl+Alt+A 无法被 xdotool 触发（无新文件），本 VM 环境 OCR 截图取证通道不可用，路径显示验证以源码链路断言为准。

### VM 验证记录（round5-b4，2026-08-10，B4 右键菜单 + 发布说明）

> 验证方式：右键弹菜单（右键点 (390,240) 项目区）→ 键盘导航（Down xN + Return）→ config.json 顺序/数量断言 + run.log 启动日志 + 窗口列表。每项独立干净状态（杀进程 → 重写 config [App1-4] → 重启 → 操作）。
> 菜单项顺序（源码 MainWindow.axaml.cs `ShowItemContextMenu`）：打开/编辑/删除/分隔/上移/下移；键盘导航跳过分隔符，且首次 Down 才选中首项，故映射为 Down x1=打开 x2=编辑 x3=删除 x4=上移 x5=下移。

| 验收点 | 结果 | 说明 |
|---|---|---|
| 右键菜单弹出 | PASS | ctx-menu 窗口出现（KWin）；用户手动截图确认 5 项完整（`~/Pictures/Screenshots/截图_LanFlow_20260810110831.png` OCR：打开/编辑/删除/上移/下移） |
| 打开 | PASS | Down x1 + Return → run.log `[LanFlow] 启动: App2 -> /bin/true` |
| 编辑 | PASS | Down x2 + Return → 编辑窗口弹出（窗口列表 `[编辑项目]` WID=67108893） |
| 删除 | PASS | Down x3 + Return → config items 4→3（App2 移除） |
| 上移 | PASS | Down x4 + Return → config `['App2','App1','App3','App4']` |
| 下移 | PASS | Down x5 + Return → config `['App1','App3','App2','App4']` |
| B4-2 RELEASE-NOTES.md 随包 | PASS | 包内存在，含安装/更新步骤与已知限制 |

> 验证技术备注：① ffmpeg x11grab **抓不到** Avalonia ContextMenu popup（KWin 合成器独立绘制，窗口列表出现 `ctx-menu — KWin`），改以「操作结果断言」（config/日志/窗口列表）为主、统信系统截图 Ctrl+Alt+A 为辅；② 早期顺序执行失败（下移/删除/打开）根因是**上移后 ReloadItems 重排导致坐标漂移** + 旧进程未完全退出引起 config 竞争，改为每项独立干净状态后全部通过；③ 截图脚本 `publish/b4-shot-*.sh` 可复用（统信截图自动拖选 + Enter 保存）。
