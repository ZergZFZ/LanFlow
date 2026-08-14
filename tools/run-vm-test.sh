#!/bin/bash
# ============================================================
# LanFlow UOS VM 冒烟测试脚本（UOS 客户机侧执行）
# 用法: bash run-vm-test.sh [轮次名]
# 数据流: 共享 /mnt/hgfs/lanflow (Windows publish/) <-> 本地 ~/lanflow-test
# 产出: 结果写回共享 results/<轮次>/  (Windows 直接可见)
# ============================================================
set -u
ROUND=${1:-vm-$(date +%Y%m%d-%H%M)}
SRC=/mnt/hgfs/lanflow          # 共享根 = Windows publish 目录
PKG=$SRC/final-r14              # 当前测试包（round14）
WORK=$HOME/lanflow-test        # 客户机本地工作目录
OUT=$SRC/results/$ROUND        # 结果输出（回写共享）

# 1. 校验共享就绪
if [ ! -d "$SRC" ] || [ ! -d "$PKG" ]; then
  echo "ERROR: 共享目录不可用: $SRC (检查 VMware 共享是否挂载)"
  exit 1
fi
mkdir -p "$OUT"

# 2. 复制包到本地（避免 hgfs 直接运行的兼容性问题）
rm -rf "$WORK"; mkdir -p "$WORK"
cp -r "$PKG"/. "$WORK"/ 2>/dev/null
cd "$WORK" || exit 1
chmod +x LanFlow createdump 2>/dev/null

# 3. 清理旧进程，启动 LanFlow
pkill -f LanFlow 2>/dev/null; sleep 1
DISPLAY=:0 nohup ./LanFlow > "$OUT/run.log" 2>&1 &
sleep 12   # 等待窗口渲染与取证输出

# 4. 取证收集
pgrep -af LanFlow > "$OUT/pid.txt" 2>&1
grep -a "\[LanFlow\]"  "$OUT/run.log" > "$OUT/lanflow-lines.txt" 2>/dev/null
grep -a "\[取证\]"    "$OUT/run.log" > "$OUT/forensics.txt" 2>/dev/null
grep -a "热键\|Hotkey\|Bounds\|崩溃\|Exception\|Fatal" "$OUT/run.log" | head -20 > "$OUT/keywords.txt" 2>/dev/null
cp -r "$HOME/.config/lanflow"* "$OUT/" 2>/dev/null || true

# 5. 环境快照（内核/系统/glibc）
{
  echo "=== 时间: $(date) ==="
  uname -a
  grep -E "^(NAME|VERSION)" /etc/os-release 2>/dev/null
  ldd --version 2>/dev/null | head -1
  echo "=== 进程 ==="
  cat "$OUT/pid.txt"
  echo "=== [LanFlow] 行 ==="
  cat "$OUT/lanflow-lines.txt" 2>/dev/null
  echo "=== 取证 ==="
  cat "$OUT/forensics.txt" 2>/dev/null
} > "$OUT/env.txt"

echo "DONE: 结果已写入共享 $OUT"
