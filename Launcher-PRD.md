---
AIGC:
    Label: "1"
    ContentProducer: 001191440300708461136T1XGW3
    ProduceID: 5d9f48eb2c25843f27994bc1a31c7abc_a2d2896f87f311f1b66e525400e6dd8f
    ReservedCode1: 0I5v6TqqMcCie9oqwg429+lylNxp+XOwAYLJ3Vge4mkZAzdMvVwqexHcqGf6ya/RyfhqZg0f4L6PAM6Ly+k/SDhLHYvd86cU3VK33L1TyLcM9d/khXH+65bb5fEcHqpD8Zsm507hAzKSbnrJr+O68jxTbutdzMvnNnZwgLCEw8FhxiY5mabhB+mpj4Q=
    ContentPropagator: 001191440300708461136T1XGW3
    PropagateID: 5d9f48eb2c25843f27994bc1a31c7abc_a2d2896f87f311f1b66e525400e6dd8f
    ReservedCode2: 0I5v6TqqMcCie9oqwg429+lylNxp+XOwAYLJ3Vge4mkZAzdMvVwqexHcqGf6ya/RyfhqZg0f4L6PAM6Ly+k/SDhLHYvd86cU3VK33L1TyLcM9d/khXH+65bb5fEcHqpD8Zsm507hAzKSbnrJr+O68jxTbutdzMvnNnZwgLCEw8FhxiY5mabhB+mpj4Q=
---

# 项目需求文档：LanFlow（类 Rolan 启动器 + 键盘搜索引擎）

> 版本：v1.1 | 日期：2026-07-25 | 状态：草案

---

## 一、项目背景与目标

### 1.1 背景

Windows 用户的痛点：随着安装软件增多，桌面图标杂乱、开始菜单层级深、查找应用效率低下。市面方案各有利弊：

| 现有方案 | 优势 | 不足 |
|----------|------|------|
| **Rolan** | 分组面板、拖拽收纳、原生性能 | 收费 ¥49/年，免费版功能受限 |
| **Flow Launcher** | 开源免费、键盘搜索极快、插件生态活跃 | 纯搜索模式，无可视化分组面板；C# 技术栈 |
| **Hain** | 开源免费、纯 JS 插件、Node.js 生态 | Electron 内存高、项目已归档（2020年停更） |
| **WinLaunch** | macOS Launchpad 风格、免费 | 项目停更，仅图标网格 |
| **Deskora** | 自动分类桌面图标 | 侧重桌面整理，非启动器 |
| **Fences** | 桌面围栏分组成熟 | 付费 $9.99，仅桌面维度 |

### 1.2 目标

开发一款 **开源免费** 的 Windows 启动器，同时具备：

- **Rolan 式可视化分组面板**（图标拖拽、分类收纳）
- **Flow Launcher 式键盘搜索**（输入即搜、秒级启动）
- 轻量原生性能，无 Electron 臃肿

---

## 二、竞品功能矩阵

| 功能 | Rolan 5 | Flow Launcher | Hain | WinLaunch | Deskora |
|------|:---:|:---:|:---:|:---:|:---:|
| 分组面板（标签页） | ✅ | ❌ | ❌ | ✅（分页） | ❌ |
| 图标拖拽收纳 | ✅ | ❌ | ❌ | ✅ | ❌ |
| 快捷键键盘搜索 | ✅（基础） | ✅（核心） | ✅（核心） | ❌ | ❌ |
| 插件/扩展系统 | ❌ | ✅（多语言） | ✅（纯 JS） | ❌ | ❌ |
| 主题/皮肤 | ✅ | ✅ | ✅ | ✅ | ✅ |
| 待办/备忘 | ✅ | ❌ | ❌ | ❌ | ❌ |
| 系统命令（关机/重启） | ✅ | ✅ | ❌ | ❌ | ❌ |
| 网页搜索快捷指令 | ✅ | ✅ | ❌ | ❌ | ❌ |
| 文件/文件夹快捷方式 | ✅ | ✅ | ❌ | ❌ | ❌ |
| 桌面自动分类 | ❌ | ❌ | ❌ | ❌ | ✅ |
| 流式布局面板 | ✅ | ❌ | ❌ | ❌ | ❌ |
| 技术栈 | C# 原生 | C# + Python | Electron + Node.js | C# WPF | C++ |
| 内存占用 | ~30MB | ~60MB | ~150MB | ~40MB | ~50MB |
| 活跃维护 | ✅ | ✅ | ❌（2020归档） | ❌ | ✅ |
| 开源 | ❌ | ✅ | ✅ | ✅ | ❌ |
| 价格 | ¥49/年 | 免费 | 免费 | 免费 | 免费 |

