#!/usr/bin/env bash
# scripts/guard-main-release.sh — main 分支发布守卫（CI 调用）
#
# 检查项：
#   [1] 冲突标记（任何事件）
#   [2] csproj 版本号一致性（任何事件）
#   [3] push 事件：main 的 first-parent 链上不得直接长出 feat/fix 等开发性提交
#   [4] PR 事件：不得向 main 引入过程文档 / 构建产物等越界文件
#
# 说明：合法的发布流程是 merge dev/* → main，合并会天然带来业务代码改动，
#       因此这里不拦截业务代码本身，而是拦截「直接提交在 main 上的开发性提交」
#       与「混入 main 的过程/构建产物」。
set -u

fail() { echo "$1" >&2; exit 1; }

echo "== [1/4] 检查冲突标记 =="
git grep -nE '^(<<<<<<<|=======|>>>>>>>)' -- . >/dev/null 2>&1 \
  && fail "❌ 仓库内存在未解决的冲突标记"

echo "== [2/4] 检查版本号一致性 =="
norm() { printf '%s' "$1" | sed -E 's/(\.0)+$//'; }
ok=1
for proj in native/LanFlow.Desktop/LanFlow.Desktop.csproj \
            linux/native/LanFlow.Linux/LanFlow.Linux.csproj; do
  [ -f "$proj" ] || continue
  v="$(sed -n 's:.*<Version>\([0-9.]*\)</Version>.*:\1:p' "$proj" | head -1)"
  a="$(sed -n 's:.*<AssemblyVersion>\([0-9.]*\)</AssemblyVersion>.*:\1:p' "$proj" | head -1)"
  f="$(sed -n 's:.*<FileVersion>\([0-9.]*\)</FileVersion>.*:\1:p' "$proj" | head -1)"
  if [ -z "$v" ] || [ "$(norm "$v")" != "$(norm "$a")" ] \
     || [ "$(norm "$v")" != "$(norm "$f")" ]; then
    echo "❌ $proj 版本号不一致：Version=$v / AssemblyVersion=$a / FileVersion=$f"
    ok=0
  fi
done
[ "$ok" -eq 1 ] || exit 1

if [ -n "${GITHUB_BASE_REF:-}" ]; then
  # ---------- PR 事件 ----------
  echo "== [3/4] PR 越界文件检查（过程文档/构建产物不得进入 main） =="
  git fetch --quiet origin "$GITHUB_BASE_REF" 2>/dev/null || true
  base="origin/$GITHUB_BASE_REF"
  changed="$(git diff --name-only "$base...HEAD" 2>/dev/null || true)"
  if [ -n "$changed" ]; then
    forbidden="$(printf '%s\n' "$changed" \
      | grep -E '^(docs/(fankui|superpowers|archive/plans|archive/superpowers)/|artifacts/|release/|\.build/|(^|/)bin/|(^|/)obj/)' \
      || true)"
    if [ -n "$forbidden" ]; then
      echo "❌ main 禁止包含过程文档/构建产物："
      printf '%s\n' "$forbidden"
      exit 1
    fi
  fi
else
  # ---------- push 事件 ----------
  echo "== [3/4] push 提交类型检查（main 禁止直接 feat/fix 等开发性提交） =="
  bad="$(git log --first-parent --pretty=%s 'origin/main..HEAD' 2>/dev/null \
    | grep -E '^(feat|fix|refactor|perf|test|style)(\(|:| )' | head -5 || true)"
  if [ -n "$bad" ]; then
    echo "❌ main 上出现直接开发性提交（请先合并到 dev/windows 或 dev/linux）："
    printf '%s\n' "$bad"
    exit 1
  fi
fi

echo "✅ main 分支守卫通过"
