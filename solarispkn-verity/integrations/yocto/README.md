# Yocto integration / Integración Yocto

**Experimental:** this repository currently contains two distinct layers
at different compatibility levels; do not combine them into a single
unreviewed production image.

1. **meta-solarispkn-crypto** targets Yocto **Wrynose** (Linux 6.18).
   Its kernel BLAKE3 Crypto API module bundles CC0 upstream portable
   code, SSE2/SSE4.1/AVX2 and AVX-512 (opt-in), plus an optional AF_ALG
   known-answer test recipe.
2. **meta-solarispkn-verity** is the existing **Scarthgap** scaffold,
   with experimental userspace Merkle tooling. The different
   LAYERSERIES_COMPAT declarations must be reconciled/tested before use
   in the same Yocto build.

For a disposable Wrynose build, add only the crypto layer first:

    bitbake-layers add-layer /path/to/solarispkn-verity/integrations/yocto/meta-solarispkn-crypto
    bitbake blake3-solarispkn

The normal distribution must provide a compatible kernel source,
configuration and **Module.symvers**. Use the same compiler/toolchain
as the kernel. BLAKE3 shash does not replace upstream dm-verity.
Do not install this candidate into any production boot chain.

The test recipe is optional:

    bitbake solarispkn-afalg-test

Complete end-to-end Secure Boot with authenticated root hash,
UEFI/OVMF signed UKI and negative tests has NOT yet been validated
for this candidate. See ../../docs/QA-2026-10-23.md and
../../RELEASE-STATUS.md.