---

## 三、核心功能需求

### 3.1 分组面板系统（Rolan 式）

| 需求编号 | 功能 | 优先级 | 说明 |
|----------|------|:---:|------|
| F01 | 创建/删除/重命名分组 | P0 | 左侧分组列表，右侧图标网格，支持无限分组 |
| F02 | 拖拽添加图标 | P0 | 从桌面/资源管理器/开始菜单拖入 .lnk / .exe / 文件夹 |
| F03 | 图标网格自动排列 | P0 | 支持图标大小调节、网格间距、对齐方式 |
| F04 | 分组排序与折叠 | P1 | 拖拽调整分组顺序，折叠不常用分组 |
| F05 | 流式布局模式 | P1 | 除网格外支持紧凑列表视图 |
| F06 | 图标右键菜单 | P1 | 编辑名称/路径/图标、删除、移动至其他分组 |
| F07 | 批量导入 | P2 | 一键扫描开始菜单/桌面，批量导入快捷方式 |
| F08 | 分组独立快捷键 | P2 | 为每个分组绑定独立快捷键直接打开 |

### 3.2 键盘搜索系统（Flow Launcher 式）

| 需求编号 | 功能 | 优先级 | 说明 |
|----------|------|:---:|------|
| S01 | 全局快捷键呼出搜索框 | P0 | 默认 Alt+Space，可自定义；搜索框居中浮动 |
| S02 | 应用搜索 | P0 | 索引开始菜单 + 用户自定义路径，模糊匹配，实时结果 |
| S03 | 文件搜索 | P0 | 集成 Everything SDK 或 Windows Search 索引 |
| S04 | 网页搜索指令 | P0 | `:g 关键词` → Google，`:b 关键词` → 百度，`:gh 关键词` → GitHub |
| S05 | 计算器 | P1 | 输入数学表达式直接计算并显示结果 |
| S06 | 系统命令 | P1 | `shutdown` / `restart` / `sleep` / `lock` 等 |
| S07 | 浏览器书签搜索 | P2 | 读取 Chrome/Edge/Firefox 书签并搜索 |
| S08 | 剪贴板历史 | P2 | 记录剪贴板历史，搜索后粘贴 |

### 3.3 核心引擎

| 需求编号 | 功能 | 优先级 | 说明 |
|----------|------|:---:|------|
| E01 | 全局快捷键管理 | P0 | 多快捷键、冲突检测、热键占用提示 |
| E02 | 窗口显示/隐藏动画 | P0 | 渐入渐出、跟随鼠标/屏幕边缘弹出 |
| E03 | 系统托盘 | P1 | 最小化到托盘、右键托盘菜单 |
| E04 | 开机自启 | P1 | 注册表 Run 键或启动目录 |
| E05 | 数据持久化 | P0 | 分组配置/图标列表存为本地 JSON 或 SQLite |

### 3.4 外观定制

| 需求编号 | 功能 | 优先级 | 说明 |
|----------|------|:---:|------|
| T01 | 颜色主题 | P1 | 预设明/暗主题 + 自定义色板 |
| T02 | 面板透明度 | P1 | 滑块调节，支持亚克力/云母模糊效果 |
| T03 | 图标尺寸调节 | P1 | 小/中/大/超大四档 |
| T04 | 字体自定义 | P2 | 字体族、大小、粗体 |

### 3.5 扩展能力

| 需求编号 | 功能 | 优先级 | 说明 |
|----------|------|:---:|------|
| X01 | 插件系统 | P1 | 参考 Flow Launcher 的 Python JSON-RPC 协议 |
| X02 | 内置插件市场 | P2 | 在线浏览/安装/更新插件 |
| X03 | 开放 API | P2 | 允许第三方通过 HTTP/WebSocket 调用启动器功能 |

