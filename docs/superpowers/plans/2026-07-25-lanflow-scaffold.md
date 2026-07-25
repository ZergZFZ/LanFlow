# LanFlow 项目脚手架与开发环境 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 搭建 LanFlow（开源 Windows 启动器）可构建的 Tauri v2 + React 18 + TypeScript 工程脚手架，并完成本地开发环境配置（GitHub 仓库、本地文档、Rust 工具链）。

**Architecture:** 前端 React 18 + TypeScript 经 Vite 构建，由 Tauri v2（Rust）承载为原生窗口；Rust 侧负责系统调用（全局快捷键、文件索引、自启、托盘、插件宿主），数据以 JSON/SQLite 持久化。

**Tech Stack:** Tauri 2, Rust (edition 2021), React 18, TypeScript 5, Vite 5, SQLite（规划）。

## Global Constraints

- 平台：Windows 10 1809+ / Windows 11（PRD 七）
- 性能：冷启动 < 500ms，搜索 < 100ms，内存 < 80MB（PRD 七）
- 安全：默认不联网（插件市场除外），配置本地存储，不收集数据（PRD 七）
- 许可证：MIT（开源免费，PRD 1.2）
- 推荐技术栈：Tauri + React（PRD 4.1）
- 搜索引擎：兼容 Flow Launcher JSON-RPC 协议（PRD 5.4 方案 2）

---

### Task 1: 远程仓库与本地文档骨架

**Files:**
- Create: `README.md`, `LICENSE`, `CONTRIBUTING.md`, `docs/ARCHITECTURE.md`, `docs/DEV_GUIDE.md`, `.gitignore`

**Interfaces:** 无（纯文档/仓库层）

- [x] **Step 1: 创建 GitHub 仓库**
Run: `gh repo create LanFlow --public --description "LanFlow - 开源免费的 Windows 启动器"`
Expected: 仓库 https://github.com/ZergZFZ/LanFlow 创建成功

- [x] **Step 2: 编写本地文档**
- [x] 写入 `README.md`（项目介绍、功能规划、技术架构、快速开始、路线图）
- [x] 写入 `LICENSE`（MIT, © 2026 ZergZFZ）
- [x] 写入 `docs/ARCHITECTURE.md`（分层架构、PRD 映射、数据模型、搜索引擎选型）
- [x] 写入 `docs/DEV_GUIDE.md`（环境前置、初始化步骤、故障排查）
- [x] 写入 `.gitignore`（Rust/Node/Tauri）

---

### Task 2: Tauri + React 前端脚手架

**Files:**
- Create: `package.json`, `tsconfig.json`, `tsconfig.node.json`, `vite.config.ts`, `index.html`
- Create: `src/main.tsx`, `src/App.tsx`, `src/styles.css`, `src/vite-env.d.ts`

**Interfaces:**
- Produces: `npm run dev` / `npm run build` / `npm run tauri` 脚本入口

- [x] **Step 1: 编写前端配置**
```json
// package.json 依赖：react 18, @tauri-apps/api 2, vite 5, typescript 5
```
- [x] **Step 2: 编写 MVP 占位 UI（App.tsx）**
展示分组面板（左标签 + 右图标网格）与顶部搜索框，体现 PRD MVP 布局。

---

### Task 3: Rust 后端脚手架

**Files:**
- Create: `src-tauri/Cargo.toml`, `src-tauri/build.rs`, `src-tauri/src/main.rs`, `src-tauri/src/lib.rs`, `src-tauri/tauri.conf.json`

**Interfaces:**
- Produces: `cargo run` / `cargo tauri` 启动原生窗口

- [x] **Step 1: 编写 Cargo.toml**
```toml
[dependencies]
tauri = { version = "2", features = [] }
serde = { version = "1", features = ["derive"] }
serde_json = "1"
tauri-plugin-shell = "2"
```
- [x] **Step 2: 编写应用入口与配置**
- [x] `lib.rs`：`tauri::Builder::default().plugin(tauri_plugin_shell::init()).run(...)` 启动窗口
- [x] `tauri.conf.json`：窗口 900×600、bundle 目标 msi/nsis、图标清单

---

### Task 4: 应用图标生成

**Files:**
- Create: `src-tauri/icons/icon-source.png`（源图）
- Generate: `src-tauri/icons/*.png|*.ico|*.icns`（由 `cargo tauri icon` 生成）

**Interfaces:**
- Consumes: 源图 `icon-source.png`
- Produces: `tauri.conf.json` 中引用的图标文件

- [ ] **Step 1: 准备 1024×1024 源图**
保存至 `src-tauri/icons/icon-source.png`

- [ ] **Step 2: 生成多平台图标**
Run: `cd src-tauri && cargo tauri icon && cd ..`
Expected: 生成 `32x32.png / 128x128.png / 128x128@2x.png / icon.ico / icon.icns`

---

### Task 5: 安装 Rust 工具链并校验

**Files:** 无新增（系统环境）

**Interfaces:**
- Consumes: 已存在的 `src-tauri/*` 脚手架

- [ ] **Step 1: 安装 Rust**
Run: `winget install Rustlang.Rustup`（或访问 https://rustup.rs）
Expected: `cargo --version` 与 `rustc --version` 可用

- [ ] **Step 2: 安装前端依赖**
Run: `npm install`
Expected: `node_modules/` 生成

- [ ] **Step 3: 校验后端编译**
Run: `cd src-tauri && cargo check`
Expected: 编译通过（若报 `link.exe not found` → 安装 MSVC Build Tools）

- [ ] **Step 4: 提交并推送**
Run:
```bash
git add -A
git commit -m "chore: scaffold Tauri + React + TS project for LanFlow"
git push -u origin main
```

---

## Self-Review

1. **Spec coverage:** PRD 1.2（开源免费 Tauri+React）→ Task 2/3；PRD 4.1 技术栈 → 全部脚手架；PRD 七非功能约束 → 写入文档约束；搜索引擎选型 → ARCHITECTURE.md 记录。MVP 功能（F01–F08/S01–S08）为后续开发任务，本计划仅完成脚手架。
2. **Placeholder scan:** 任务步骤均含具体命令/代码，无 TBD。
3. **Type consistency:** 文件名、脚本名前后一致（`npm run tauri`、`cargo tauri icon`）。
