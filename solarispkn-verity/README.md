# SolarisPKN-Verity

**BLAKE3 integration for Linux dm-verity.** A separately packageable component of [SolarisPKN-OS](https://github.com/SolarisPKN/SolarisPKN-OS).

**Status:** 0.1.0-dev, initial source-packaging scaffold. **There is no ready-to-install kernel module here yet.** Do not treat this as a secure-boot or integrity-protection release.

## Purpose
Package and configure the upstream Linux **dm-verity** integrity subsystem with a BLAKE3 Kernel Crypto API provider, reproducible Merkle-tree tooling, and Yocto / Linux distribution integration. We reuse upstream dm-verity rather than writing a competing block-verifier.

An authenticated root hash and trusted boot chain remain necessary for verified boot.

## Layout
- `kernel/`: future audited BLAKE3 kernel adapter.
- `tools/`: future tree-building and configuration utilities.
- `integrations/yocto/meta-solarispkn-verity/`: Yocto Scarthgap layer skeleton, **no recipes installed yet**.
- `docs/ROADMAP.md`: planned security checks and deliverables.
- `Makefile`: standalone source packaging.

## Independent source packaging
Run from this directory:

```sh
make check
make package
```

Produces `dist/solarispkn-verity-0.1.0-dev.tar.gz`: a **source scaffold archive**, not a compiled `.ko`, RPM, or DEB. Installable packages will follow after source review and kernel tests.

## Licensing and credits
Our original integration files here: **GPL-2.0-only**, unless marked otherwise. See [LICENSE](LICENSE), [AUTHORS.md](AUTHORS.md), and [THIRD_PARTY.md](THIRD_PARTY.md). No upstream source files are bundled in this initial version.

This project does not claim authorship of the Linux kernel or BLAKE3 algorithm. Experimental code or artifacts from employer-owned labs are not copied into this public scaffold.
