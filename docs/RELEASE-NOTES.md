# LanFlow Linux 发布说明

> 状态：活跃 ｜ 最近更新：2026-08-07

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
