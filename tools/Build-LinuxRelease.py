#!/usr/bin/env python3
"""一键生成 LanFlow Linux 的两条发布资产并回读校验（Python 标准库，无需 dpkg-deb / bsdtar）。

用法（仓库根目录）：
    dotnet publish native/LanFlow.Linux/LanFlow.Linux.csproj -c Release \
        -r linux-x64 --self-contained -o .build/linux-wt/publish/final-rN
    python tools/Build-LinuxRelease.py --publish-dir .build/linux-wt/publish/final-rN

产物（release/ 被 .gitignore 忽略，只上传 GitHub Release、不进提交）：
    LanFlow-linux-x64-rN.tar.gz (+ .sha256)
    lanflow_{version}_amd64.deb  (+ .sha256)

规范见 docs/ARCHITECTURE.md §5.3–§5.4。
"""

from __future__ import annotations

import argparse
import gzip
import hashlib
import io
import re
import sys
import tarfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
CSPROJ = ROOT / "native" / "LanFlow.Linux" / "LanFlow.Linux.csproj"
RELEASE_NOTES = ROOT / "docs" / "RELEASE-NOTES.md"
DESKTOP_TEMPLATE = ROOT / "tools" / "packtool" / "debian" / "lanflow.desktop"

CONTROL_TEMPLATE = """Package: lanflow
Version: {version}
Architecture: amd64
Maintainer: LanFlow <noreply@lanflow.local>
Installed-Size: {installed_size}
Section: utils
Priority: optional
Description: LanFlow 轻量启动与整理工具（UOS/Deepin）
 快速启动、分组整理、全局热键与自定义主题的轻量启动器。
 自包含 .NET 8 运行时，无额外依赖。
"""

POSTINST = """#!/bin/sh
set -e
# LanFlow 安装后：补可执行位 + 刷新桌面/图标缓存
chmod +x /opt/lanflow/LanFlow /opt/lanflow/lanflow.sh /opt/lanflow/install-lanflow.sh 2>/dev/null || true
update-desktop-database /usr/share/applications >/dev/null 2>&1 || true
gtk-update-icon-cache -f /usr/share/icons/hicolor >/dev/null 2>&1 || true
exit 0
"""

COPYRIGHT = """LanFlow — 轻量启动与整理工具

本项目遵循项目仓库的开源协议（详见 github.com/ZergZFZ/LanFlow）。
打包分发说明：
- 包内包含自包含 .NET 8 运行时，无外部运行时依赖
- 数据目录：~/.config/LanFlow/
- Linux 版不做应用内更新，升级请手动替换或重装更高版本的 .deb
"""

TEST_CARD = """# LanFlow Linux {version} 实机验收卡（{round_id}）

> 目标：UOS 20 Pro / Deepin，海光 x86_64，X11，glibc ≥ 2.28。本机无人值守不能代验，请在目标机逐项勾测。

## tar.gz（手动解压替换）

- [ ] 升级前备份 `~/.config/LanFlow/config.json`
- [ ] `tar -xzf LanFlow-linux-x64-{round_id}.tar.gz -C ~/LanFlow` 后，双击 `LanFlow` 或 `./lanflow.sh` 能启动
- [ ] 终端启动可见 `[LanFlow]` 日志行；首次启动无 glibc / libX11 报错
- [ ] 全局热键（Ctrl+Alt+L 或自定义字母键）唤回主窗口，重复触发稳定
- [ ] 托盘图标显示项目图标而非黑框；托盘「重启软件」可用
- [ ] 设置页：改主题/透明度/布局后保存，关闭再打开保持；点「取消」不落盘
- [ ] 新增或修改任一外观项后保存，重启应用仍保持（本轮 SettingsPipeline 回归重点）
- [ ] 分层透明 / 整窗透明切换正常，滑块有百分比反馈
- [ ] 分组切换、搜索、启动项目、编辑增删正常
- [ ] 外部拖放（X11 XDND 平台限制，失败时用「添加文件」兜底按钮）

## .deb

- [ ] `sudo apt install ./lanflow_{version}_amd64.deb` 安装成功
- [ ] 应用菜单出现 LanFlow 且图标正常，点击可启动
- [ ] 从 1.4.9 升级安装后，`~/.config/LanFlow/config.json` 未被清空
- [ ] `sudo dpkg -r lanflow` 卸载干净，用户数据目录保留

## 签名与完整性

- [ ] `sha256sum -c LanFlow-linux-x64-{round_id}.tar.gz.sha256` 通过
- [ ] `sha256sum -c lanflow_{version}_amd64.deb.sha256` 通过
"""


