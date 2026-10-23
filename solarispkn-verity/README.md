# SolarisPKN-Verity — kernel BLAKE3 preview

**Estado / Status:** 0.6.0-rc1 — experimental source preview. **Not production ready.**

## Español

Integra BLAKE3 en la **Linux Crypto API (shash)** como módulo externo, reutilizando
**dm-verity** del propio kernel. Incluye una capa **Yocto Wrynose**,
herramientas de construcción de árboles Merkle, una CLI Python experimental
para root hash/firma y pruebas de vectores a través de AF_ALG.

- **x86-64:** SSE2, SSE4.1 y AVX2 tienen rutas ejecutadas en QEMU con 6/6
  vectores conocidos por modo, junto a la versión portable.
- **AVX-512:** el código está incluido, pero la ruta NO se ejecutó en las
  pruebas disponibles; se verificó el fallback seguro en CPU no compatible.
- **Seguridad de memoria:** la variante combinada superó un conjunto
  controlado de KASAN/ENOMEM y 168 cálculos de estrés; no garantiza ausencia
  general de errores.
- **Arranque firmado:** los tres escenarios PKCS#7 históricos probaron una
  versión anterior, NO esta combinación final. UEFI Secure Boot completo
  sigue pendiente, al igual que ARM64/RISC-V.

Esta versión es para investigación, integración y benchmarks preliminares
cuando se libere la PC; **no la uses para proteger datos de producción**.

### Organización

- \`integrations/yocto/meta-solarispkn-crypto/\`: código BLAKE3 + receta del módulo
  y tests AF_ALG (experimental, kernels Yocto Linux 6.18).
- \`integrations/yocto/meta-solarispkn-verity/\`: integración independiente de la
  herramienta Merkle/dm-verity, conservada del repositorio existente.
- \`cli/\`: utilidades Python experimentales, \`pyproject.toml\` y tests.
- \`tools/verity-tree/\`: generador Merkle basado en AF_ALG existente.
- \`docs/QA-2026-10-23.md\`: alcance de las pruebas, limitaciones y pasos pendientes.
- \`RELEASE-STATUS.md\`: requisitos antes de producción.
- \`THIRD_PARTY.md\`: procedencia y licencias.

**Compilación:** usa un kernel Linux 6.18 preparado con \`Module.symvers\`,
\`bitbake\`/Yocto y el toolchain que compila ese kernel. \`dm-verity\` requiere
un root hash autenticado y una cadena de confianza de arranque; el módulo
BLAKE3 por sí solo no la proporciona.

## English

Experimental BLAKE3 \`shash\` kernel Crypto API provider for upstream
\`dm-verity\`, with a Yocto Wrynose recipe, Merkle tooling and AF_ALG tests.
Portable, SSE2, SSE4.1 and AVX2 passed QEMU KATs; AVX-512 was compiled,
but only its unsupported-CPU fallback was exercised. KASAN/ENOMEM and
concurrent digest smoke tests passed on a bounded test suite.

This is **not an upstream Linux patch**, nor a verified UEFI Secure Boot
release; the current candidate has **not** passed end-to-end UEFI and ARM64/
RISC-V validation. Performance benchmarks on idle physical hardware are
still pending.

## Licencias / Licensing

Linux integration glue: GPL-2.0-only unless file headers state otherwise.
Bundled upstream BLAKE3 C/assembly: CC0-1.0 (license included inside recipe
\`files/LICENSE_CC0\`). Python component: see \`cli/pyproject.toml\`.
See \`THIRD_PARTY.md\` and source-level notices.
