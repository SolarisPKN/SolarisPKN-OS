#!/bin/sh
# SPDX-License-Identifier: GPL-2.0-only
# Independent SHA-256 format-1 compatibility check against cryptsetup.
# Uses temporary regular files and disposable loop devices only.
set -eu

SELF=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
ROOT=$(CDPATH= cd -- "$SELF/.." && pwd)
TOOL="$ROOT/tools/verity-tree/solarispkn-verity-tree"
[ "$(id -u)" -eq 0 ] || { echo "Run inside disposable QEMU guest as root" >&2; exit 1; }
[ -x "$TOOL" ] || { echo "Build tool before running" >&2; exit 1; }
for cmd in veritysetup losetup cmp awk dd mktemp; do
    command -v "$cmd" >/dev/null 2>&1 || { echo "Missing $cmd" >&2; exit 1; }
done

TMP=$(mktemp -d /tmp/spkn-compat.XXXXXX)
DATA_LOOP=""
HASH_LOOP=""
cleanup() {
    [ -z "$HASH_LOOP" ] || losetup -d "$HASH_LOOP" 2>/dev/null || true
    [ -z "$DATA_LOOP" ] || losetup -d "$DATA_LOOP" 2>/dev/null || true
    rm -rf -- "$TMP"
}
trap cleanup EXIT HUP INT TERM

SALT=0011223344556677
# 257 blocks => at least two levels in the Merkle tree.
dd if=/dev/zero of="$TMP/data.img" bs=4096 count=257 >/dev/null 2>&1
printf "SolarisPKN cross-implementation test" |
    dd of="$TMP/data.img" bs=1 seek=8192 conv=notrunc >/dev/null 2>&1

OUR=$("$TOOL" format sha256 "$TMP/data.img" "$TMP/our.img" "$SALT")
OUR_ROOT=$(printf '%s\n' "$OUR" | awk '/^Root hash:/ {print $3; exit}')
[ -n "$OUR_ROOT" ] || { echo "Missing calculated root" >&2; exit 1; }

dd if=/dev/zero of="$TMP/ref.img" bs=4096 count=8 >/dev/null 2>&1
DATA_LOOP=$(losetup -f)
losetup "$DATA_LOOP" "$TMP/data.img"
HASH_LOOP=$(losetup -f)
losetup "$HASH_LOOP" "$TMP/ref.img"

REF=$(veritysetup --no-superblock --hash=sha256 --format=1 \
    --salt="$SALT" --data-block-size=4096 --hash-block-size=4096 \
    format "$DATA_LOOP" "$HASH_LOOP")
REF_ROOT=$(printf '%s\n' "$REF" | awk '/^Root hash:/ {print $3; exit}')

[ "$OUR_ROOT" = "$REF_ROOT" ] || {
    echo "FAIL: root mismatch vs cryptsetup" >&2
    printf 'Our root: %s\nReference: %s\n' "$OUR_ROOT" "$REF_ROOT" >&2
    exit 1
}

HASH_BLOCKS=$(printf '%s\n' "$OUR" | awk '/^Hash blocks:/ {print $3; exit}')
BYTES=$((HASH_BLOCKS * 4096))
dd if="$TMP/ref.img" of="$TMP/ref-tree.img" bs=1 count="$BYTES" >/dev/null 2>&1
cmp "$TMP/our.img" "$TMP/ref-tree.img"
echo "PASS: SHA-256 root and raw Merkle tree identical to veritysetup"
