# SolarisPKN-Verity — verification and release plan

## Goal
Provide Linux dm-verity format-1 trees with BLAKE3 (and SHA-256 for baseline compatibility), leaving the original upstream dm-verity block-verification engine unchanged.

## Tests to run on disposable QEMU guest
1. Load experimental BLAKE3 kernel driver. Inspect /proc/crypto.
2. Confirm AF_ALG hash availability (CONFIG_CRYPTO_USER_API_HASH). Registration in /proc/crypto does NOT prove AF_ALG availability.
3. Run tools/verity-tree/solarispkn-verity-tree selftest blake3. It checks 11 official BLAKE3 vectors: 0, 1, 63, 64, 65, 1023, 1024, 1025, 2048, 4096, 8192 bytes.
4. Compare generated SHA-256 dm-verity trees and root against veritysetup v2.7.x using --no-superblock, fixed salt and 257 blocks.
5. For BOTH SHA-256 and BLAKE3 generate raw trees with the same layout, verify them in userspace, map with Linux dmsetup verity target, read all blocks, compare output and then corrupt a disposable input block. Require a kernel I/O error for corrupt blocks.
6. Expand to 1, 2, 127, 128, 129, 256, 257, 16385 data blocks for Merkle boundary coverage, varying salt and content patterns.
7. Correct and audit kernel module shash state lifetime and export/import (pointer-based context cannot be naïvely memcpy'd). Test kernel runtime behavior and memory safety.
8. Test module signing, root hash authentication and actual verified boot chain before claiming Secure Boot protection.
9. Benchmark kernel SHA256 optimized against BLAKE3 portable and SIMD on equivalent hardware and workloads.

## Unprivileged SHA-256 smoke (Linux, no root)

A separately gated first step exercises SHA-256 format/verify on 1, 2, 127, 128, 129 and 257 data blocks; tests an independently known single-block SHA-256 digest, modified data, modified tree, wrong salt, wrong root and refusal to overwrite a tree. The local-only GitHub Actions workflow runs this script after source compilation on Ubuntu. **Not a BLAKE3 or dm-verity kernel validation.**

    make -C tools/verity-tree
    sh tests/test-sha256-unprivileged.sh

See [SECURE-BOOT-TRUST.md](SECURE-BOOT-TRUST.md) for the chain of trust and QEMU+OVMF release gates.

## Suggested privileged kernel test commands (later, NOT executed in this working session)

    make -C tools/verity-tree
    tools/verity-tree/solarispkn-verity-tree selftest blake3
    sh tests/test-sha256-cryptsetup-compat.sh
    sh tests/test-verity-tree.sh

These scripts are intended for a **disposable Linux QEMU guest with root privileges**. They create and erase only their temporary images and loop/mapping devices. Review them before running.

## Poky toolchain
Minimal Poky has neither cc nor make. A new Yocto recipe is available:

    integrations/yocto/meta-solarispkn-verity/recipes-solarispkn/verity-tree/solarispkn-verity-tree_0.1.bb

Build it with BitBake only after connecting the layer and reviewing the code. To include the binary in a custom image add:

    IMAGE_INSTALL:append = " solarispkn-verity-tree"

The tool uses kernel AF_ALG; kernel configuration should include CONFIG_CRYPTO_USER_API_HASH. This is separate from CONFIG_DM_VERITY.

## Outcomes and limitations
- /proc/crypto "selftest: passed" is insufficient to claim official BLAKE3 digest correctness.
- veritysetup format --hash blake3 can fail due to OpenSSL not supporting BLAKE3. The new tool bypasses this by using AF_ALG; it does not automatically make veritysetup format BLAKE3-capable.
- Both user-side tree generator and kernel dm-verity can rely on the same faulty kernel BLAKE3 implementation. Official vectors are mandatory, and comparing against an *independent* userspace BLAKE3 implementation is recommended.
- An unauthenticated root hash can be replaced by an attacker together with the tree. Secure Boot or signed PKCS#7/UKI root policy is a separate concern.
- This code has been **written locally but not compiled, tested, audited or pushed** in this session.
