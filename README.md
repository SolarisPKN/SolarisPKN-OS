# SolarisPKN-OS

**EspaÃ±ol** | [English](#english)

SolarisPKN-OS es un proyecto modular de investigaciÃ³n de componentes para sistemas operativos. **No es todavÃ­a una distribuciÃ³n ni una imagen lista para instalar.** Cada paquete mantiene sus propios requisitos, versiÃ³n y licencia.

## EspaÃ±ol

### Componentes

| Paquete | VersiÃ³n | Estado |
| --- | --- | --- |
| [SolarisPKN-Verity](solarispkn-verity/) | `0.6.0-rc1` | **Vista previa experimental de fuentes; no apta para producciÃ³n** |

**SolarisPKN-Verity** implementa un proveedor externo de BLAKE3 para la **Linux Crypto API (`shash`)**, utilizable por el **dm-verity original del kernel**; no reemplaza el motor de verificaciÃ³n de bloques. Incluye ademÃ¡s un generador de Ã¡rboles Merkle, una CLI Python experimental, pruebas AF_ALG e integraciÃ³n Yocto.

- **Yocto Wrynose / Linux 6.18:** capa `meta-solarispkn-crypto`, mÃ³dulo BLAKE3, SIMD x86-64 y receta AF_ALG.
- **Yocto Scarthgap:** la capa `meta-solarispkn-verity` es un esqueleto anterior de herramientas de usuario. **No se validÃ³ su combinaciÃ³n con la capa Wrynose.**
- **Pruebas documentadas:** vectores conocidos en QEMU para portable, SSE2, SSE4.1 y AVX2, ademÃ¡s de pruebas acotadas KASAN, ENOMEM y concurrencia. El cÃ³digo AVX-512 estÃ¡ incluido, pero **no se verificÃ³ su ejecuciÃ³n nativa**: solo el fallback en una CPU no compatible.
- **Pendientes:** Secure Boot UEFI completo con root hash autenticado, repeticiÃ³n de pruebas de arranque firmado sobre esta revisiÃ³n exacta, ARM64/RISC-V y benchmarks fÃ­sicos reproducibles. Las pruebas previas PKCS#7 corresponden a otra revisiÃ³n.

> **Aviso:** experimental y **no apto para producciÃ³n**. Un hash BLAKE3 dentro de dm-verity no autentica por sÃ­ solo el root hash ni la cadena Secure Boot. Superar casos puntuales no prueba ausencia de vulnerabilidades.

### DocumentaciÃ³n

- [Componente e instrucciones](solarispkn-verity/README.md)
- [Estado de publicaciÃ³n / requisitos pendientes](solarispkn-verity/RELEASE-STATUS.md)
- [QA del 23/10/2026](solarispkn-verity/docs/QA-2026-10-23.md)
- [Confianza y Secure Boot](solarispkn-verity/docs/SECURE-BOOT-TRUST.md)
- [IntegraciÃ³n Yocto](solarispkn-verity/integrations/yocto/README.md)
- [Avisos de terceros](solarispkn-verity/THIRD_PARTY.md)

En Linux, con las herramientas instaladas, se pueden comprobar metadatos y generar un archivo de **solo cÃ³digo fuente**:

```sh
make -C solarispkn-verity check package
```

Ese comando **no compila ni certifica el mÃ³dulo kernel**, ni valida Secure Boot. Las pruebas de integraciÃ³n deben realizarse en entornos descartables.

**Licencias:** integraciÃ³n Linux bajo GPL-2.0-only salvo cabeceras especÃ­ficas; cÃ³digo upstream BLAKE3 distribuido bajo CC0-1.0; consultar licencias individuales y [THIRD_PARTY.md](solarispkn-verity/THIRD_PARTY.md). No incorporar claves, datos privados o material confidencial de terceros.

---

## English

SolarisPKN-OS is a modular operating-system component research project, **not yet a complete installable OS**. Each package may use its own version, license and integration requirements.

| Package | Version | Status |
| --- | --- | --- |
| [SolarisPKN-Verity](solarispkn-verity/) | `0.6.0-rc1` | **Experimental source preview; not production-ready** |

SolarisPKN-Verity supplies an external **BLAKE3 Linux Crypto API (`shash`) provider** for **upstream dm-verity**, plus experimental Merkle-tree tooling, a Python CLI, AF_ALG checks and Yocto layers. It does **not** replace the kernel's dm-verity block target.

- The crypto integration targets **Yocto Wrynose / Linux 6.18**. An earlier userspace-tool scaffold targets **Scarthgap**; combining the layers remains unvalidated.
- QEMU known-answer tests exercised portable, SSE2, SSE4.1 and AVX2, with bounded KASAN/ENOMEM and concurrent digest checks. **Native AVX-512 instruction execution was not confirmed**; only the unsupported-CPU fallback was tested.
- **Still required:** fully verified UEFI Secure Boot with an authenticated root digest, signed-boot tests on the exact candidate, ARM64/RISC-V validation, and reproducible benchmarks on physical hardware. Earlier PKCS#7 tests covered a different revision.

**Do not use this preview to protect production systems.** A BLAKE3 provider does not by itself authenticate the root digest or establish a trusted boot chain.

Read the [component documentation](solarispkn-verity/README.md), [release status](solarispkn-verity/RELEASE-STATUS.md), [QA evidence summary](solarispkn-verity/docs/QA-2026-10-23.md), and [Secure Boot trust model](solarispkn-verity/docs/SECURE-BOOT-TRUST.md). On Linux, `make -C solarispkn-verity check package` only validates metadata and creates a source archive; it is not a kernel or secure-boot test.

Linux integration glue uses GPL-2.0-only unless noted; bundled BLAKE3 third-party sources use CC0-1.0. See [third-party notices](solarispkn-verity/THIRD_PARTY.md) and per-file headers.