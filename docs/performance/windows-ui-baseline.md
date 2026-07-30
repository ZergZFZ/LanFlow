# Windows UI 性能基线与采样规范

## 1. 目的与适用范围

本文件定义 LanFlow Windows 桌面端 UI 性能验证的固定数据集、采样口径、环境记录和验收门槛。它是复现实验的操作规范，不代表已经取得真实性能结果。

目标运行环境：

- Windows 11，系统“性能模式”场景需单独记录。
- Release 构建。
- 500 个总项目、当前组 100 个项目。
- 网格、列表、卡片三种显示模式。
- 顶部和左侧分组导航；主界面始终只显示一套分组导航。
- 分层透明与整窗透明两种模式，重点覆盖 85%，并覆盖 40% 和 100%。
- 冷缓存与热缓存分别采样。

## 2. 固定数据集

使用应用现有导入能力导入一份可删除的本地测试清单：

1. 建立 10 个分组，每组至少 50 个项目，总数至少 500。
2. 指定一个当前组，保证该组有 100 个项目；如需保持总数恰好 500，可从其他组等量调减。
3. 图标路径按近似相同比例混合：
   - 存在且可读取的本地文件；
   - 不存在或扩展名异常的路径；
   - 多个项目复用同一路径，用于验证缓存命中。
4. 名称同时包含短名称、长名称、中文、英文和需要 Tooltip 的截断文本。
5. 每轮测试前记录数据集版本或校验值；不得在冷/热缓存对比中途改变数据。

测试数据必须与用户真实清单隔离，验证结束后可完整删除。

## 3. 环境记录

每次采样必须记录：

- 测试日期和待测提交 SHA；
- Windows 版本、edition、build 和体系结构；
- CPU、GPU 和显示驱动版本；
- 主显示器分辨率、刷新率和 Windows 缩放；
- .NET SDK/runtime 版本；
- 主题、桌面背景类型和高对比模式；
- 显示模式、分组导航位置和触发方式；
- 透明模式与精确透明度；
- 动画模式以及 Windows 系统动画开关；
- 缓存状态和数据集规模。

虚拟显示器、远程桌面或串流软件会改变合成与帧时间，必须在报告中显式标注，不能与本地物理显示器结果混为一组。

## 4. 采样定义

### 4.1 缓存状态

- `cold`：应用启动后首次进入目标组，或图标缓存已按约定清空后的首次访问。
- `warm`：不修改数据和图标文件的前提下，再次进入已经访问过的目标组。
- 文件更新失效验证单独记录，不得归入普通 warm 样本。

### 4.2 切组标记

`UiPerformanceTrace` 使用以下标记：

- `selection-ack`：分组选择已被界面逻辑接收。
- `content-stable`：目标组视图状态恢复并得到当前已实现容器数量。

每次切组必须先调用 `GroupSwitchStarted`。同一组合至少记录 30 次有效切组；缺少起始标记、环境字段或缓存状态的样本作废。

### 4.3 帧间隔

滚动测试至少持续 20 秒。以连续渲染时间戳的差值作为帧间隔，分别汇总 P50、P95、P99 和最大值，并记录：

- 总帧数；
- 大于 16.67 ms、33.33 ms、50 ms 和 100 ms 的帧数与比例；
- 测试期间的显示器刷新率；
- 是否存在输入、拖放或窗口缩放等额外负载。

合成单元测试时间、测试运行器耗时和 `dotnet build` 时间不能替代帧间隔数据。

### 4.4 百分位

`PerformanceSampleCollector.Summarize` 对升序样本使用 nearest-rank：

```text
index = ceil(percentile * count) - 1
```

输出 P50、P95、P99 和最大值。空样本或非有限数值必须拒绝，不能生成误导性百分位。

## 5. CSV 格式

固定表头：

```text
os,cpu,gpu,resolution,scale,marker,elapsedMs,cacheState,transparencyMode,realizedContainers
```

要求：

- 数字使用 invariant culture；
- 文本按 RFC 4180 处理逗号、双引号和换行；
- 每个样本携带完整环境字段；
- `realizedContainers` 直接读取虚拟化面板公开状态或当前容器计数；
- 不为采样主动调用 `UpdateLayout`；
- 不为了统计容器而在采样路径递归遍历整个视觉树。

## 6. Release 基准流程

先运行自动化验证：

```powershell
dotnet test native\LanFlow.Core.Tests\LanFlow.Core.Tests.csproj -c Release
dotnet test native\LanFlow.Desktop.Tests\LanFlow.Desktop.Tests.csproj -c Release
dotnet build native\LanFlow.Desktop\LanFlow.Desktop.csproj -c Release
```

再启动应用：

```powershell
dotnet run --project native\LanFlow.Desktop\LanFlow.Desktop.csproj -c Release
```

对每个待测组合执行：

1. 确认数据集、主题、透明模式、透明度、动画和缓存状态。
2. 记录环境快照。
3. 在两个固定分组间切换，采集至少 30 组 `selection-ack` 和 `content-stable`。
4. 在 100 项当前组连续滚动至少 20 秒，记录帧时间戳。
5. 记录 realized container count 的最小值、最大值和异常峰值。
6. 观察首帧是否出现超出配置几何的大图标闪烁；视觉结论必须注明人工观察或视频证据。
7. 导出原始 CSV，计算汇总并保存到最终报告旁的证据目录。

冷缓存与热缓存必须分开运行，不能在同一汇总中混合。

## 7. 性能目标

- selection-ack：P95 约 50 ms 或更低；
- warm content-stable：P95 约 100 ms 或更低；
- 滚动：接近当前刷新率下的流畅呈现，60 Hz 基准下重点检查 16.67 ms 长帧分布；
- 虚拟化：realized container count 应与可视区域和缓冲区规模相符，不应随总项目数线性增长。

若没有原始 UI 样本，不得填写百分位，不得写“达标”；使用 `NOT RUN` 或 `BLOCKED`，并明确阻塞原因。

## 8. 证据完整性

最终报告至少保留：

- 原始 CSV 或 trace 日志；
- 环境快照；
- 汇总计算方式；
- 每个组合的样本数；
- 自动化测试与 Release 构建结果；
- 人工回归日期、执行人和证据；
- 未运行项、失败项与后续动作。

只有自动化、真实 UI 性能采样和回归矩阵都有可审计记录时，才能宣告阶段完成。
