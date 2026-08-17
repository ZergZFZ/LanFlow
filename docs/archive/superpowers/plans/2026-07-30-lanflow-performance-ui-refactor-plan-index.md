# LanFlow Windows 性能与 UI 分阶段原地重构总计划 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在保留 WPF、现有业务流程和全部显示模式的前提下，分四个可验证阶段消除分组切换首帧大图标闪烁与卡顿，加入虚拟化、异步图标、双透明度、统一设计系统和事务式设置页。

**Architecture:** 先稳定配置模型、可见集合和图标管线，再替换为可回收的虚拟化布局及单实例分组导航；随后引入共享设计令牌、双透明度和低成本动效；最后重构设置页并完成性能与回归验收。各阶段只做有边界的职责抽取，不进行全量 MVVM 重写，且每一阶段结束时都必须得到可运行、可测试的软件。

**Tech Stack:** .NET 8、C# 12、WPF、XAML、xUnit、Windows Shell API、ETW/Stopwatch 性能标记。

## Global Constraints

- 保留 WPF 和现有应用模型；不迁移到 WinUI、Avalonia 或其他 UI 框架。
- 不进行与目标无关的完整 MVVM 重写，只抽取性能关键职责。
- 目标规模：单组 30–100 项、全部 100–500 项；验收基准使用 Release 构建、500 个总项目、当前组 100 项。
- 保留网格、列表、卡片三种模式以及顶部、左侧两种分组位置。
- 任一时刻只渲染一套分组导航；禁止新增或短暂显示右侧分组栏。
- 点击切组为默认；悬停切组可选，意图延迟固定为 200 ms，只提交最后一个有效目标。
- 图标必须异步加载；禁止分组切换时同步加载全部分组图标、同步调用 `UpdateLayout()` 或递归遍历全部项目视觉树。
- 网格和卡片必须使用容器回收虚拟化；列表必须保持虚拟化。
- 图标缓存上限为 256 项，按稳定标识、像素尺寸、版本戳和主题变体区分；跨线程 `ImageSource` 必须冻结。
- 透明度范围 40%–100%，默认 85%；分层透明与整窗透明分别记忆数值。
- 旧用户缺失透明模式时迁移为整窗透明并保留原 `Opacity`；新安装默认分层透明 85%。
- 缓存命中时的内容过渡仅 80–120 ms，不允许图标缩放；动画默认跟随 Windows 减少动画偏好。
- 不使用动态全窗模糊；40% 透明度时文字、焦点、选中、警告和拖拽反馈仍须可辨识。
- 新增视觉代码必须使用共享语义令牌，不继续扩散同义硬编码颜色、圆角和间距。
- 设置页采用左侧分类、右侧滚动内容、底部固定操作区；预览不持久化，应用保存，取消恢复，未保存关闭需警告。
- 所有代码任务执行 TDD：先失败测试，再最小实现，再全量验证；每个任务独立提交。
- 性能目标：导航选中反馈 P95 约 50 ms，热缓存内容稳定 P95 约 100 ms，滚动接近 60 FPS并记录帧时间分布。

---

## 固定跨计划接口

以下命名在四个计划间视为冻结；除非测试证明不可行，不得在后续计划中另起同义类型：

```csharp
namespace LanFlow.Desktop.Models;

public static class SettingsOptionValues
{
    public const string GridLayout = "grid";
    public const string ListLayout = "list";
    public const string CardLayout = "card";
    public const string GroupTop = "top";
    public const string GroupLeft = "left";
    public const string GroupSwitchClick = "click";
    public const string GroupSwitchHover = "hover";
    public const string TransparencyLayered = "layered";
    public const string TransparencyWholeWindow = "wholeWindow";
    public const string AnimationSystem = "system";
    public const string AnimationOn = "on";
    public const string AnimationOff = "off";
}
```