def check(condition: bool, message: str) -> None:
    if not condition:
        raise SystemExit(f"[FAIL] {message}")
    print(f"  [OK] {message}")


def read_version() -> str:
    text = CSPROJ.read_text(encoding="utf-8-sig")
    values = {}
    for tag in ("Version", "AssemblyVersion", "FileVersion"):
        m = re.search(rf"<{tag}>([^<]+)</{tag}>", text)
        if not m:
            raise SystemExit(f"[FAIL] csproj 缺少 <{tag}>")
        values[tag] = m.group(1).strip()
    version = values["Version"]
    for tag in ("AssemblyVersion", "FileVersion"):
        check(values[tag] == f"{version}.0", f"csproj {tag} ({values[tag]}) 与 Version ({version}) 一致")
    return version


def sha256_bytes(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def sha256_file(path: Path) -> str:
    return sha256_bytes(path.read_bytes())


def collect_publish(pub: Path) -> list[tuple[str, bytes]]:
    """返回扁平的 (name, bytes) 列表；排除 PDB（与 Windows 发布清单一致）。"""
    check(pub.is_dir(), f"publish 目录存在: {pub}")
    files = []
    for path in sorted(pub.iterdir(), key=lambda p: p.name):
        if not path.is_file():
            continue
        if path.suffix.lower() == ".pdb":
            continue
        files.append((path.name, path.read_bytes()))
    names = {name for name, _ in files}
    for required in ("LanFlow", "LanFlow.Core.dll", "SkiaSharp.dll",
                     "libSkiaSharp.so", "lanflow.png", "lanflow.sh", "install-lanflow.sh"):
        check(required in names, f"publish 含 {required}")
    elf = dict(files)["LanFlow"]
    check(elf[:4] == b"\x7fELF", "LanFlow 是合法 ELF 可执行文件")
    skia = dict(files)["SkiaSharp.dll"]
    check(b"2.88.9" in skia, "托管 SkiaSharp 仍钉住 2.88.9（glibc 2.28 约束）")
    check(not any(n.lower().startswith("flickd") for n in names), "publish 不含 FlickD 命名残留")
    return files


def tar_info(name: str, size: int, mode: int = 0o755) -> tarfile.TarInfo:
    info = tarfile.TarInfo(name)
    info.size = size
    info.mode = mode
    info.uid = 0
    info.gid = 0
    info.uname = "root"
    info.gname = "root"
    info.mtime = 0
    return info


def make_tar_gz(members: list[tuple[str, bytes]], directories: list[str] | None = None) -> bytes:
    """USTAR + 目录条目 + root/0 + 0755，输出 gzip 字节。"""
    buf = io.BytesIO()
    with gzip.GzipFile(fileobj=buf, mode="wb", compresslevel=9, mtime=0) as gz:
        with tarfile.open(fileobj=gz, mode="w", format=tarfile.USTAR_FORMAT) as tf:
            for directory in directories or []:
                info = tar_info(directory.rstrip("/"), 0, 0o755)
                info.type = tarfile.DIRTYPE
                tf.addfile(info)
            for name, data in members:
                tf.addfile(tar_info(name, len(data)), io.BytesIO(data))
    return buf.getvalue()


def ar_member(name: str, data: bytes) -> bytes:
    header = (
        f"{name:<16}{'0':<12}{'0':<6}{'0':<6}{'100666':<8}{len(data):<10}`\n"
    ).encode("ascii")
    if len(header) != 60:
        raise SystemExit(f"[FAIL] ar 头长度异常: {len(header)}")
    return header + data + (b"\n" if len(data) % 2 else b"")


def build_tarball(files: list[tuple[str, bytes]], extras: list[tuple[str, bytes]],
                  out: Path) -> None:
    with tarfile.open(out, "w:gz", format=tarfile.USTAR_FORMAT) as tf:
        for name, data in files + extras:
            tf.addfile(tar_info(name, len(data)), io.BytesIO(data))
    with tarfile.open(out, "r:gz") as tf:
        actual = tf.getmembers()
        check(len(actual) == len(files) + len(extras),
              f"tar.gz 回读条目数 {len(actual)} 与预期一致")
        check(all(m.mode == 0o755 and m.uid == 0 and m.gid == 0 for m in actual),
              "tar.gz 全部条目 root/0 且 0755（可执行位保留）")
        by_name = {m.name: tf.extractfile(m).read() for m in actual}
        check(by_name.get("LanFlow", b"")[:4] == b"\x7fELF", "tar.gz 内 LanFlow 为 ELF")


def build_deb(files: list[tuple[str, bytes]], version: str, out: Path,
             elf_sha: str) -> None:
    directories = [
        "opt", "opt/lanflow", "usr", "usr/share", "usr/share/applications",
        "usr/share/doc", "usr/share/doc/lanflow", "usr/share/icons",
        "usr/share/icons/hicolor", "usr/share/icons/hicolor/256x256",
        "usr/share/icons/hicolor/256x256/apps",
    ]
    desktop = DESKTOP_TEMPLATE.read_text(encoding="utf-8-sig").replace("\r\n", "\n").rstrip("\n") + "\n"
    check("杞婚噺" not in desktop, "desktop 模板为 UTF-8 中文，无历史 GBK 乱码")

    payload: list[tuple[str, bytes]] = [(f"opt/lanflow/{name}", data) for name, data in files]
    payload += [
        ("usr/share/applications/lanflow.desktop", desktop.encode("utf-8")),
        ("usr/share/icons/hicolor/256x256/apps/lanflow.png",
         dict(files)["lanflow.png"]),
        ("usr/share/doc/lanflow/copyright", COPYRIGHT.encode("utf-8")),
    ]
    payload.sort(key=lambda item: item[0])

    md5sums = "".join(
        f"{hashlib.md5(data).hexdigest()}  {name}\n" for name, data in payload
    )
    installed_kb = sum(len(data) for _, data in payload) // 1024 + 1
    control_files = [
        ("control", CONTROL_TEMPLATE.format(version=version, installed_size=installed_kb).encode("utf-8")),
        ("md5sums", md5sums.encode("utf-8")),
        ("postinst", POSTINST.encode("utf-8")),
    ]
    control_gz = make_tar_gz(control_files)
    data_gz = make_tar_gz(payload, directories)

    ar = b"!<arch>\n"
    ar += ar_member("debian-binary", b"2.0\n")
    ar += ar_member("control.tar.gz", control_gz)
    ar += ar_member("data.tar.gz", data_gz)
    out.write_bytes(ar)

    # ---- 回读校验 ----
    members: dict[str, bytes] = {}
    pos = 8
    while pos < len(ar):
        header = ar[pos:pos + 60]
        name = header[:16].decode("ascii").strip()
        size = int(header[48:58].decode("ascii").strip())
        members[name] = ar[pos + 60:pos + 60 + size]
        pos += 60 + size + (size % 2)
    check(list(members) == ["debian-binary", "control.tar.gz", "data.tar.gz"],
          ".deb ar 三成员顺序正确")
    check(members["debian-binary"] == b"2.0\n", ".deb debian-binary 为 2.0")

    with tarfile.open(fileobj=io.BytesIO(gzip.decompress(members["control.tar.gz"]))) as tf:
        ctrl = {m.name: tf.extractfile(m).read().decode("utf-8") for m in tf.getmembers()}
    check(re.search(rf"^Version: {re.escape(version)}$", ctrl["control"], re.M) is not None,
          f"control Version 与 csproj 一致 ({version})")
    check("chmod +x /opt/lanflow/LanFlow" in ctrl["postinst"], "postinst 含可执行位修复")

    with tarfile.open(fileobj=io.BytesIO(gzip.decompress(members["data.tar.gz"]))) as tf:
        data_members = {m.name: tf.extractfile(m).read() for m in tf.getmembers() if m.isfile()}
        check(all(m.uid == 0 and m.gid == 0 for m in tf.getmembers()), ".deb data 全部 root/0")
        check(all(d in tf.getnames() for d in directories), ".deb data 含全部目录条目")
    check(data_members["opt/lanflow/LanFlow"][:4] == b"\x7fELF", ".deb 内 LanFlow 为 ELF")
    check(sha256_bytes(data_members["opt/lanflow/LanFlow"]) == elf_sha,
          "tar.gz 与 .deb 内 LanFlow SHA-256 一致")
    check(data_members["usr/share/applications/lanflow.desktop"].decode("utf-8") == desktop,
          ".deb 菜单项内容与模板一致")
    mismatched = []
    for line in ctrl["md5sums"].strip().splitlines():
        digest, name = line.split("  ", 1)
        if hashlib.md5(data_members[name]).hexdigest() != digest:
            mismatched.append(name)
    check(not mismatched, ".deb md5sums 逐文件与实际载荷一致")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--publish-dir", required=True, type=Path)
    parser.add_argument("--round", default="r16")
    parser.add_argument("--out-dir", default=ROOT / "release", type=Path)
    parser.add_argument("--version", default=None, help="默认从 csproj 读取；传入时必须一致")
    args = parser.parse_args()

    version = read_version()
    if args.version:
        check(args.version == version, f"--version {args.version} 与 csproj {version} 一致")
    print(f"== LanFlow Linux {version} 发布打包（{args.round}）==")

    publish = args.publish_dir if args.publish_dir.is_absolute() else ROOT / args.publish_dir
    out_dir = args.out_dir if args.out_dir.is_absolute() else ROOT / args.out_dir
    out_dir.mkdir(parents=True, exist_ok=True)

    files = collect_publish(publish)
    elf_sha = sha256_bytes(dict(files)["LanFlow"])
    print(f"  publish 文件 {len(files)} 个（已排除 PDB）；LanFlow SHA-256 {elf_sha[:16]}…")

    notes = RELEASE_NOTES.read_text(encoding="utf-8-sig").replace("\r\n", "\n")
    extras = [
        ("RELEASE-NOTES.md", notes.encode("utf-8")),
        (f"TEST-CARD-{args.round}.md",
         TEST_CARD.format(version=version, round_id=args.round).encode("utf-8")),
    ]

    tarball = out_dir / f"LanFlow-linux-x64-{args.round}.tar.gz"
    deb = out_dir / f"lanflow_{version}_amd64.deb"
    for target in (tarball, deb):
        if target.exists():
            target.unlink()

    print("-- tar.gz --")
    build_tarball(files, extras, tarball)
    print("-- .deb --")
    build_deb(files, version, deb, elf_sha)

    print("-- SHA-256 sidecar --")
    for asset in (tarball, deb):
        digest = sha256_file(asset)
        asset.with_name(asset.name + ".sha256").write_bytes(
            f"{digest}  {asset.name}\n".encode("utf-8"))
        print(f"  {asset.name}: {digest} ({asset.stat().st_size:,} bytes)")

    print(f"\n完成。产物位于 {out_dir}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
