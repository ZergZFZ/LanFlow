#!/bin/bash
# ============================================================
# LanFlow UOS VM 自动化验证（对照 TEST-CARD-r4 可自动化项）
# 覆盖: 基础启动取证 / B5-1 300项目内存 / B5-2 <=450MB / B5-3 config version / B5-4 换位置 / B4-2 包完整性
# 用法: bash vm-verify.sh [轮次名]
# ============================================================
set -u
ROUND=${1:-vmverify-$(date +%Y%m%d-%H%M)}
SRC=/mnt/hgfs/lanflow
PKG=$SRC/final-r14
WORK=$HOME/lanflow-test
OUT=$SRC/results/$ROUND
CFGDIR=$HOME/.config/LanFlow
mkdir -p "$OUT"

[ -d "$SRC" ] && [ -d "$PKG" ] || { echo "ERROR: 共享不可用"; exit 1; }
rm -rf "$WORK"; mkdir -p "$WORK"; cp -r "$PKG"/. "$WORK"/ 2>/dev/null
cd "$WORK" || exit 1
chmod +x LanFlow

S=""
pass() { S+="PASS | $1\n"; }
fail() { S+="FAIL | $1\n"; }
mem_mb() { local p=$(pgrep -f "\./LanFlow" | head -1); [ -n "$p" ] && echo $(( $(ps -o rss= -p $p | tr -d ' ') / 1024 )) || echo 0; }

# ---------- A. 基础启动 + config version (B5-3) ----------
pkill -f LanFlow 2>/dev/null; sleep 1
rm -rf "$CFGDIR"
DISPLAY=:0 nohup ./LanFlow > "$OUT/run-a.log" 2>&1 &
sleep 15
grep -a "\[LanFlow\]" "$OUT/run-a.log" > "$OUT/a-lanflow.txt" 2>/dev/null
grep -a "\[取证\]"   "$OUT/run-a.log" > "$OUT/a-forensics.txt" 2>/dev/null
[ -s "$OUT/a-lanflow.txt" ]   && pass "A-1 [LanFlow] 日志存在" || fail "A-1 [LanFlow] 日志缺失"
[ -s "$OUT/a-forensics.txt" ] && pass "A-2 取证 Bounds 输出"   || fail "A-2 取证 Bounds 缺失"
if [ -f "$CFGDIR/config.json" ]; then
  VER=$(grep -o '"version"[ ]*:[ ]*[0-9]*' "$CFGDIR/config.json" | head -1 | grep -o '[0-9]*')
  [ "$VER" = "1" ] && pass "B5-3 config version=1" || fail "B5-3 config version=$VER (期望1)"
else
  fail "B5-3 config.json 未生成"
fi

# ---------- B. 300 项目场景 (B5-1 有界 + B5-2 <=450MB) ----------
pkill -f LanFlow 2>/dev/null; sleep 2
mkdir -p "$CFGDIR"
{
  echo '{"version":1,"groups":[{"id":"g1","name":"批量测试","collapsed":false,"sortMode":"custom","items":['
  for i in $(seq 1 300); do
    [ $i -gt 1 ] && echo ","
    printf '{"id":"id%04d","name":"Item%d","path":"/usr/bin/echo","command":"echo %d","kind":"app","hotkey":"","isEnabled":true,"useCount":0}' "$i" "$i" "$i"
  done
  echo ']}]}'
} > "$CFGDIR/config.json"
DISPLAY=:0 nohup ./LanFlow > "$OUT/run-b.log" 2>&1 &
sleep 20   # 300 项渲染较慢
PID=$(pgrep -f "\./LanFlow" | head -1)
[ -n "$PID" ] && pass "B5-1 300项目启动存活" || fail "B5-1 300项目未存活"
MEM=$(mem_mb)
echo "B5-2 内存(300项)=${MEM}MB 阈值450MB" >> "$OUT/mem.txt"
[ -n "$PID" ] && [ "$MEM" -le 450 ] && pass "B5-2 内存<=450MB ($MEM)" || { [ -n "$PID" ] && fail "B5-2 内存超限 ($MEM)" || fail "B5-2 进程已退出(无法测)"; }
grep -a "\[取证\]" "$OUT/run-b.log" >> "$OUT/a-forensics.txt" 2>/dev/null

# ---------- C. 换位置 (B5-4 可选) ----------
pkill -f LanFlow 2>/dev/null; sleep 2
rm -rf /tmp/lf
LANFLOW_CONFIG_DIR=/tmp/lf DISPLAY=:0 nohup ./LanFlow > "$OUT/run-c.log" 2>&1 &
sleep 12
pkill -f LanFlow 2>/dev/null
[ -f /tmp/lf/config.json ] && pass "B5-4 换位置生效(/tmp/lf)" || fail "B5-4 换位置未生效"

# ---------- D. 包完整性 (B4-2) ----------
[ -f "$WORK/RELEASE-NOTES.md" ] && pass "B4-2 RELEASE-NOTES.md 随包" || fail "B4-2 RELEASE-NOTES.md 缺失"

# ---------- 汇总 ----------
{
  echo "=== LanFlow VM 验证汇总 [$ROUND] ==="
  echo "时间: $(date)"
  uname -r
  echo "---"
  echo -e "$S"
} > "$OUT/summary.txt"
echo "DONE: $OUT"
