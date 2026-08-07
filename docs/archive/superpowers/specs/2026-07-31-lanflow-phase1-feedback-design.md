# LanFlow Windows 一期反馈分阶段原地重构设计

- 日期：2026-07-31
- 状态：用户已确认
- 目标分支：`windows`（跟踪 `dev/windows`）
- 技术路线：路线 B——分阶段原地重构
- 反馈来源：`docs/fankui/反馈0731-1.md`

## 1. 目标

在不更换 WPF 技术栈、不重写完整设置系统、不改变已确认主页结构的前提下，处理一期反馈中的八项问题，并保持应用主要在约 85% 透明度下运行时仍具有清晰、稳定且流畅的视觉表现。

本轮交付完成后应具备：

1. 布局模式只保留网格和卡片，旧列表配置安全迁移；
2. 网格与卡片切换后图标稳定刷新；
3. 设置页与主页共享视觉令牌，导航轮廓和百分比显示完整；
4. “列间距”具有准确、可预览的水平间距效果；
5. 设置窗口非模态、单实例，并优先与主页并排；
6. 配置文件位置可查看、打开、迁移和恢复默认；
7. 分组悬停切换延迟可在 0–500ms 之间配置；
8. 完成基础自动化验证并输出 Windows x64 Debug 包供手动测试。

## 2. 全局约束

- 保留 .NET 8、WPF 和现有项目划分。
- 不引入新的第三方 UI 框架或依赖。
- 不推送远端。
- 不批量修复与本轮无关的既有乱码或历史样式。
- 旧 JSON 字段 `itemSpacing` 保持不变，仅把用户界面文案改为“列间距”。
- 旧 `layoutMode = "list"` 必须归一化为 `grid`，不能导致启动失败。
- 设置窗口继续使用现有 `SettingsPreviewSession` 的预览、应用、取消语义。
- 配置位置迁移在重启后生效；运行中不热切换 `ConfigStore`。
- 原配置文件迁移成功后保留为备份，不立即删除。
- 新增配置字段必须有默认值、克隆逻辑和归一化边界。
- 修复和新增行为先以失败测试或合同测试锁定，再写实现。
- 最终只做基础开发验证和 Debug 打包，复杂场景由用户手动检测。

## 3. 分阶段边界

### 阶段 1：布局模式、列间距与图标刷新

收敛布局选择、兼容旧配置、修复卡片图标消失，并确保水平间距只由虚拟化面板的 `HorizontalSpacing` 控制。

### 阶段 2：设置页视觉和基础可用性

复用主页动态资源，修正导航区域宽度计算、选中轮廓、设置区块层级和透明度百分比输入宽度。

### 阶段 3：非模态设置窗口

把 `ShowDialog()` 攓为单实例 `Show()`；增加可测试的并排定位算法；保留现有未保存更改处理和预览事务。

### 阶段 4：配置位置与悬停延迟

增加稳定定位文件、配置迁移服务和设置页入口；增加 `GroupHoverDelayMs` 并允许实时更新分组切换协调器。

## 4. 布局模式设计

### 4.1 用户可见模式

设置页仅显示：

- 网格：`grid`
- 卡片：`card`

列表：`list` 不再作为可选项出现。

### 4.2 兼容旧配置

`ConfigStore` 和 `MainViewModel` 的设置复制/归一化逻辑都必须遵守：

```text
list -> grid
grid -> grid
card -> card
其他值 -> grid
```

`SettingsOptionValues.ListLayout` 在所有调用方清除后删除。兼容旧字符串使用 Core 内部私有常量，不再暴露为产品选项。

### 4.3 布局切换和图标刷新

`MainWindow.ApplyLayoutSettings` 在网格/卡片切换时：

1. 捕获选中项、焦点项和垂直偏移；
2. 取消旧的图标请求；
3. 更新 `ItemContainerStyle` 和 `ItemTemplate`；
4. 保持 `VirtualizingWrapItemsPanel`，不再切到列表面板；
5. 在 `DispatcherPriority.Loaded` 后重新获取 `VirtualizingWrapPanel`；
6. 恢复视图状态；
7. 重新计算可见范围并调用 `LoadVisibleIconsAsync()`。

必须确保旧布局异步任务不能覆盖新布局的图标状态；继续使用现有 `ViewportIconCoordinator` 的取消和 generation 机制。

## 5. 列间距设计

