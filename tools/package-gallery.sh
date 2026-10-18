#!/usr/bin/env bash
# 把 NativeAOT 发布的 Gallery 打成可分发的压缩包（release 流程与本地共用）。
# 用法：tools/package-gallery.sh <rid> <version> [publish-dir]
#   publish-dir 默认 artifacts/gallery/<rid>，需已包含 RaTeX 原生库（ratex_ffi.*）。
# 产物：artifacts/release/StreamMark.Gallery-<version>-<rid>.{zip|tar.gz}
#   osx-*   → .app 包（ad-hoc 签名）再 zip
#   linux-* → tar.gz
#   win-*   → zip
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
rid="${1:?rid required}"
version="${2:?version required}"
publish="${3:-$root/artifacts/gallery/$rid}"
out="$root/artifacts/release"
name="StreamMark.Gallery-$version-$rid"
stage="$root/artifacts/release/stage/$name"

ls "$publish"/*ratex_ffi.* >/dev/null 2>&1 || { echo "missing RaTeX native library in $publish" >&2; exit 1; }
rm -rf "$stage"
mkdir -p "$stage" "$out"

# 只带可执行文件与原生库：符号文件（.pdb / .dbg / .dSYM）与 XML 文档不进包
copy_payload() {
  local dest="$1"
  find "$publish" -maxdepth 1 -type f \
    ! -name '*.pdb' ! -name '*.dbg' ! -name '*.xml' ! -name '*.json' \
    -exec cp {} "$dest/" \;
}

case "$rid" in
  osx-*)
    app="$stage/StreamMark Gallery.app"
    mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
    copy_payload "$app/Contents/MacOS"
    sed -e "s/@VERSION@/$version/g" -e "s/@SHORT_VERSION@/${version%%-*}/g" "$root/samples/StreamMark.Gallery/Packaging/Info.plist" > "$app/Contents/Info.plist"
    # 未公证：ad-hoc 签名让 arm64 能启动；首次打开需右键 → 打开，或 xattr -dr com.apple.quarantine
    # 扩展属性不进包：否则 zip 里会带 ._* 文件，被 unzip 解出后破坏签名封装
    xattr -cr "$app" 2>/dev/null || true
    if command -v codesign >/dev/null; then
      codesign --force --deep --sign - "$app"
    fi
    rm -f "$out/$name.zip"
    (cd "$stage" && ditto -c -k --norsrc --noextattr --keepParent "StreamMark Gallery.app" "$out/$name.zip")
    ;;
  linux-*)
    copy_payload "$stage"
    chmod +x "$stage/StreamMark.Gallery"
    tar -C "$root/artifacts/release/stage" -czf "$out/$name.tar.gz" "$name"
    ;;
  win-*)
    copy_payload "$stage"
    rm -f "$out/$name.zip"
    if command -v 7z >/dev/null; then
      (cd "$root/artifacts/release/stage" && 7z a -tzip -bso0 "$out/$name.zip" "$name")
    elif command -v zip >/dev/null; then
      (cd "$root/artifacts/release/stage" && zip -qr "$out/$name.zip" "$name")
    else
      (cd "$root/artifacts/release/stage" && powershell -NoProfile -Command "Compress-Archive -Path '$name' -DestinationPath '$name.zip'" && mv "$name.zip" "$out/")
    fi
    ;;
  *) echo "unsupported rid $rid" >&2; exit 1 ;;
esac
rm -rf "$root/artifacts/release/stage"
ls -la "$out" | grep "$name"
