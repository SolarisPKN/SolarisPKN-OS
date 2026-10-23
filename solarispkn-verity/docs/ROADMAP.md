# SolarisPKN-Verity — development plan

## v0.1 — provenance and kernel compatibility
- [x] Independent GPL-2.0-only folder under SolarisPKN-OS
- [x] Source archive packaging and upstream credits
- [x] Yocto Scarthgap layer skeleton (experimental userspace tool recipe locally added; no kernel-driver recipe)
- [ ] Review original rights and upstream licenses before copying any lab code
- [ ] Pin upstream BLAKE3 source and validate known-answer vectors
- [ ] Implement safe Linux Crypto API shash state handling, allocation and export/import
- [ ] Validate module registration on a throwaway guest with proper signing policy

## v0.2 — usable integration
- [ ] Build/install BLAKE3 kernel module with Yocto kernel headers
- [ ] Generate dm-verity-compatible SHA-256/BLAKE3 Merkle trees
- [ ] Pass clean, corrupt-block, incorrect-root, and size-boundary checks
- [ ] Build an installable Yocto recipe and QEMU test image
- [ ] Document integrity, authenticity, and verified-boot threat models

## v0.3 — distributions and performance
- [ ] Debian/Ubuntu packaging and module signing instructions
- [ ] SIMD implementations under appropriate kernel SIMD constraints
- [ ] Fair performance tests vs optimized SHA-256
- [ ] Release automation, licenses, source manifest, SBOM, reproducibility

**Scope:** dm-verity + BLAKE3 integration; **not** a new dm-verity block-verification engine.
