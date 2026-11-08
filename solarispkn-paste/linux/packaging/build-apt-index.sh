#!/usr/bin/env bash
# Build APT indexes. InRelease/Release.gpg must be signed offline.
set -euo pipefail
repo="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../../.." && pwd)"
package="$repo/solarispkn-paste/dist/solarispkn-paste_0.3.0~alpha1-1_all.deb"
aptroot="$repo/apt"
[[ -s "$package" ]] || { echo "Missing Debian package: $package" >&2; exit 2; }
command -v dpkg-scanpackages >/dev/null
command -v apt-ftparchive >/dev/null
mkdir -p "$aptroot/pool/main/s/solarispkn-paste"
cp -- "$package" "$aptroot/pool/main/s/solarispkn-paste/"
for arch in amd64 arm64; do
  mkdir -p "$aptroot/dists/stable/main/binary-$arch"
done
(
  cd "$aptroot"
  dpkg-scanpackages -m pool/main/s/solarispkn-paste /dev/null \
    > dists/stable/main/binary-amd64/Packages
  cp dists/stable/main/binary-amd64/Packages \
     dists/stable/main/binary-arm64/Packages
  for arch in amd64 arm64; do
    gzip -9n -c "dists/stable/main/binary-$arch/Packages" \
      > "dists/stable/main/binary-$arch/Packages.gz"
  done
  apt-ftparchive \
    -o APT::FTPArchive::Release::Origin="SolarisPKN" \
    -o APT::FTPArchive::Release::Label="SolarisPKN Paste" \
    -o APT::FTPArchive::Release::Suite="stable" \
    -o APT::FTPArchive::Release::Codename="stable" \
    -o APT::FTPArchive::Release::Architectures="amd64 arm64" \
    -o APT::FTPArchive::Release::Components="main" \
    -o APT::FTPArchive::Release::Description="SolarisPKN-Paste signed experimental packages for Debian and Ubuntu" \
    release dists/stable > dists/stable/Release
)
echo "APT_INDEX_READY: $aptroot"
echo "Sign Release with GPG into dists/stable/InRelease and Release.gpg"
