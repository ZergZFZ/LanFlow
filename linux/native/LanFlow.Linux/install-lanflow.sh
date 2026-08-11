#!/bin/sh
# LanFlow 桌面快捷方式安装脚本（带项目图标）
# 用法：解压后在包目录内执行 ./install-lanflow.sh
# 效果：生成 ~/.local/share/applications/lanflow.desktop，应用菜单出现带图标的 LanFlow

DIR="$(cd "$(dirname "$0")" && pwd)"
APP="$DIR/LanFlow"
ICON="$DIR/lanflow.png"

if [ ! -f "$APP" ]; then
    echo "错误：未找到 LanFlow 可执行文件（当前目录：$DIR）"
    exit 1
fi

chmod +x "$APP" 2>/dev/null || true

mkdir -p "$HOME/.local/share/applications"
cat > "$HOME/.local/share/applications/lanflow.desktop" <<EOF
[Desktop Entry]
Type=Application
Name=LanFlow
Comment=LanFlow 轻量启动与整理工具
Exec="$APP"
Icon=$ICON
Terminal=false
Categories=Utility;
EOF

# 刷新桌面数据库（无此命令的系统忽略）
update-desktop-database "$HOME/.local/share/applications" >/dev/null 2>&1 || true

echo "已安装：应用菜单中可找到 LanFlow（图标 $ICON）"
echo "如需卸载：rm ~/.local/share/applications/lanflow.desktop"
