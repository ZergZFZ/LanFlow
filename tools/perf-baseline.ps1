# LanFlow R4 性能基准测量脚本（PRD P3 / ROADMAP R4）
# 用法：powershell -File tools\perf-baseline.ps1 -ExePath <LanFlow.exe 路径> [-Runs 3]
# 测量项：冷启动耗时（进程创建→主窗口句柄就绪）、启动内存、空闲驻留内存、
#         分组切换峰值/回收后工作集（验证 v1.5.2 防抖回收）、热键呼出延迟。
# 注意：会先停止正在运行的 LanFlow 实例（单实例互斥体冲突）；结束后不自动重启。

param(
    [Parameter(Mandatory = $true)] [string] $ExePath,
    [int] $Runs = 3,
    [int] $GroupSwitches = 8
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms

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

$results = [System.Collections.Generic.List[object]]::new()

try {
for ($run = 1; $run -le $Runs; $run++) {
    Write-Host "`n=== 第 $run/$Runs 次冷启动 ==="
    Stop-LanFlowInstances
    Start-Sleep -Milliseconds 500

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $proc = Start-Process -FilePath $resolved -PassThru
    $startupMs = Wait-MainWindowHandle $proc
    $wsAtReady = $proc.WorkingSet64; $privAtReady = $proc.PrivateMemorySize64

    Start-Sleep -Seconds 5   # 等待图标加载/首帧渲染稳定
    $proc.Refresh()
    $wsIdle5s = $proc.WorkingSet64; $privIdle5s = $proc.PrivateMemorySize64

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

    # ---- 热键呼出延迟（隐藏 -> 呼出）----
    $hotkeyMs = -1
    try {
        $window = Get-MainWindow $proc
        if ($null -ne $window) {
            $offscreenProp = [System.Windows.Automation.AutomationElement]::IsOffscreenProperty
            $isOff = [bool]$window.GetCurrentPropertyValue($offscreenProp)
            if (-not $isOff) {
                # 先用热键隐藏一次
                [System.Windows.Forms.SendKeys]::SendWait("^%l")
                Start-Sleep -Milliseconds 600
                $isOff = [bool]$window.GetCurrentPropertyValue($offscreenProp)
            }
            if ($isOff) {
                $sw2 = [System.Diagnostics.Stopwatch]::StartNew()
                [System.Windows.Forms.SendKeys]::SendWait("^%l")
                while ($sw2.ElapsedMilliseconds -lt 5000) {
                    if (-not [bool]$window.GetCurrentPropertyValue($offscreenProp)) { break }
                    Start-Sleep -Milliseconds 15
                }
                $hotkeyMs = $sw2.ElapsedMilliseconds
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
    })

    Stop-LanFlowInstances
}

} finally {
    Stop-LanFlowInstances
    Write-Host "`n=== 汇总 ==="
}

$results | Format-Table -AutoSize | Out-String -Width 200
$results | ConvertTo-Json -Depth 3



