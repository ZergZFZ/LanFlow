# ============================================================
# LanFlow UOS VM smoke-test trigger (run on Windows dev machine)
# Usage: powershell -ExecutionPolicy Bypass -File run-vm-test.ps1 [-Round <name>]
# Prereq: UOS VM powered on, open-vm-tools running, share "lanflow" mounted
# Output: publish\results\<Round>\ with run.log / lanflow-lines.txt / forensics.txt / env.txt
# ============================================================
param(
    [string]$Round = "vm-$(Get-Date -Format 'yyyyMMdd-HHmm')"
)

$ErrorActionPreference = "Stop"
$VMRUN   = "E:\VMware\vmrun.exe"
$VMX     = "E:\UOS DATE\UOS 20.vmx"
$GUEST   = "test"
$GPass   = "111"
$PUBLISH = "E:\AI\LanFlow-main\.build\linux-wt\publish"
$TOOLS   = Split-Path -Parent $MyInvocation.MyCommand.Path
$SH_SCRIPT = "$TOOLS\run-vm-test.sh"

# 0. Pre-checks
if (-not (Test-Path $VMRUN))   { Write-Error "vmrun not found: $VMRUN"; exit 1 }
if (-not (Test-Path $VMX))     { Write-Error "VM not found: $VMX"; exit 1 }
if (-not (Test-Path $PUBLISH)) { Write-Error "publish dir not found: $PUBLISH"; exit 1 }

# 1. Ensure VM is running
$running = & $VMRUN -T ws list
if ($running -notmatch [regex]::Escape($VMX)) {
    Write-Host "[1/4] VM not running, starting..."
    & $VMRUN -T ws start $VMX gui
    Start-Sleep -Seconds 15
} else {
    Write-Host "[1/4] VM already running"
}

# 2. Sync test script into share (= into VM)
Copy-Item $SH_SCRIPT $PUBLISH -Force
Write-Host "[2/4] run-vm-test.sh synced to share"

# 3. Run test remotely
Write-Host "[3/4] Executing in UOS VM: bash /mnt/hgfs/lanflow/run-vm-test.sh $Round"
& $VMRUN -T ws -gu $GUEST -gp $GPass runScriptInGuest $VMX /bin/bash "bash /mnt/hgfs/lanflow/run-vm-test.sh $Round"
if ($LASTEXITCODE -ne 0) { Write-Error "Remote execution failed (exit=$LASTEXITCODE)"; exit 1 }

# 4. Collect results
$resultDir = "$PUBLISH\results\$Round"
if (Test-Path $resultDir) {
    Write-Host "[4/4] Results ready: $resultDir"
    Get-ChildItem $resultDir | Select-Object Name, Length | Format-Table -AutoSize
    Write-Host "--- lanflow-lines.txt ---"
    Get-Content "$resultDir\lanflow-lines.txt" -ErrorAction SilentlyContinue
    Write-Host "--- forensics.txt ---"
    Get-Content "$resultDir\forensics.txt" -ErrorAction SilentlyContinue
} else {
    Write-Warning "Result dir not found: $resultDir (check share mount / script execution)"
}
