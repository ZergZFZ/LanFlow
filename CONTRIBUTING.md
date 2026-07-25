# 贡献指南（CONTRIBUTING）

感谢参与 LanFlow 开发！本项目遵循 MIT 许可证，欢迎 Issue、PR 与文档改进。

## 开发流程

1. Fork 本仓库并克隆到本地。
2. 按 [`docs/DEV_GUIDE.md`](docs/DEV_GUIDE.md) 配置开发环境。
3. 从 `main` 切出功能分支：`git checkout -b feat/your-feature`。
4. 保持小步提交，提交信息遵循 [Conventional Commits](https://www.conventionalcommits.org/)（如 `feat:`, `fix:`, `docs:`）。
5. 确保 `npm run build` 与 `cargo check` 通过后再提交 PR。
6. 在 PR 中说明变更内容与关联的 PRD 需求编号（如 `F01` / `S02`）。

## 代码规范

- 前端：TypeScript strict 模式，组件以函数式 + Hooks 编写。
- 后端：Rust 2021 edition，`cargo fmt` / `cargo clippy` 无警告。
- 数据持久化优先使用本地 JSON / SQLite，不收集任何用户数据（见 PRD 非功能需求）。

## 行为准则

请友好、包容地交流，尊重所有贡献者。