- UI 标题：“列间距”。
- UI 说明：“调整同一行中相邻项目之间的水平距离。”
- C# 属性继续使用 `Settings.ItemSpacing`。
- JSON 属性继续使用 `itemSpacing`。
- 取值范围继续为 0–64。
- `MainWindow.xaml` 中 `VirtualizingWrapPanel.HorizontalSpacing` 继续绑定 `ItemSpacing`。
- 卡片容器不得再叠加会改变相邻项目实际空隙的水平 Margin。
- `VirtualizingWrapLayout.CalculateColumns` 和 `GetItemRect` 必须验证同一间距值同时用于列数和坐标计算。

## 6. 设置页视觉设计

### 6.1 视觉原则

设置页不创建独立色板，统一使用：

- `WindowBackgroundBrush`
- `SurfaceBrush`
- `MutedSurfaceBrush`
- `WindowBorderBrush`
- `DividerBrush`
- `ItemHoverBrush`
- `ItemSelectedBrush`
- `FocusBorderBrush`
- `PrimaryTextBrush`
- `SecondaryTextBrush`

在 85% 透明度下，输入框、按钮、导航选中态和区块边界必须仍然清晰。

### 6.2 左侧导航

- 父列保留明确宽度；
- `ListBox` 使用 Stretch，不使用与父列相同的固定宽度；
- 移除负 Margin；
- 导航项右侧与分割线保留至少 6px；
- 键盘焦点采用内侧 1px/2px 视觉，不扩大控件实际边界。

### 6.3 右侧设置区块

- 页面边距统一为 20–24px；
- 区块间距 14–16px；
- 圆角沿用主页中等圆角；
- 区块背景使用 `SurfaceBrush` 或 `MutedSurfaceBrush`；
- 分割线使用 `DividerBrush`；
- 不用高对比纯白卡片堆叠。

### 6.4 透明度百分比

- 输入框使用 `MinWidth="72"`，不再使用 `Width="58"`；
- 数值右对齐；
- `%` 作为独立后缀；
- 0、85、100 在 100%、125%、150% DPI 下均应完整显示。

## 7. 非模态设置窗口设计

### 7.1 生命周期

`MainWindow` 保存 `_settingsWindow` 和当前 `SettingsPreviewSession`：

- 第一次打开：创建并 `Show()`；
- 再次打开：恢复窗口状态并 `Activate()`；
- 设置窗口关闭：完成现有关闭决策，然后清理引用；
- 主窗口关闭：关闭设置窗口并释放预览订阅；
- 任意时刻最多存在一个设置窗口。

### 7.2 并排定位

新增纯计算组件 `SettingsWindowPlacement`：

输入：

- 主窗口矩形；
- 设置窗口期望尺寸；
- 当前显示器工作区；
- 窗口间距，默认 12px。

输出：设置窗口左上角。

规则：

1. 右侧容纳完整窗口时放右侧；
2. 否则左侧容纳完整窗口时放左侧；
3. 否则把位置限制在工作区；
4. 始终保证标题栏位于工作区；
5. 使用实际 DPI 下的 WPF 设备无关单位。

### 7.3 设置事务

现有语义保持：

- 实时预览通过 `SettingsPreviewSession.PreviewRequested`；
- 应用：提交 Working，更新 MainViewModel 并保存；
- 放弃：恢复 Original；
- 继续编辑：取消关闭；
- 非模态改造不使用 `DialogResult` 作为完成信号。

## 8. 配置位置完整方案

### 8.1 稳定文件

默认配置目录：

```text
%APPDATA%\LanFlow
```

默认配置文件：

```text
%APPDATA%\LanFlow\config.json
```

稳定定位文件：

```text
%APPDATA%\LanFlow\config-location.json
```

定位文件格式：

```json
{
  "configDirectory": "D:\\LanFlowData"
}
```

使用默认目录时删除定位文件；定位文件只保存目录，不保存其他设置。

### 8.2 启动解析

新增 `ConfigLocationService`，职责仅包括：

- 计算默认目录和 locator 路径；
- 读取并验证 locator；
- 返回当前有效目录；
- locator 无效时回退默认目录并输出可记录的状态。

`MainWindow` 先解析目录，再构造 `ConfigStore("Alt+Space", resolvedDirectory)`。

### 8.3 迁移服务

新增 `ConfigMigrationService`，输入当前完整 `AppConfig`、当前目录和目标目录，输出明确结果：

