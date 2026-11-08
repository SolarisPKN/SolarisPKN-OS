#!/usr/bin/env bash
set -euo pipefail
umask 022
script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
linux_dir="$(cd "$script_dir/.." && pwd)"
repo="$(cd "$linux_dir/.." && pwd)"
version="0.3.0~alpha1-1"
out="${1:-$repo/dist}"
mkdir -p "$out"
out="$(cd "$out" && pwd)"
if ! command -v dpkg-deb >/dev/null || ! command -v python3 >/dev/null; then
  echo "dpkg-deb and python3 required" >&2; exit 2
fi
tmp="$(mktemp -d)"
trap 'rm -rf -- "$tmp"' EXIT
install -d -m 0755 "$tmp/DEBIAN" "$tmp/usr/bin" \
  "$tmp/usr/lib/solarispkn-paste/solarispkn_paste" \
  "$tmp/usr/share/doc/solarispkn-paste"
install -m 0644 "$linux_dir/solarispkn_paste/__init__.py" \
  "$tmp/usr/lib/solarispkn-paste/solarispkn_paste/"
install -m 0644 "$linux_dir/solarispkn_paste/cli.py" \
  "$tmp/usr/lib/solarispkn-paste/solarispkn_paste/"
cat > "$tmp/usr/bin/solarispkn-paste" <<'WRAPPER'
#!/usr/bin/python3
import sys
sys.path.insert(0, "/usr/lib/solarispkn-paste")
from solarispkn_paste.cli import main
raise SystemExit(main())
WRAPPER
chmod 0755 "$tmp/usr/bin/solarispkn-paste"
install -m 0644 "$repo/README.md" \
  "$tmp/usr/share/doc/solarispkn-paste/README.md"
install -m 0644 "$repo/README.es.md" \
  "$tmp/usr/share/doc/solarispkn-paste/README.es.md"
cat > "$tmp/DEBIAN/control" <<'CONTROL'
Package: solarispkn-paste
Version: 0.3.0~alpha1-1
Section: utils
Priority: optional
Architecture: all
Maintainer: SolarisPKN Labs
Depends: python3 (>= 3.10), b3sum
Homepage: https://github.com/SolarisPKN/SolarisPKN-OS
Description: SolarisPKN-Paste experimental verified Linux copy CLI
 Atomic staging, BLAKE3 verification using b3sum, conservative
 HDD/SSD/RAID-aware routing and bounded synthetic benchmarking.
 This preview does not provide Windows Explorer integration on Linux.
CONTROL
cat > "$tmp/DEBIAN/postinst" <<'POST'
#!/bin/sh
set -e
# No user files, services, system settings or shell extensions are changed.
exit 0
POST
chmod 0755 "$tmp/DEBIAN/postinst"
find "$tmp" -type d -exec chmod 755 {} +
find "$tmp" -type f ! -path '*/DEBIAN/postinst' ! -path '*/usr/bin/solarispkn-paste' -exec chmod 644 {} +
dpkg-deb --root-owner-group --build "$tmp" "$out/solarispkn-paste_${version}_all.deb"
sha256sum "$out/solarispkn-paste_${version}_all.deb"