---

## 四、技术架构建议

### 4.1 推荐方案：Tauri + React

| 维度 | 说明 |
|------|------|
| **前端** | React 18 + TypeScript，CSS Modules 或 Tailwind |
| **后端** | Rust（Tauri），处理系统调用、快捷键、文件索引 |
| **数据** | SQLite 存配置，JSON 存临时缓存 |
| **搜索** | 集成 Everything SDK（Rust FFI）或 tantivy 全文索引 |
| **打包** | Tauri bundler → .msi / .exe，安装包 < 10MB |

### 4.2 为什么不用 Electron

- Rolan 5 特意强调「无 Electron」，因其内存 150MB+ 饱受诟病
- Tauri 内存仅 ~30MB，安装包 ~5MB，更符合「轻量」定位

### 4.3 为什么不用 WPF / WinForms

- WPF 开发效率低于 Web 前端，UI 迭代慢
- 跨 Windows 版本兼容需 .NET Runtime 依赖
- 但**性能最优**，若 Rust + Native UI 组合也可考虑 egui/iced

---

## 五、Hain vs Flow Launcher 搜索引擎方案对比（二选一）

LanFlow 需要内置一个键盘搜索引擎。Hain 和 Flow Launcher 是 Windows 上最成熟的两款开源方案，**二选一作为搜索引擎内核**。

### 5.1 基本信息对比