```csharp
namespace LanFlow.Desktop.Services;

public enum IconLoadPriority
{
    Viewport = 0,
    Buffer = 1,
    Idle = 2
}

public interface IIconService : IAsyncDisposable
{
    ValueTask<ImageSource?> GetIconAsync(
        string? path,
        int pixelSize,
        IconLoadPriority priority,
        CancellationToken cancellationToken);

    void Invalidate(string? path);
    void Clear();
}
```

```csharp
namespace LanFlow.Desktop.Presentation;

public sealed class SettingsPreviewSession
{
    public Settings Original { get; }
    public Settings Working { get; }
    public bool HasChanges { get; }

    public event EventHandler<Settings>? PreviewRequested;

    public void Update(Action<Settings> mutation);
    public Settings Commit();
    public Settings Cancel();
}
```

## 阶段顺序与完成门

| 顺序 | 计划 | 依赖 | 可独立验收的完成门 |
|---|---|---|---|
| 1 | [渲染与数据基础](2026-07-30-lanflow-01-rendering-data-foundation.md) | 无 | 配置迁移正确；`VisibleItems` 身份稳定；首帧尺寸由 XAML 决定；异步图标服务通过并发、缓存、失效和过期写回测试。 |
| 2 | [导航与虚拟化](2026-07-30-lanflow-02-navigation-virtualization.md) | 计划 1 | 网格/卡片只生成视口和缓冲区容器；顶部/左侧共用单一导航数据；点击/悬停/拖拽切组正确。 |
| 3 | [设计系统与透明度](2026-07-30-lanflow-03-design-transparency.md) | 计划 1、2 | 主窗口使用语义令牌；两种透明模式在 40/85/100% 可读；动画遵循系统偏好且不影响布局。 |
| 4 | [设置页与最终验证](2026-07-30-lanflow-04-settings-validation.md) | 计划 1–3 | 设置事务、分类页面和未保存关闭行为完整；全部原设置可达；回归矩阵与性能报告可复现。 |

## 文件责任边界

- `LanFlow.Core`：持久化模型、配置规范化、稳定可见集合和与 UI 无关的状态更新。
- `LanFlow.Desktop/Services`：Shell 图标提取、异步调度、缓存、Windows 动画偏好和窗口材质策略。
- `LanFlow.Desktop/Controls`：虚拟化布局、单实例分组导航及纯 UI 控件行为。
- `LanFlow.Desktop/Presentation`：设置预览事务、窗口协调器和可测试的展示状态。
- `LanFlow.Desktop/Themes`：基础、语义和组件级资源字典。
- `LanFlow.Desktop.Tests`：不依赖真实桌面的调度、布局数学、状态机和事务测试。
- `docs/performance`：基准环境、测量命令、原始数据摘要、P50/P95/P99 和剩余瓶颈。

## 统一验证命令

每个计划末尾至少运行：

```powershell
dotnet test native\LanFlow.Core.Tests\LanFlow.Core.Tests.csproj -c Release
dotnet test native\LanFlow.Desktop.Tests\LanFlow.Desktop.Tests.csproj -c Release
dotnet build native\LanFlow.Desktop\LanFlow.Desktop.csproj -c Release
```

预期：两个测试项目均显示 `Failed: 0`，桌面项目输出 `Build succeeded.` 且警告数不因本阶段新增代码而上升。

## 执行纪律

- [ ] 按 1→2→3→4 的顺序执行，不跨过阶段完成门。
- [ ] 每个任务完成后检查 `git diff --check` 并提交，不将多个独立任务压成一个提交。
- [ ] 每个阶段完成后记录一次 Release 构建和人工烟雾测试结果。
- [ ] 若虚拟化破坏拖放、键盘导航或搜索索引映射，阶段 2 不得标记完成。
- [ ] 若旧用户透明度迁移或取消预览行为不正确，阶段 3/4 不得标记完成。
- [ ] 最终报告不得只写“感觉更流畅”，必须给出硬件、分辨率、缩放、透明模式、缓存冷热和帧时间分布。
