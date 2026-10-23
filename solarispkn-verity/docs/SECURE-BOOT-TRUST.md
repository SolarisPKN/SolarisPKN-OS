# SolarisPKN-Verity: Secure Boot and the trusted root hash

Status: **design and threat model only**. No end-to-end verified-boot claim is made.

## Separate roles

- **UEFI Secure Boot** authorizes EFI executables in the boot chain using
  signature verification (and, where configured, EFI image hash allowances).
  It does **not** rehash every root-filesystem block at startup.
- **Linux dm-verity** verifies requested blocks against a Merkle tree and a
  *trusted root digest*. Our experimental integration proposes BLAKE3 as the
  digest for this tree, using the kernel Crypto API while retaining the
  upstream dm-verity block target.
- **Authenticity of the root digest** is a separate requirement. Changing
  SHA-256 to BLAKE3 inside dm-verity does not change the firmware's signature
  algorithms or disable Secure Boot, but it also does not automatically bind
  dm-verity to Secure Boot.

The trusted path we must demonstrate is:

    UEFI secure boot policy
       -> authenticated EFI boot application / signed UKI
       -> authenticated kernel + initramfs + boot configuration
       -> authenticated dm-verity root digest + enforced root= mapping
       -> upstream dm-verity with a verified BLAKE3 kernel hash provider
       -> verified root filesystem blocks

**Design candidate:** a signed Unified Kernel Image (UKI) containing the
kernel, initramfs and trusted dm-verity root configuration. The implementation
must prove that alternative, unsigned command lines, initramfs components or
boot entries cannot bypass the selected integrity policy. Merely storing the
root hash in an editable file under /boot is **not** sufficient.

## Distinct security requirements

1. The kernel must contain dm-verity, the correct Crypto API plumbing, and an
   independently validated BLAKE3 implementation. A kernel module must satisfy
   the platform's module-signature and lockdown policy when these are enforced.
2. The tree builder and kernel verifier must agree on dm-verity version 1,
   4096-byte blocks, digest length, salt order (salt || block), and layout.
3. The expected root hash must come from authenticated, release-controlled
   material; it must not be recomputed from writable disk content on boot.
4. Boot parameters and initramfs must **enforce** dm-verity and refuse fallback
   to an unverified writable root when integrity validation fails.
5. The signing chain and recovery procedure must be documented. Secure Boot
   alone does not prevent a signed older release from booting (rollback).
   Anti-rollback requires a **separate** version/counter policy.
6. dm-verity does not encrypt the disk, protect writable data partitions, or
   guarantee runtime memory integrity. Treat these as independent controls.

## Release-gating tests in disposable QEMU + OVMF

- [ ] Confirm Secure Boot is actually enabled in the guest with appropriate
      keys, and verify the intended signed UKI is authorized.
- [ ] Prove the guest boots with a known-good kernel and known-good root digest
      and that the root filesystem is mounted through the verified mapping.
- [ ] Verify clean reads using **BLAKE3**, after checking independent official
      vectors. A SHA-256-only passing test is not a BLAKE3 proof.
- [ ] Tamper with one data block offline; demand a verified-mapping read error,
      not merely a userspace hash mismatch.
- [ ] Tamper with one hash-tree block; demand a mapping read error.
- [ ] Replace the expected root digest with an unauthenticated alternative;
      the enforced signed boot configuration must reject it.
- [ ] Alter the signed UKI; Secure Boot must refuse the altered image.
- [ ] Try an alternative unsigned boot configuration; it must not quietly
      select an unverified root filesystem.
- [ ] Check module loading and signature enforcement on the actual guest
      kernel, not only module metadata.
- [ ] Test recovery and a signed older image against the documented rollback
      policy (if anti-rollback is claimed).

## What may be said today

Supported **design statement**: BLAKE3 can be integrated with dm-verity without
changing firmware Secure Boot hashing/signing, provided the kernel implements
it correctly and the root digest is bound to an authenticated boot chain.

**Not yet proven:** working BLAKE3 dm-verity mapping, kernel-module signing,
an authenticated root hash, a Secure Boot-enforced root mapping, QEMU/OVMF
end-to-end success, resistance to rollback, or performance improvements.

## Notes

This is SolarisPKN's private-project engineering work and must not incorporate
code, binaries, keys, or confidential results from an employer's Yocto lab.
