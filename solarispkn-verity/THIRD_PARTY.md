# Third-party provenance / Procedencia

- Linux dm-verity, Crypto API and kernel tooling: Linux kernel,
  GPL-2.0-only or file-specific SPDX; referenced, **not vendored**.
  https://git.kernel.org/pub/scm/linux/kernel/git/torvalds/linux.git
- BLAKE3 C/assembly: upstream BLAKE3 project version 1.8.7, CC0-1.0
  option, bundled in the Yocto module recipe with \`LICENSE_CC0\`.
  https://github.com/BLAKE3-team/BLAKE3
- SolarisPKN BLAKE3 \`shash\` kernel adapter, dispatch integration and
  module/Yocto glue: independent experimental GPL-2.0-only code.
- Yocto / OpenEmbedded: external build dependency, no upstream sources
  copied here.
- Python Merkle/PKCS7 CLI: independent experimental package; see
  \`cli/pyproject.toml\`.

**No workplace-owned reverse engineering source or data is included.**
BLAKE3 upstream ownership and licenses remain with their respective authors.
