#!/bin/sh
# SPDX-License-Identifier: GPL-2.0-only
# Destructive only to temporary files and own loop/DM devices.
# Run inside a disposable QEMU guest, not on the host.
set -eu

SELF=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
ROOT=$(CDPATH= cd -- "$SELF/.." && pwd)
TOOL="$ROOT/tools/verity-tree/solarispkn-verity-tree"
BLOCKS=257
SALT=0011223344556677

die() { echo "FAIL: $*" >&2; exit 1; }
[ "$(id -u)" -eq 0 ] || die "Run as root only inside a disposable test guest."
[ -x "$TOOL" ] || die "Build first: make -C tools/verity-tree"
for cmd in losetup dmsetup dd cmp mktemp awk grep; do
    command -v "$cmd" >/dev/null 2>&1 || die "Missing: $cmd"
done

TMP=$(mktemp -d /tmp/solaris-verity-test.XXXXXX) || exit 1
DATA_LOOP=""
HASH_LOOP=""
DM_NAME=""
cleanup() {
    if [ -n "$DM_NAME" ]; then dmsetup remove "$DM_NAME" >/dev/null 2>&1 || true; fi
    if [ -n "$HASH_LOOP" ]; then losetup -d "$HASH_LOOP" >/dev/null 2>&1 || true; fi
    if [ -n "$DATA_LOOP" ]; then losetup -d "$DATA_LOOP" >/dev/null 2>&1 || true; fi
    rm -rf -- "$TMP"
}
trap cleanup EXIT HUP INT TERM

attach_loop() {
    # BusyBox and util-linux compatible (no --find --show).
    LOOP=$(losetup -f) || die "No free loop device"
    losetup "$LOOP" "$1" || die "Cannot attach $1"
    printf '%s\n' "$LOOP"
}
clear_mapping() {
    if [ -n "$DM_NAME" ]; then
        dmsetup remove "$DM_NAME" || die "Cannot remove test mapping"
        DM_NAME=""
    fi
}
clear_loops() {
    if [ -n "$HASH_LOOP" ]; then losetup -d "$HASH_LOOP" || die "Cannot detach hash loop"; HASH_LOOP=""; fi
    if [ -n "$DATA_LOOP" ]; then losetup -d "$DATA_LOOP" || die "Cannot detach data loop"; DATA_LOOP=""; fi
}

# Verify the kernel BLAKE3 implementation against official BLAKE3 vectors.
"$TOOL" selftest blake3 || die "BLAKE3 upstream vector mismatch"

for ALG in sha256 blake3; do
    echo "=== $ALG: Merkle format / userspace verify / dm-verity clean and corrupt ==="
    DATA_FILE="$TMP/$ALG-data.img"
    HASH_FILE="$TMP/$ALG-tree.img"
    dd if=/dev/zero of="$DATA_FILE" bs=4096 count="$BLOCKS" >/dev/null 2>&1
    printf "SolarisPKN-Verity test data" |
        dd of="$DATA_FILE" bs=1 seek=8200 conv=notrunc >/dev/null 2>&1

    OUTPUT=$("$TOOL" format "$ALG" "$DATA_FILE" "$HASH_FILE" "$SALT") ||
        die "$ALG: tree format failed"
    printf '%s\n' "$OUTPUT"
    ROOT_HASH=$(printf '%s\n' "$OUTPUT" |
        awk '/^Root hash:/ {print $3; exit}')
    [ -n "$ROOT_HASH" ] || die "$ALG: missing root hash"
    "$TOOL" verify "$ALG" "$DATA_FILE" "$HASH_FILE" "$ROOT_HASH" "$SALT" ||
        die "$ALG: verification of intact tree failed"

    DATA_LOOP=$(attach_loop "$DATA_FILE")
    HASH_LOOP=$(attach_loop "$HASH_FILE")
    DM_NAME="spkn_"$ALG"_$$"
    TABLE=$("$TOOL" table "$ALG" "$DATA_LOOP" "$HASH_LOOP" "$BLOCKS" "$ROOT_HASH" "$SALT") ||
        die "$ALG: table failed"
    printf '%s\n' "$TABLE" | dmsetup create "$DM_NAME" ||
        die "$ALG: kernel rejected clean mapping"

    dd if="/dev/mapper/$DM_NAME" of="$TMP/$ALG-output.img" \
        bs=4096 count="$BLOCKS" >/dev/null 2>&1 ||
        die "$ALG: clean kernel read failed"
    cmp "$DATA_FILE" "$TMP/$ALG-output.img" ||
        die "$ALG: kernel returned incorrect clean data"
    echo "PASS: $ALG full clean read"

    clear_mapping
    clear_loops
    printf 'CORRUPTED!' |
        dd of="$DATA_FILE" bs=1 seek=16 conv=notrunc >/dev/null 2>&1

    if "$TOOL" verify "$ALG" "$DATA_FILE" "$HASH_FILE" "$ROOT_HASH" "$SALT"; then
        die "$ALG: userspace verifier ACCEPTED corrupted data"
    fi
    echo "PASS: $ALG userspace verifier rejects corruption"

    DATA_LOOP=$(attach_loop "$DATA_FILE")
    HASH_LOOP=$(attach_loop "$HASH_FILE")
    DM_NAME="spkn_"$ALG"_$$"
    TABLE=$("$TOOL" table "$ALG" "$DATA_LOOP" "$HASH_LOOP" "$BLOCKS" "$ROOT_HASH" "$SALT")
    printf '%s\n' "$TABLE" | dmsetup create "$DM_NAME" ||
        die "$ALG: kernel rejected mapping before read"

    ERR_FILE="$TMP/$ALG-kernel-corruption.err"
    if dd if="/dev/mapper/$DM_NAME" of=/dev/null bs=4096 count=1 >/dev/null 2>"$ERR_FILE"; then
        die "$ALG: kernel DID NOT reject corrupted block"
    fi
    # A generic dd failure (missing device, permission error, etc.) is NOT
    # evidence of verified corruption; require EIO and the dm-verity C state.
    if ! LC_ALL=C grep -Eiq 'Input/output error|I/O error' "$ERR_FILE"; then
        cat "$ERR_FILE" >&2
        die "$ALG: read failed but did not report EIO"
    fi
    DM_STATUS=$(dmsetup status "$DM_NAME") ||
        die "$ALG: cannot inspect device-mapper status"
    printf '%s\n' "$DM_STATUS"
    if ! printf '%s\n' "$DM_STATUS" | grep -Eq ' verity C([[:space:]]|$)'; then
        die "$ALG: failed read did not register dm-verity corruption"
    fi
    echo "PASS: $ALG kernel reports EIO and verity corruption state"
    clear_mapping
    clear_loops
done

echo "PASS: SHA-256 and BLAKE3 dm-verity parity on 257-block tree"
