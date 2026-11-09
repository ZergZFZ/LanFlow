# FlickD 文档索引（windows 分支 / 主工作区）

> 查看项目情况：先读本索引 + [ARCHITECTURE.md](ARCHITECTURE.md) + [DEVELOPMENT-ROADMAP.md](DEVELOPMENT-ROADMAP.md) + [CHANGELOG.md](../CHANGELOG.md)。
> 当前产品基线为 **v2.0.0（FlickD）**；根目录 `Launcher-PRD.md` 是 v1.3.7 的历史产品基线，仅供需求准入参考，不作为当前功能验收依据。
> 归档文档不参与日常阅读。Linux 主线请切到 `linux` 分支查看其 `docs/README.md`（或用 `git show linux:docs/README.md`），不要依赖本地 `.build/linux-wt` 等临时工作树。

| 文档 | 状态 | 说明 |
|---|---|---|
| [../CHANGELOG.md](../CHANGELOG.md) | **活跃** | 版本变更日志（完整版本历史，当前最新 2.0.0；`[Unreleased]` 为待发布内容） |
| [ARCHITECTURE.md](ARCHITECTURE.md) | **活跃** | 架构分层、配置规则与 full / lite / ocr 发布更新规范 |
| [DEVELOPMENT-ROADMAP.md](DEVELOPMENT-ROADMAP.md) | **活跃** | 产品发展路线 R1–R4 与发布闸门 |
| [import-manifest.example.json](import-manifest.example.json) | **活跃** | 导入清单契约样例 |
| [import-manifest.schema.json](import-manifest.schema.json) | **活跃** | 导入清单契约 schema（schemaVersion 1.0） |
| [performance/R4-baseline.md](performance/R4-baseline.md) | **参考** | v1.5.2 实测性能基线与告警阈值；UI 终验报告仍为 BLOCKED |
| [archive/windows/](archive/windows/) | 归档 | Windows 历史记录（Settings-UI-Beautify / Win11-Shadow-Fix / PUBLIC-RELEASE-EXPORT / fankui 反馈） |
| [archive/plans/](archive/plans/) | 归档 | 计划文档（Import-Manifest / P2 工作区体验） |
| [archive/performance/](archive/performance/) | 归档 | Windows UI 性能基线/报告/回归清单 |
| [archive/superpowers/](archive/superpowers/) | 归档 | 过程性计划与设计（7-30/7-31 迭代） |

> 归档目录与 PRD 属于过程文档，按 `AGENTS.md` 红线不得再进入 `main`。
