#!/usr/bin/env bash
# One-time, fingerprint-pinned third-party APT registration. Review before use.
# This is SolarisPKN's own signed repository, NOT an official Debian/Ubuntu mirror.
set -euo pipefail
if [[ "$(id -u)" -ne 0 ]]; then
  echo "Run as root with: sudo bash linux/packaging/bootstrap-apt.sh" >&2
  exit 2
fi
base="https://raw.githubusercontent.com/SolarisPKN/SolarisPKN-OS/main/apt"
pub="$base/solarispkn-paste-archive-key.asc"
keyfile="/etc/apt/keyrings/solarispkn-paste.asc"
sources="/etc/apt/sources.list.d/solarispkn-paste.sources"
# This is replaced by the verified SHA256 of our *public* signing key in release.
expected="161e51d5f57c34ed48425bee201c4d13bab9a0383718230898a49ef68ad67181"
temp="$(mktemp)"
trap 'rm -f -- "$temp"' EXIT
if command -v curl >/dev/null 2>&1; then
  curl --fail --location --silent --show-error --retry 3 "$pub" -o "$temp"
elif command -v wget >/dev/null 2>&1; then
  wget -qO "$temp" "$pub"
else
  echo "Install curl or wget before registering the repository" >&2
  exit 2
fi
echo "$expected  $temp" | sha256sum --check --status || {
  echo "ERROR: signing key SHA-256 mismatch. Stop; do not trust repository." >&2
  exit 3
}
install -d -m 0755 /etc/apt/keyrings
install -m 0644 "$temp" "$keyfile"
cat > "$sources" <<EOF
Types: deb
URIs: $base/
Suites: stable
Components: main
Architectures: amd64 arm64
Signed-By: $keyfile
EOF
chmod 0644 "$sources"
echo "SolarisPKN-Paste APT repository configured with a pinned signing key."
apt-get update
apt-cache policy solarispkn-paste
echo "Now run: sudo apt install solarispkn-paste"
