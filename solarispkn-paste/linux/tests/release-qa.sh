#!/usr/bin/env bash
set -euo pipefail
module="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$module"
echo "=== DEBIAN ENVIRONMENT ==="
sed -n '1,8p' /etc/os-release
echo "=== PYTHON TESTS ==="
(cd linux && python3 -m unittest discover -s tests -p 'test_*.py' -v)
echo "=== BUILD .DEB ==="
bash linux/packaging/build-deb.sh
echo "=== PACKAGE SMOKE ==="
bash linux/tests/package-smoke.sh
echo "=== BENCHMARK BUFFERED ==="
python3 linux/solarispkn_paste/cli.py benchmark --root "$HOME" --size-mib 16 --rounds 3 --hash sha256 --engine buffered
echo "=== BENCHMARK COPY_FILE_RANGE ==="
python3 linux/solarispkn_paste/cli.py benchmark --root "$HOME" --size-mib 16 --rounds 3 --hash sha256 --engine copyfile-range
echo "=== GRAPHICS ==="
file docs/assets/copy-pipeline.gif
python3 - <<'PY'
from pathlib import Path
from xml.etree import ElementTree
path=Path("docs/assets/copy-flow.svg")
root=ElementTree.parse(path).getroot()
assert root.tag.endswith("svg")
gif=Path("docs/assets/copy-pipeline.gif").read_bytes()
assert gif[:6]==b"GIF89a" and gif[-1:]==b"\x3b"
assert gif.count(b"\x21\xf9\x04")>=12
print("GRAPHICS_VALID")
PY
echo "LINUX_RELEASE_QA_PASS"
