# Third-party provenance and licensing

**No source files from either project are copied into this initial scaffold.**

| Component | Source | License and intended use |
|---|---|---|
| Linux dm-verity | https://github.com/torvalds/linux/tree/master/drivers/md | Linux GPL-2.0-only, subject to individual file SPDX headers; reuse the active kernel's dm-verity |
| BLAKE3 reference C code | https://github.com/BLAKE3-team/BLAKE3 | Upstream offers CC0-1.0 and Apache-2.0 options; pin an upstream revision and choose the compatible licensing option before vendoring |
| Yocto / OpenEmbedded | https://git.openembedded.org/openembedded-core/ | Multiple licenses depending on component; build system is a dependency rather than bundled source |

Repository links are credits, **not a substitute for applicable license notices**. On vendoring upstream code, include exact revision, notices, license texts, modifications, and source checksums. Do not automatically publish employer-owned experimental work.
