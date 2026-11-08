#!/usr/bin/env bash
# SPDX-License-Identifier: GPL-3.0-only
# One-time signed APT repository setup .deb for Debian/Ubuntu.
# No postinst scripts, apt-key, trusted=yes or global APT trust.
set -euo pipefail
umask 022
root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)"
public="$root/apt/solarispkn-paste-archive-key.asc"
filename="solarispkn-repository_1.0-1_all.deb"
output="${1:-$root/apt/bootstrap/$filename}"
expected="161e51d5f57c34ed48425bee201c4d13bab9a0383718230898a49ef68ad67181"
[[ -s "$public" ]] || { echo "Archive public key missing: $public" >&2; exit 2; }
command -v dpkg-deb >/dev/null
echo "$expected  $public" | sha256sum -c --status || {
  echo "Unexpected signing public key; refusing to build" >&2; exit 3;
}
tmp="$(mktemp -d)"
trap 'rm -rf -- "$tmp"' EXIT
install -d -m 0755 \
  "$tmp/DEBIAN" \
  "$tmp/etc/apt/sources.list.d" \
  "$tmp/usr/share/keyrings"
install -m 0644 "$public" "$tmp/usr/share/keyrings/solarispkn-archive.asc"
cat > "$tmp/etc/apt/sources.list.d/solarispkn.sources" <<'SOURCES'
Types: deb
URIs: https://raw.githubusercontent.com/SolarisPKN/SolarisPKN-OS/main/apt/
Suites: stable
Components: main
Architectures: amd64 arm64
Signed-By: /usr/share/keyrings/solarispkn-archive.asc
SOURCES
cat > "$tmp/DEBIAN/control" <<'CONTROL'
Package: solarispkn-repository
Version: 1.0-1
Section: admin
Priority: optional
Architecture: all
Maintainer: SolarisPKN <31356753+SolarisPKN@users.noreply.github.com>
Depends: ca-certificates
Homepage: https://github.com/SolarisPKN/SolarisPKN-OS
Description: one-time signed SolarisPKN APT repository configuration
 Installs the scoped SolarisPKN archive OpenPGP public key and a
 deb822 APT sources entry using Signed-By. This is a third-party
 repository, not part of the official Debian or Ubuntu archives.
 No scripts, daemons, drivers, or user files are modified.
CONTROL
cat > "$tmp/DEBIAN/conffiles" <<'CONFFILES'
/etc/apt/sources.list.d/solarispkn.sources
CONFFILES
mkdir -p "$(dirname -- "$output")"
dpkg-deb --root-owner-group --build "$tmp" "$output" >/dev/null
echo "BOOTSTRAP_DEB_READY $output"
sha256sum "$output"
dpkg-deb -I "$output"
