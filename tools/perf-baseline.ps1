# LanFlow R4 性能基准测量脚本（PRD P3 / ROADMAP R4）
# 用法：powershell -File tools\perf-baseline.ps1 -ExePath <LanFlow.exe 路径> [-Runs 3]
# 测量项：冷启动耗时（进程创建→主窗口句柄就绪）、启动内存、空闲驻留内存、
#         分组切换峰值/回收后工作集（验证 v1.5.2 防抖回收）、
#         热键呼出延迟（直投 WM_HOTKEY，v1.5.4 起替代不可靠的 SendKeys 方案）、
#         启动→窗口可见 / 启动→首批项目就绪（解析毫秒级启动诊断日志）。
# 注意：会先停止正在运行的 LanFlow 实例（单实例互斥体冲突）；结束后不自动重启。
#       启动分段指标需要被测版本 ≥ v1.5.4（毫秒时间戳日志），旧版本显示"无数据"。

param(
    [Parameter(Mandatory = $true)] [string] $ExePath,
    [int] $Runs = 3,
    [int] $GroupSwitches = 8
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms
Add-Type -Namespace LanFlow.Perf -Name Win32 -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool PostMessage(System.IntPtr hWnd, uint Msg, System.IntPtr wParam, System.IntPtr lParam);
[DllImport("user32.dll")] public static extern bool ShowWindow(System.IntPtr hWnd, int nCmdShow);
[DllImport("user32.dll")] public static extern bool IsWindowVisible(System.IntPtr hWnd);
'@

if (-not (Test-Path $ExePath)) { throw "未找到 $ExePath" }
$resolved = (Resolve-Path $ExePath).Path

function Stop-LanFlowInstances {
    foreach ($attempt in 1..5) {
        $procs = @(Get-Process LanFlow -ErrorAction SilentlyContinue)
        if ($procs.Count -eq 0) { return }
        foreach ($p in $procs) {
            try {
                Write-Host "停止运行中的实例 PID=$($p.Id) ($($p.Path))"
                $p.Kill()
            } catch {
                # 进程可能恰好自行退出，忽略
            }
        }
        Start-Sleep -Milliseconds 800
    }
    Get-Process LanFlow -ErrorAction SilentlyContinue | Wait-Process -Timeout 10 -ErrorAction SilentlyContinue
}

function Wait-MainWindowHandle([System.Diagnostics.Process] $proc, [int] $timeoutMs = 30000) {
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    while ($sw.ElapsedMilliseconds -lt $timeoutMs) {
        try { $proc.Refresh(); if ($proc.HasExited) { throw "进程在启动后退出" } } catch { throw }
        if ($proc.MainWindowHandle -ne 0) { return $sw.ElapsedMilliseconds }
        Start-Sleep -Milliseconds 10
    }
    throw "等待主窗口超时（${timeoutMs}ms）"
}

function Get-MainWindow([System.Diagnostics.Process] $proc) {
    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $proc.Id)
    return $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
}

function Get-GroupItems($window) {
    if ($null -eq $window) { return @() }
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::ListItem)
    return @($window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond) | ForEach-Object { $_ })
}

