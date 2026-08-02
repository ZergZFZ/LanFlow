# LanFlow

**轻一点，快一点。**

LanFlow 是一款面向 Windows 的轻量启动器。把常用应用、文件夹和快捷方式放进清晰的分组里，需要时用全局快捷键呼出面板，少找一点，多做一点。

## 它能做什么

- 用分组整理常用应用、文件、文件夹和快捷方式；
- 从资源管理器直接拖入项目；
- 拖动调整顺序，或按使用频率排序；
- 在已整理项目中快速筛选，支持中文拼音首字母搜索（输入 `wx` 可找到「微信」）；
- 自定义主题、透明度、图标和卡片大小；
- 常驻系统托盘，支持开机自启和可配置全局快捷键；
- 使用 GitHub Release 检查并安装更新。

## 开始使用

1. 下载并运行 LanFlow；
2. 新建一个分组；
3. 将常用目标从资源管理器、开始菜单或 Everything 等外部工具拖进面板，也可以通过“添加项目”加入；
4. 在设置中按自己的习惯调整快捷键和外观；
5. 以后用快捷键打开 LanFlow，快速启动所需内容。

## 选择下载版本

| 版本 | 适合谁 | 使用方式 |
|---|---|---|
| **full** | 希望免配置、直接使用 | 解压 `LanFlow-{version}-full.zip` 后运行 `LanFlow.exe`，无需预装 .NET |
| **lite** | 已安装 .NET 8 Windows Desktop Runtime，想要更小下载 | 直接运行 `LanFlow-{version}-lite.exe` |

每个正式版本都会同时提供 full 和 lite 两种发布文件；应用内更新会自动匹配当前使用的版本类型。

## 本地优先

LanFlow 的分组、项目和外观设置保存在本机：

```text
%AppData%\LanFlow\config.json
```

不需要登录账号，也不要求把个人使用数据上传到云端。

## AI 批量导入

LanFlow 支持通过一份 JSON 清单批量导入分组和项目。这份清单可以由
AI 助手或外部工具生成，然后在应用内预览、勾选并一次性合并。

### 告诉 AI 怎么生成

把下面这段提示词直接交给 AI 助手即可：

```text
请读取仓库中的 docs/import-manifest.schema.json，按 schemaVersion 1.0
生成一份 LanFlow 导入清单（import-manifest.json）。要求：
1. 只输出 JSON 文件本身，不要解释或额外说明；
2. 每个分组至少包含一个项目；
3. 每个项目必须包含 name 和 path 两个字段，path 必须是 Windows
   完整路径（例如 C:\Program Files\App\App.exe）；
4. 不要包含 id、settings、useCount 等内部字段，也不要修改
   LanFlow 的 config.json。
```

### 最小可用示例

```json
{
  "schemaVersion": "1.0",
  "groups": [
    {
      "name": "开发工具",
      "items": [
        {
          "name": "Visual Studio Code",
          "path": "C:\\Users\\you\\AppData\\Local\\Programs\\Microsoft VS Code\\Code.exe"
        },
        {
          "name": "Windows Terminal",
          "path": "C:\\Program Files\\WindowsApps\\Microsoft.WindowsTerminal_8wekyb3d8bbwe\\WindowsTerminal.exe"
        }
      ]
    }
  ]
}
```

### 怎么导入

1. 把生成的 JSON 保存为 `import-manifest.json`；
2. 打开 LanFlow，点击底部工具栏的「导入清单」按钮；
3. 选择文件后在预览窗口中核对分组和项目，可逐项勾选；
4. 点击「确认导入」，选中的项目会一次性合并进现有配置。

导入只读取你明确选择的路径，不会扫描整台电脑，也不会覆盖已有分组和设置。

## 许可

LanFlow 使用 [MIT License](LICENSE) 开源发布。
