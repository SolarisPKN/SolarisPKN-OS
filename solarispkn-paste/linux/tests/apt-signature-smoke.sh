#!/usr/bin/env bash
set -euo pipefail
root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../../.." && pwd)"
aptroot="$root/apt"
test -s "$aptroot/dists/stable/InRelease"
test -s "$aptroot/solarispkn-paste-archive-key.asc"
test -s "$aptroot/dists/stable/main/binary-amd64/Packages.gz"
tmp="$(mktemp -d)"
trap 'rm -rf -- "$tmp"' EXIT
mkdir -p "$tmp/lists/partial" "$tmp/empty"
cat > "$tmp/local.sources" <<EOF
Types: deb
URIs: file:$aptroot
Suites: stable
Components: main
Architectures: amd64
Signed-By: $aptroot/solarispkn-paste-archive-key.asc
EOF
apt-get update \
  -o Dir::Etc::sourcelist="$tmp/local.sources" \
  -o Dir::Etc::sourceparts="$tmp/empty" \
  -o Dir::State::lists="$tmp/lists" \
  -o APT::Get::List-Cleanup=0 \
  -o Debug::NoLocking=1
apt-cache \
  -o Dir::State::lists="$tmp/lists" \
  -o Dir::Etc::sourcelist="$tmp/local.sources" \
  -o Dir::Etc::sourceparts="$tmp/empty" \
  policy solarispkn-paste
result="$(apt-cache -o Dir::State::lists="$tmp/lists" \
  -o Dir::Etc::sourcelist="$tmp/local.sources" \
  -o Dir::Etc::sourceparts="$tmp/empty" \
  policy solarispkn-paste)"
if ! grep -Fq '0.3.0~alpha1-1' <<< "$result"; then
  echo "APT cannot see the signed SolarisPKN-Paste package" >&2
  exit 3
fi
echo "SIGNED_APT_INDEX_PASS"
