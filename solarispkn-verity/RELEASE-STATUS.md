# Estado de publicación / Release gate (0.6.0-rc1)

This checkout is a **source-only experimental preview**. No binaries,
keys, private certificates or workplace-owned research are included.

## Probado / Verified
- Linux 6.18.52 Yocto QEMU AF_ALG: 6/6 reference hashes each on portable,
  SSE2, SSE4.1, AVX2, auto and AVX-512-requested modes (36 of 36).
- Actual SSE2, SSE4.1 and AVX2 execution markers confirmed.
- AVX-512: unsupported by QEMU guest, portable fallback confirmed; ISA execution NOT confirmed.
- New hardened SHASH + kernel return thunk candidate: KASAN/ENOMEM/recovery test PASS.
- 168 concurrent/sequential digest calculations PASS.
- Kernel mitigations retained; previous 12 objtool return warnings resolved.
- dm-verity + root PKCS#7 signed boot passed on an **earlier** module revision only.

## Faltante / Outstanding
- Full UEFI OVMF Secure Boot validation: enrolled PK/KEK/db,
  \`SecureBoot=1\`, \`SetupMode=0\`, signed UKI (kernel/initramfs/cmdline),
  authenticated dm-verity root hash and negative tests.
- Re-run signedboot and corruption of data/tree with this exact source.
- Stack-frame warnings around 2136/2240 bytes.
- Reproduce with official Yocto GCC 15.3 instead of host GCC 14.2.
- AVX-512 actual instruction execution on eligible CPU.
- ARM64/RISC-V compilation and runtime; optional NEON later.
- Streaming \`shash export\` longer than 64 bytes is explicitly
  unsupported (fail closed); further API lifecycle review required.
- No final physical CPU throughput benchmarks have been run.

**Do not represent this candidate as production secure boot or optimized
maximum throughput. No Git remote push is implied by local publication.**
