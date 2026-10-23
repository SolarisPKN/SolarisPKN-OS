#!/bin/sh
# SPDX-License-Identifier: GPL-2.0-only
# Unprivileged, SHA-256-only smoke test. No loop, dmsetup, sudo, or root required.
# All writable paths are created under one private temporary directory.
set -eu
umask 077
LC_ALL=C
export LC_ALL

HERE=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
ROOT=$(CDPATH= cd -- "$HERE/.." && pwd)
TOOL="$ROOT/tools/verity-tree/solarispkn-verity-tree"
die() { printf 'FAIL: %s\n' "$*" >&2; exit 1; }
[ -x "$TOOL" ] || die "Compile with: make -C tools/verity-tree"
for cmd in dd mktemp awk sha256sum; do
    command -v "$cmd" >/dev/null 2>&1 || die "Missing $cmd"
done

TMP=$(mktemp -d "${TMPDIR:-/tmp}/spkn-verity-smoke.XXXXXXXX") || die "Cannot create temporary directory"
cleanup() { rm -rf -- "$TMP"; }
trap cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM

for BLOCKS in 1 2 127 128 129 257; do
    DATA="$TMP/data-$BLOCKS.img"
    TREE="$TMP/tree-$BLOCKS.img"
    SALT=0011223344556677
    if [ "$BLOCKS" -eq 1 ]; then SALT=-; fi

    dd if=/dev/zero of="$DATA" bs=4096 count="$BLOCKS" 2>/dev/null ||
        die "Cannot create $BLOCKS-block input"
    OUTPUT=$("$TOOL" format sha256 "$DATA" "$TREE" "$SALT") ||
        die "format failed for $BLOCKS blocks"
    ROOT_HASH=$(printf '%s\n' "$OUTPUT" | awk '/^Root hash:/ {print $3; exit}')
    [ "${#ROOT_HASH}" -eq 64 ] || die "Invalid root hash length for $BLOCKS blocks"
    "$TOOL" verify sha256 "$DATA" "$TREE" "$ROOT_HASH" "$SALT" >/dev/null ||
        die "valid tree rejected for $BLOCKS blocks"

    # Independent digest oracle for the single-block / empty-salt case.
    if [ "$BLOCKS" -eq 1 ]; then
        EXPECTED=$(sha256sum "$DATA" | awk '{print $1}')
        [ "$ROOT_HASH" = "$EXPECTED" ] || die "single-block root disagrees with sha256sum"
    fi

    # The format operation must never overwrite an existing hash tree.
    if "$TOOL" format sha256 "$DATA" "$TREE" "$SALT" >/dev/null 2>&1; then
        die "format overwrote the existing hash tree ($BLOCKS blocks)"
    fi

    # A caller-supplied different root must be rejected.
    case "$ROOT_HASH" in
        0*) BAD_ROOT="1${ROOT_HASH#?}" ;;
        *)  BAD_ROOT="0${ROOT_HASH#?}" ;;
    esac
    if "$TOOL" verify sha256 "$DATA" "$TREE" "$BAD_ROOT" "$SALT" >/dev/null 2>&1; then
        die "wrong trusted root accepted ($BLOCKS blocks)"
    fi

    # The salt is integrity-critical, including at Merkle boundary sizes.
    if [ "$SALT" != "-" ]; then
        if "$TOOL" verify sha256 "$DATA" "$TREE" "$ROOT_HASH" - >/dev/null 2>&1; then
            die "incorrect salt accepted ($BLOCKS blocks)"
        fi
    fi

    # Mutate data after formatting and check rejection, then restore it.
    printf '\001' | dd of="$DATA" bs=1 seek=0 conv=notrunc 2>/dev/null ||
        die "Cannot corrupt test data"
    if "$TOOL" verify sha256 "$DATA" "$TREE" "$ROOT_HASH" "$SALT" >/dev/null 2>&1; then
        die "corrupted data accepted ($BLOCKS blocks)"
    fi
    printf '\000' | dd of="$DATA" bs=1 seek=0 conv=notrunc 2>/dev/null ||
        die "Cannot restore test data"
    "$TOOL" verify sha256 "$DATA" "$TREE" "$ROOT_HASH" "$SALT" >/dev/null ||
        die "restored data rejected ($BLOCKS blocks)"

    # The single-block special case has no tree: test tree tampering only when >1.
    if [ "$BLOCKS" -gt 1 ]; then
        dd if=/dev/zero of="$TREE" bs=4096 count=1 conv=notrunc 2>/dev/null ||
            die "Cannot corrupt test tree"
        if "$TOOL" verify sha256 "$DATA" "$TREE" "$ROOT_HASH" "$SALT" >/dev/null 2>&1; then
            die "corrupted Merkle tree accepted ($BLOCKS blocks)"
        fi
    fi
    printf 'PASS: SHA-256 userspace boundary=%s blocks\n' "$BLOCKS"
done
printf 'PASS: unprivileged SHA-256 smoke suite (no kernel BLAKE3 claim)\n'
