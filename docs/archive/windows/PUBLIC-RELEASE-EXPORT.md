# 公开发布导出规则

## 分支与仓库职责

- `dev/windows`：Windows 的完整私有开发线，包含测试、开发文档和内部验证材料。
- `dev/linux`：Linux 的完整私有开发线。
- `origin/main`：公开发布线，只承载面向用户的 README、许可证和可发布的生产源码。

不得把 `windows` 或 `linux` 直接合并到公开 `main`。Git 合并会携带完整开发历史；即使之后删除文件，开发材料仍可能从公开历史中获取。

## 导出方式

在私有 `windows` 分支根目录运行：

```powershell
.\tools\Export-PublicRelease.ps1 -OutputPath ..\LanFlow-public-stage
```

导出目录已存在时，必须人工确认它是可删除的临时目录，才可使用：

```powershell
.\tools\Export-PublicRelease.ps1 -OutputPath ..\LanFlow-public-stage -Force
```

脚本仅导出以下内容：

- `.gitignore`、`LICENSE`、`README.md`
- `native/LanFlow.Core` 的生产项目、模型、服务和视图模型
- `native/LanFlow.Desktop` 的生产项目、应用入口、资源、发布配置、服务和视图

脚本会拒绝非白名单文件、常见本地配置文件、凭据特征和 `C:\Users\` 绝对路径痕迹。

## 发布前门禁

1. 在私有 `windows` 分支运行全部测试。
2. 执行导出脚本，确认输出目录仅包含白名单路径。
3. 从导出目录构建 Windows 项目。
4. 在独立的公开仓库工作树审阅差异，再创建公开 `main` 的发布快照和 GitHub Release 附件。
5. 发布后的修复必须先回写私有 `windows`；公开 `main` 不承接日常开发。

## 未决边界

当前公开仓库历史中已存在开发文档和测试相关内容。本规则只防止未来发布继续泄露；是否创建一个全新、历史也干净的公开仓库，需要单独决策后再执行。