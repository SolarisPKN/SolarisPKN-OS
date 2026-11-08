#!/usr/bin/env bash
# SPDX-License-Identifier: GPL-3.0-only
# Build Debian source+binaries for independent packages in the SolarisPKN-OS monorepo.
# Works in an isolated build directory; never modifies apt sources or the host OS.
set -euo pipefail
umask 022
root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)"
module="${1:?Usage: build-source.sh solarispkn-paste|solarispkn-verity [destination]}"
case "$module" in
  solarispkn-paste|solarispkn-verity) ;;
  *) echo "Unknown module: $module" >&2; exit 2 ;;
esac
src="$root/$module"
[[ -f "$src/debian/control" && -f "$src/debian/changelog" ]] || {
  echo "Missing debian/ metadata in $module" >&2; exit 3;
}
version="$(dpkg-parsechangelog --file "$src/debian/changelog" -S Version)"
upstream="${version%-*}"
destination="${2:-$root/packaging/debian/dist/$module}"
mkdir -p "$destination"
destination="$(cd "$destination" && pwd)"
work="$(mktemp -d)"
trap 'rm -rf -- "$work"' EXIT
stage="$work/$module-$upstream"
mkdir -p "$stage"

# Exclude previously built artifacts, local virtualenvs and Python caches.
tar -C "$src" \
  --exclude='./.git' --exclude='./dist' --exclude='./out' \
  --exclude='./bin' --exclude='./obj' \
  --exclude='./tools/verity-tree/solarispkn-verity-tree' \
  --exclude='__pycache__' --exclude='*.pyc' --exclude='*.pyo' \
  --exclude='.venv' --exclude='./.pytest_cache' \
  -cf - . | tar -C "$stage" -xf -

# On Windows DrvFS all copied files can appear executable. Normalize archive
# permissions, then explicitly mark source scripts and debhelper rules.
find "$stage" -type d -exec chmod 0755 {} +
find "$stage" -type f -exec chmod 0644 {} +
chmod 0755 "$stage/debian/rules"
for f in "$stage"/debian/tests/* "$stage"/linux/solarispkn-paste; do
  if [[ -f "$f" && "$f" != */control ]]; then chmod 0755 "$f"; fi
done

# Original upstream tarball never includes Debian-specific packaging.
epoch="${SOURCE_DATE_EPOCH:-1794096000}"
tar --sort=name --mtime="@$epoch" --owner=0 --group=0 --numeric-owner \
  -C "$stage" --exclude='./debian' -cJf \
  "$work/${module}_${upstream}.orig.tar.xz" .
(
  cd "$stage"
  dpkg-buildpackage -us -uc -sa
)
for f in "$work"/*.dsc "$work"/*.changes "$work"/*.buildinfo "$work"/*.deb "$work"/*.orig.tar.xz "$work"/*.debian.tar.xz; do
  if [[ -f "$f" ]]; then cp -- "$f" "$destination/"; fi
done
echo "DEBIAN_SOURCE_READY module=$module version=$version out=$destination"
ls -lh "$destination"/*.dsc "$destination"/*.changes "$destination"/*.deb