function Get-StartupLogMetrics([long] $offset) {
    # 从本次启动新增的 config-diagnostics.log 内容解析阶段时间戳（依赖 v1.5.4 起的毫秒时间戳）。
    $logPath = Join-Path $env:LOCALAPPDATA 'LanFlow\config-diagnostics.log'
    if (-not (Test-Path $logPath)) { return $null }
    $begin = $null; $shown = $null; $firstRealized = $null
    try {
        $fs = [System.IO.FileStream]::new($logPath, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
        try {
            $fs.Seek($offset, [System.IO.SeekOrigin]::Begin) | Out-Null
            $reader = [System.IO.StreamReader]::new($fs)
            while ($null -ne ($line = $reader.ReadLine())) {
                if (-not ($line -match '^\[(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3})\] ([a-z:-]+)')) { continue }
                $ts = [datetime]::ParseExact($Matches[1], 'yyyy-MM-dd HH:mm:ss.fff', $null)
                $key = $Matches[2]
                if ($key -eq 'startup:begin' -and -not $begin) { $begin = $ts }
                elseif ($key -eq 'startup:window-shown' -and -not $shown) { $shown = $ts }
                elseif ($key -eq 'startup:item-realization' -and -not $firstRealized) { $firstRealized = $ts }
            }
        } finally { $fs.Dispose() }
    } catch { return $null }
    if (-not $begin) { return $null }
    [pscustomobject]@{
        Begin = $begin
        ShowDeltaMs = if ($shown) { [math]::Round(($shown - $begin).TotalMilliseconds) } else { $null }
        FirstRealizedMs = if ($firstRealized) { [math]::Round(($firstRealized - $begin).TotalMilliseconds) } else { $null }
    }
}

$results = [System.Collections.Generic.List[object]]::new()

try {
for ($run = 1; $run -le $Runs; $run++) {
    Write-Host "`n=== 第 $run/$Runs 次冷启动 ==="
    Stop-LanFlowInstances
    Start-Sleep -Milliseconds 500

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $diagLog = Join-Path $env:LOCALAPPDATA 'LanFlow\config-diagnostics.log'
    $logOffset = if (Test-Path $diagLog) { (Get-Item $diagLog).Length } else { 0 }
    $proc = Start-Process -FilePath $resolved -PassThru
    $startupMs = Wait-MainWindowHandle $proc
    $proc.Refresh()
    $mainHwnd = $proc.MainWindowHandle   # 启动时抓取句柄；后续 MainWindowHandle 可能返回 0，不能二次取
    $wsAtReady = $proc.WorkingSet64; $privAtReady = $proc.PrivateMemorySize64

    Start-Sleep -Seconds 5   # 等待图标加载/首帧渲染稳定
    $proc.Refresh()
    $wsIdle5s = $proc.WorkingSet64; $privIdle5s = $proc.PrivateMemorySize64
    $logMetrics = Get-StartupLogMetrics $logOffset
    if (-not $logMetrics) { $logMetrics = [pscustomobject]@{ ShowDeltaMs = $null; FirstRealizedMs = $null } }

    # ---- 分组切换与内存回收验证 ----
    $switchPeakWs = 0L
    $switchPeakPriv = 0L
    $postReclaimWs = 0L
    $switchedCount = 0
    try {
        $window = Get-MainWindow $proc
        $items = Get-GroupItems $window
        if ($items.Count -ge 2) {
            for ($i = 0; $i -lt [Math]::Min($GroupSwitches, $items.Count * 2); $i++) {
                $target = $items[$i % $items.Count]
                try {
                    ($target.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)) -as [System.Windows.Automation.SelectionItemPattern] | Out-Null
                    $sel = $target.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
                    $sel.Select()
                    $switchedCount++
                    Start-Sleep -Milliseconds 300   # 切换动画窗口期内采样峰值
                    $proc.Refresh()
                    if ($proc.WorkingSet64 -gt $switchPeakWs) { $switchPeakWs = $proc.WorkingSet64 }; if ($proc.PrivateMemorySize64 -gt $switchPeakPriv) { $switchPeakPriv = $proc.PrivateMemorySize64 }
                    Start-Sleep -Milliseconds 900   # 越过 800ms 防抖，让回收发生
                } catch { continue }
            }
            Start-Sleep -Seconds 3                  # 最后一次回收完成
            $proc.Refresh()
            $postReclaimWs = $proc.WorkingSet64; $postReclaimPriv = $proc.PrivateMemorySize64
        } else {
            Write-Host "UIA 未找到分组标签（可见分组不足），跳过切换测量"
        }
    } catch {
        Write-Host "UIA 分组切换失败：$($_.Exception.Message)"
    }

    # ---- 热键呼出延迟（直投 WM_HOTKEY，wParam=1 主热键；比 SendKeys/UIA IsOffscreen 可靠）----
    $hotkeyMs = -1
    try {
        if ($mainHwnd -ne [IntPtr]::Zero) {
            [LanFlow.Perf.Win32]::ShowWindow($mainHwnd, 0) | Out-Null   # SW_HIDE，模拟托盘驻留状态
            Start-Sleep -Milliseconds 600
            if (-not [LanFlow.Perf.Win32]::IsWindowVisible($mainHwnd)) {
                $sw2 = [System.Diagnostics.Stopwatch]::StartNew()
                [LanFlow.Perf.Win32]::PostMessage($mainHwnd, 0x0312, [IntPtr]1, [IntPtr]0) | Out-Null
                while ($sw2.ElapsedMilliseconds -lt 3000) {
                    if ([LanFlow.Perf.Win32]::IsWindowVisible($mainHwnd)) { break }
                    Start-Sleep -Milliseconds 5
                }
                if ([LanFlow.Perf.Win32]::IsWindowVisible($mainHwnd)) { $hotkeyMs = $sw2.ElapsedMilliseconds }
            } else {
                Write-Host "隐藏主窗口失败，跳过热键测量"
            }
        }
    } catch {
        Write-Host "热键延迟测量失败：$($_.Exception.Message)"
    }

    $results.Add([pscustomobject]@{
        Run                 = $run
        StartupMs           = $startupMs
        WsAtReadyMB         = [math]::Round($wsAtReady / 1MB, 1)
        PrivAtReadyMB       = [math]::Round($privAtReady / 1MB, 1)
        WsIdle5sMB          = [math]::Round($wsIdle5s / 1MB, 1)
        PrivIdle5sMB        = [math]::Round($privIdle5s / 1MB, 1)
        GroupSwitches       = $switchedCount
        SwitchPeakMB        = if ($switchPeakWs -gt 0) { [math]::Round($switchPeakWs / 1MB, 1) } else { $null }
        SwitchPeakPrivMB    = if ($switchPeakPriv -gt 0) { [math]::Round($switchPeakPriv / 1MB, 1) } else { $null }
        PostReclaimMB       = if ($postReclaimWs -gt 0) { [math]::Round($postReclaimWs / 1MB, 1) } else { $null }
        PostReclaimPrivMB   = if ($postReclaimPriv -gt 0) { [math]::Round($postReclaimPriv / 1MB, 1) } else { $null }
        HotkeyShowMs        = if ($hotkeyMs -ge 0) { $hotkeyMs } else { $null }
        ShowDeltaMs         = $logMetrics.ShowDeltaMs
        FirstRealizedMs     = $logMetrics.FirstRealizedMs
    })

    Stop-LanFlowInstances
}

} finally {
    Stop-LanFlowInstances
    Write-Host "`n=== 汇总 ==="

    function Get-MedianOf($values) {
        $vals = @($values | Where-Object { $null -ne $_ } | Sort-Object)
        if ($vals.Count -eq 0) { return $null }
        $vals[[math]::Floor(($vals.Count - 1) / 2)]
    }

    $medians = [ordered]@{
        '冷启动→句柄就绪 (ms)' = @{ v = Get-MedianOf $results.StartupMs;      warn = 1500 }
        '启动→窗口可见 (ms)'   = @{ v = Get-MedianOf $results.ShowDeltaMs;    warn = 300 }
        '启动→首批项目 (ms)'   = @{ v = Get-MedianOf $results.FirstRealizedMs; warn = 800 }
        '热键→窗口可见 (ms)'   = @{ v = Get-MedianOf $results.HotkeyShowMs;    warn = 150 }
        '空闲私有内存 (MB)'    = @{ v = Get-MedianOf $results.PrivIdle5sMB;    warn = 100 }
    }
    foreach ($entry in $medians.GetEnumerator()) {
        $m = $entry.Value.v; $warn = $entry.Value.warn
        $verdict = if ($null -eq $m) { '无数据' } elseif ($m -le $warn) { "PASS（阈值 $warn）" } else { "WARN（阈值 $warn）" }
        $display = if ($null -eq $m) { '-' } else { $m }
        Write-Host ("  {0}: {1}  {2}" -f $entry.Key, $display, $verdict)
    }
    Write-Host "  （启动→窗口可见/首批项目 为 v1.5.4 新增指标，阈值为建议值；热键延迟为 WM_HOTKEY 直投的应用侧路径）"
}

$results | Format-Table -AutoSize | Out-String -Width 200
$results | ConvertTo-Json -Depth 3



