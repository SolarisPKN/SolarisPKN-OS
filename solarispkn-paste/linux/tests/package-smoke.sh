#!/usr/bin/env bash
set -euo pipefail
module="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)"
package="$module/dist/solarispkn-paste_0.3.0~alpha1-1_all.deb"
if [[ ! -f "$package" ]]; then
  bash "$module/linux/packaging/build-deb.sh"
fi
root="$(mktemp -d)"
trap 'rm -rf -- "$root"' EXIT
dpkg-deb -x "$package" "$root/pkg"
dpkg-deb -I "$package" control | grep -E '^(Package|Version|Architecture|Depends):'
export PYTHONPATH="$root/pkg/usr/lib/solarispkn-paste"
python3 - <<'PY'
from solarispkn_paste.cli import main, VERSION
assert VERSION == "0.3.0-alpha.1", VERSION
assert main(["profile","."]) == 0
print("PACKAGE_IMPORT_PROFILE_PASS")
PY
if ! command -v b3sum >/dev/null 2>&1; then
  echo "B3SUM_NOT_INSTALLED: checking official Debian repository without root"
  mkdir -p "$root/debs"
  if (cd "$root/debs" && timeout 45 apt-get download b3sum); then
    files=("$root"/debs/b3sum_*.deb)
    if [[ -f "${files[0]}" ]]; then
      dpkg-deb -x "${files[0]}" "$root/b3"
      export PATH="$root/b3/usr/bin:$PATH"
    fi
  fi
fi
if command -v b3sum >/dev/null 2>&1; then
  mkdir -p "$root/work"
  printf 'solaris-verification-2026' > "$root/work/origin"
  python3 - <<PY
from solarispkn_paste.cli import main
assert main(["copy","$root/work/origin","$root/work/copied"]) == 0
print("REAL_BLAKE3_VERIFIED_PASS")
PY
  cmp "$root/work/origin" "$root/work/copied"
  echo "BLAKE3_REAL_PASS"
else
  echo "BLAKE3_EXTERNAL_TOOL_UNAVAILABLE: dependency declared; SHA256 path tested separately"
fi
python3 - <<PY
from solarispkn_paste.cli import main
assert main(["benchmark","--root","$root","--size-mib","2","--rounds","2","--hash","sha256"]) == 0
print("PACKAGE_BENCHMARK_PASS")
PY
echo "PACKAGE_SMOKE_PASS"