```text
Success
NoChange
TargetContainsConfig
InvalidTarget
WriteFailed
ValidationFailed
```

迁移顺序：

1. 规范化路径；
2. 拒绝相同目录和无效目录；
3. 创建目标目录并验证写入；
4. 如目标已有 `config.json`，先要求 UI 确认；
5. 把当前完整配置写入 `config.json.tmp`；
6. 重新反序列化验证；
7. 原子替换目标 `config.json`；
8. 原子写入 locator；
9. 保留源配置；目标旧配置覆盖前保存时间戳备份；
10. 返回“重启后生效”。

任一步失败时不能更新 locator，当前运行继续使用原配置。

### 8.4 设置页入口

“性能与缓存”增加：

- 当前配置文件完整路径；
- “打开目录”；
- “更换位置”；
- “恢复默认位置”；
- 迁移状态文本。

路径文本可选择和复制，超长时裁切，ToolTip 显示完整值。

文件夹选择使用 Windows 自带选择器；打开目录使用资源管理器。迁移成功后不热切换，提示重启。

## 9. 分组悬停延迟设计

新增配置：

```csharp
[JsonPropertyName("groupHoverDelayMs")]
public int GroupHoverDelayMs { get; set; } = 100;
```

规则：

- 范围 0–500ms；
- 步进 10ms；
- 默认 100ms；
- 0ms 表示立即切换；
- 仅在悬停模式启用控件；
- 点击模式保留值但禁用控件；
- 设置预览实时应用；
- 取消设置恢复原值。

`GroupSwitchCoordinator` 把只读 `_intentDelay` 改为可更新 `_intentDelay`，新增：

```csharp
public void UpdateIntentDelay(TimeSpan delay)
```

调用时取消当前 hover 和 drag-hover 定时器，再使用新值。点击切换不受影响。

## 10. 测试设计

### Core

- `ConfigStoreTests`：list 迁移、非法布局回退、悬停延迟边界；
- `SettingsCloneTests`：克隆悬停延迟；
- 新增 `ConfigLocationServiceTests`：无 locator、有效 locator、损坏 locator；
- 新增 `ConfigMigrationServiceTests`：成功、同目录、已有文件、备份、失败不更新 locator。

### Desktop

- `SettingsWindowContractTests`：无 list、列间距文案、72px 百分比、配置路径入口；
- `VirtualizingWrapLayoutTests`：水平间距准确影响列数和矩形；
- `ViewportIconCoordinatorTests` 或合同测试：布局切换后取消旧请求并刷新；
- `SettingsWindowPlacementTests`：右侧、左侧、工作区限制；
- `MainWindowArchitectureContractTests`：单实例 `Show()`、不存在设置窗口 `ShowDialog()`；
- `GroupSwitchCoordinatorTests`：更新延迟、取消旧计时、0ms；
- `SettingsWindowViewModelTests`：悬停延迟预览和点击模式禁用状态；
- `ThemeResourceContractTests`：设置页只使用批准的语义资源。

## 11. 交付验证

依次运行：

```powershell
dotnet test native\LanFlow.Core.Tests\LanFlow.Core.Tests.csproj -c Release
dotnet test native\LanFlow.Desktop.Tests\LanFlow.Desktop.Tests.csproj -c Release
dotnet build native\LanFlow.Desktop\LanFlow.Desktop.csproj -c Debug
dotnet publish native\LanFlow.Desktop\LanFlow.Desktop.csproj -c Debug -r win-x64 --self-contained false
```

基础手测：

1. 启动应用并打开设置；
2. 主窗口和设置窗口可同时操作；
3. 网格/卡片反复切换图标不消失；
4. 列间距和透明度实时预览；
5. 85% 和 100% 百分比完整显示；
6. 悬停延迟 0、100、200、500ms；
7. 配置目录迁移成功、取消和失败路径；
8. 重启后从新目录加载配置；
9. 日志无未处理异常。

最终产物：

```text
artifacts\debug\<commit>\LanFlow-1.3.8-debug-<commit>-win-x64.zip
```

## 12. 非目标

- 不迁移图标缓存和日志目录；
- 不支持运行中热切换配置存储；
- 不恢复列表布局；
- 不新增右侧分组栏；
- 不重做主页信息架构；
- 不引入 WinUI、Avalonia 或第三方主题库；
- 不执行复杂性能基准和长时间稳定性测试；
- 不推送或创建远端 PR。