| 维度 | Hain | Flow Launcher |
|------|------|---------------|
| GitHub | [hainproject/hain](https://github.com/hainproject/hain) | [Flow-Launcher/Flow.Launcher](https://github.com/Flow-Launcher/Flow.Launcher) |
| Stars | ~3.2k | ~30k+ |
| 技术栈 | Electron + Node.js v8 | C# (.NET) + Python 插件 |
| 协议 | MIT | MIT |
| 最后更新 | 2020 年（已归档） | 2026 年 6 月（活跃） |
| 安装包 | ~50MB（含 Chromium） | ~30MB |
| 内存占用 | ~150MB | ~60MB |
| 插件语言 | JavaScript（npm 分发） | C# / Python / JS / TS / F# |
| 插件分发 | npm registry | 内置插件商店 |
| 搜索引擎 | 内置模糊匹配 | Everything + Windows Search |

### 5.2 架构理念差异

| 维度 | Hain | Flow Launcher |
|------|------|---------------|
| 设计哲学 | 严格语法（如终端命令），非自然语言 | 自然语言模糊匹配 + 指令前缀 |
| 目标用户 | 极客/开发者 | 普通用户 + 开发者 |
| 搜索结果渲染 | 纯文本列表 | 富文本（图标 + 标题 + 副标题 + 操作按钮） |
| 插件模型 | `query → [results]` 同步返回 | `query → [results]` + JsonRPCAction 异步回调 |
| 扩展门槛 | 低（会 JS 就能写） | 中（需理解 JSON-RPC + 多语言适配） |

### 5.3 集成分析

#### 选择 Hain 作为内核

**优势**：
- JavaScript 插件生态与 Tauri 前端天然一致，可直接复用 npm 插件
- 插件开发门槛极低，社区贡献更容易
- 插件语法简洁（`module.exports = (query) => [...]`）

**劣势**：
- 项目已归档 6 年，需大量现代化改造（Node v8 → v22、Electron → Tauri）
- 插件市场依赖 npm registry，无专门的分发渠道
- 原版 Electron 内存高，移植到 Tauri 需重写大部分后端代码
- 生态远小于 Flow Launcher，可用插件少

**改造工作量**：约 **8~12 天**（升级依赖 + 移植 Electron 到 Tauri 后端 + 修复兼容性）

#### 选择 Flow Launcher 作为内核

**优势**：
- 活跃社区、30k+ Stars、持续维护
- 插件商店成熟，已有 100+ 可用插件
- Everything 集成开箱即用，搜索体验业界最佳
- 多语言插件支持，生态覆盖面广

**劣势**：
- 核心是 C#，与 Tauri（Rust）技术栈不一致，无法直接嵌入
- 插件协议需通过 JSON-RPC 桥接，增加一层通信开销
- 插件开发需要了解 C# 或 Python，门槛高于纯 JS

**集成方式**（推荐方案 2）：
1. **方案 1：进程间通信** — LanFlow 通过 stdin/stdout 或本地 HTTP 调用 Flow Launcher 作为后台搜索服务
2. **方案 2：协议兼容** — LanFlow 实现 Flow Launcher 的 JSON-RPC 插件协议，可直接安装 Flow 插件
3. **方案 3：Fork 嵌入** — Fork Flow Launcher 仓库，将其搜索核心提取为 Rust/Node 库

**改造工作量**：方案 2 约 **5~7 天**（实现 JSON-RPC 插件宿主 + Everything 集成），方案 1 约 2~3 天但体验较差

### 5.4 推荐选择

| 考量维度 | 权重 | Hain 得分 | Flow 得分 |
|----------|:---:|:---:|:---:|
| 搜索引擎成熟度 | 30% | 5 | 9 |
| 生态繁荣度 | 25% | 3 | 9 |
| 集成本项目便利性 | 20% | 6 (JS 同源) | 5 (需桥接) |
| 长期可维护性 | 15% | 2 (已归档) | 9 (活跃) |
| 性能与资源占用 | 10% | 4 (Electron) | 7 (C# 原生) |
| **加权总分** | — | **4.05** | **7.80** |

> **推荐选择 Flow Launcher**，走方案 2（协议兼容）：实现 JSON-RPC 插件宿主，可直接复用 Flow 的 100+ 插件生态，长期可持续性远优于已归档的 Hain。

### 5.5 备选：Hain 现代化改造路径

如团队强烈偏好 JS 全栈，可 Fork Hain 并执行以下改造：

1. 升级 Node.js v8 → v22，替换所有废弃 API
2. Electron → Tauri，用 Rust 重写系统调用层
3. 重构插件系统为 JSON-RPC 协议（与 Flow 兼容）
4. 集成 Everything SDK 替代原版模糊匹配
5. 建立独立插件分发渠道替代 npm registry

预估工作量：**15~20 天**（全职），风险高于直接兼容 Flow Launcher。

---

## 六、MVP 范围（v0.1）

| 模块 | 包含内容 | 预计工时 |
|------|----------|:---:|
| 分组面板 | 创建/删除分组、拖拽图标、网格排列 | 4 天 |
| 快捷键呼出 | 全局热键注册、窗口显隐动画 | 1.5 天 |
| 应用启动 | ShellExecute 启动 .exe/.lnk/文件夹/URL | 0.5 天 |
| 基础搜索 | 搜索已添加的应用名称（本地匹配） | 1 天 |
| 数据持久化 | JSON 配置文件读写 | 1 天 |
| 托盘 + 自启 | 系统托盘图标、开机自启 | 1 天 |
| 主题基础 | 明暗切换、透明度 | 1 天 |
| 测试与打包 | 集成测试、安装包制作 | 2 天 |
| **合计** | | **12 天（全职）** |

---

## 七、非功能需求

- **性能**：冷启动 < 500ms，搜索响应 < 100ms，内存 < 80MB
- **兼容性**：Windows 10 1809+ / Windows 11
- **安全**：不联网（除插件市场外），配置文件本地存储，不收集任何数据
- **国际化**：初始仅中文，架构预留 i18n

---

## 八、后续迭代路线

| 版本 | 主题 | 关键特性 |
|:---:|------|----------|
| v0.1 | MVP | 分组面板 + 拖拽收纳 + 基础搜索 + 主题 |
| v0.2 | 搜索引擎 | Flow Launcher JSON-RPC 协议兼容 + Everything 集成 + 网页搜索指令 + 计算器 |
| v0.3 | 外观升级 | 主题市场、亚克力效果、自定义字体 |
| v0.5 | 插件体系 | Flow 插件商店兼容、内置插件市场 |
| v1.0 | 成熟发布 | 自动更新、崩溃报告、多语言 |
*（内容由AI生成，仅供参考）*
