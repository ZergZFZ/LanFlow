# Windows UI 最终性能报告

## 1. 报告状态

**状态：BLOCKED / 真实性能基准尚未执行。**

本报告于 2026-07-30 建立，记录了采样器自动化验证和当前可读取环境，但没有伪造 Windows 11 WPF 交互样本。当前会话无法满足目标基准的操作系统和人工交互条件，因此 selection-ack、content-stable 与滚动帧百分位均保持 `NOT RUN`。

- 报告准备基线 SHA：`1d60bbb`
- 分支：`windows`
- 构建配置：`Release`
- .NET SDK：`8.0.423`
- 目标数据集：500 总项目 / 100 当前组
- 原始 UI CSV：未生成

> 此处 SHA 表示开始 Task 5 时的已提交代码基线。采样器和本报告将在 Task 5 独立提交中加入；真实性能执行时必须另行记录实际待测提交 SHA。

## 2. 当前环境快照

| 字段 | 实际读取值 | 适用性 |
|---|---|---|
| OS | Microsoft Windows 10 企业版 LTSC | 不符合 Windows 11 目标环境 |
| OS version/build | 10.0.19044 / 19044 | 不符合目标 |
| Architecture | 64 位 | 已记录 |
| CPU | Intel Core i7-4790 @ 3.60 GHz，4 核 8 线程 | 已记录 |
| 物理 GPU | NVIDIA GeForce GTX 970，driver 32.0.15.6094 | 已记录 |
| 虚拟显示设备 | GameViewer Virtual Display Adapter；MuMu Virtual Display Adapter | 会影响合成与显示测量 |
| 可见分辨率 | GameViewer 3840×2160 @ 144 Hz；GTX 970 1920×1080 @ 74 Hz | 主显示器归属未人工确认 |
| Windows DPI | AppliedDPI 96，约 100% | 仅注册表读取，未人工确认显示器级缩放 |
| 主题/背景/高对比 | 未记录 | NOT RUN |
| 透明模式/透明度 | 未运行应用 | NOT RUN |

由于当前系统是 Windows 10 build 19044，且存在虚拟显示设备，本环境不能替代用户要求的 Windows 11 性能模式正式结果。

## 3. 自动化证据

2026-07-30 在 `Release` 下执行：

| 验证项 | 结果 | 证据摘要 |
|---|---|---|
| Core tests | PASS | 38 passed，0 failed，0 skipped |
| Desktop tests | PASS | 156 passed，0 failed，0 skipped |
| PerformanceSampleCollectorTests | PASS | 4 passed，覆盖 nearest-rank、空集合、环境字段、invariant 数字与 RFC 4180 转义 |
| Desktop Release build | PASS | 0 warnings，0 errors |

这些结果只证明代码级合同和构建状态，不是 WPF 帧时间或交互延迟数据。

## 4. 切组性能结果

| 显示模式 | 导航 | 触发 | 透明模式 | 透明度 | 缓存 | Marker | N | P50 | P95 | P99 | Max | Realized containers | 结论 |
|---|---|---|---|---:|---|---|---:|---:|---:|---:|---:|---|---|
| grid | top | click | layered | 85% | cold | selection-ack | 0 | NOT RUN | NOT RUN | NOT RUN | NOT RUN | NOT RUN | BLOCKED |
| grid | top | click | layered | 85% | cold | content-stable | 0 | NOT RUN | NOT RUN | NOT RUN | NOT RUN | NOT RUN | BLOCKED |
| grid | top | click | layered | 85% | warm | selection-ack | 0 | NOT RUN | NOT RUN | NOT RUN | NOT RUN | NOT RUN | BLOCKED |
| grid | top | click | layered | 85% | warm | content-stable | 0 | NOT RUN | NOT RUN | NOT RUN | NOT RUN | NOT RUN | BLOCKED |

其他 grid/list/card、top/left、click/hover、layered/wholeWindow 与 40/85/100% 组合均未执行。不得由上表外推任何性能结论。

## 5. 滚动帧结果

| 显示模式 | 透明模式 | 透明度 | 缓存 | 持续时间 | 帧数 | P50 | P95 | P99 | Max | >16.67 ms | >33.33 ms | >50 ms | >100 ms | 结论 |
|---|---|---:|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|
| grid | layered | 85% | warm | 0 s | 0 | NOT RUN | NOT RUN | NOT RUN | NOT RUN | NOT RUN | NOT RUN | NOT RUN | NOT RUN | BLOCKED |

没有采集 20 秒渲染时间戳，不能声称接近 60 FPS，也不能评估长帧分布。

## 6. 目标判定

| 目标 | 判定 | 原因 |
|---|---|---|
| selection-ack P95 约 50 ms | NOT EVALUATED | 样本数 0 |
| warm content-stable P95 约 100 ms | NOT EVALUATED | 样本数 0 |
| 滚动接近 60 FPS | NOT EVALUATED | 未采集帧间隔 |
| realized container count 不随总项目线性增长 | NOT EVALUATED | 未运行 500/100 数据集 |
| 85% 透明度首帧无大图标闪烁 | NOT EVALUATED | 未进行 WPF 人工观察或视频采集 |

## 7. 阻塞项与剩余风险

1. 当前 OS 为 Windows 10 企业版 LTSC build 19044，不是目标 Windows 11。
2. 当前会话没有可审计的 WPF 桌面交互自动化，无法导入隔离数据集并完成每组合至少 30 次切组。
3. 未生成 20 秒滚动帧时间戳；代码库当前只有 `UiPerformanceTrace` 的切组 marker，没有本次可引用的真实帧 CSV。
4. `UiPerformanceTrace` 使用 `TraceSource`；正式运行前需确认 listener、日志位置和采样导出链路确实启用。
5. 当前存在虚拟显示适配器，远程/串流合成会污染帧时间，正式测试需固定物理显示器或单独分组报告。
6. 主显示器、主题、桌面背景、高对比模式和显示器级缩放尚未人工确认。
7. 人工回归矩阵尚未执行；自动化测试通过不能替代透明背景、焦点、拖放和视觉稳定性验证。

## 8. 可复现执行步骤

1. 在 Windows 11 目标机检出实际待测提交，记录 `git rev-parse HEAD`。
2. 运行：

```powershell
dotnet test native\LanFlow.Core.Tests\LanFlow.Core.Tests.csproj -c Release
dotnet test native\LanFlow.Desktop.Tests\LanFlow.Desktop.Tests.csproj -c Release
dotnet build native\LanFlow.Desktop\LanFlow.Desktop.csproj -c Release
dotnet run --project native\LanFlow.Desktop\LanFlow.Desktop.csproj -c Release
```

3. 按 `windows-ui-baseline.md` 导入隔离的 500/100 数据集。
4. 固定显示器、分辨率、刷新率、缩放、主题、背景、透明模式和动画配置。
5. 每个组合分别采集 cold/warm 至少 30 次切组；保存原始 marker 数据和 realized container count。
6. 在 100 项组滚动至少 20 秒并保存逐帧时间戳。
7. 使用 `PerformanceSampleCollector` 生成 CSV 与 nearest-rank 汇总。
8. 将真实结果替换本报告中的 `NOT RUN`，附原始证据路径，并执行完整回归检查表。

## 9. 阶段结论

Task 5 的采样数据类型、百分位计算与 CSV 格式具备自动化覆盖；但 Windows 11 真实 UI 基准、85% 透明度视觉验证和完整人工回归没有执行。**Phase 4 和本轮整体优化不得据此宣告完成。**
