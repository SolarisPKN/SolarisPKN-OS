# SolarisPKN-Verity — compatibilidad e integridad (ES)

**Versión documentada:** `0.6.0-rc1` · **Estado:** experimental, sin certificación para producción · **Actualización:** 2026-10-26

[English version](INTEGRATION-AND-REGRESSIONS.en.md) · [Estado de publicación](../RELEASE-STATUS.md) · [Pruebas ejecutables](../cli/tests/test_verity_regressions.py)

> **Alcance:** este documento trata únicamente de **funcionalidad, integración y corrección de errores**. No incluye benchmarks, métricas de velocidad ni archivos de pruebas privadas. El código fuente de BLAKE3 se integra como proveedor de Linux Crypto API; **no es un algoritmo nuevo ni un fork de dm-verity**.

![Flujo de integridad: datos, árbol Merkle y raíz autenticada](assets/merkle-integrity.svg)

## 1. Arquitectura: no hace falta modificar dm-verity

```text
Imagen EXT4 inmutable (bloques de 4096 B)
    │
    ├── Generador compatible con dm-verity v1
    │       H(salt || bloque) → hashes hoja → niveles Merkle → root hash
    │
    └── Dispositivo de datos ─┐
                               ├─ dm-verity original (kernel)
Árbol Merkle / dispositivo hash ┘       │
              Linux Crypto API: blake3 (shash) o sha256
                                        │
                         Ext4 verificado en solo lectura
```

Una raíz Merkle **no se autentica a sí misma**: el arranque tiene que obtenerla por una cadena de confianza (por ejemplo, una firma PKCS#7 validada contra una clave confiable). El laboratorio pudo exigir una firma, pero **no equivale a haber validado UEFI Secure Boot integral**.

### Contrato del árbol

| Propiedad | Decisión implementada | Motivo |
|---|---|---|
| Formato | dm-verity v1 | Interoperar con el verificador del kernel |
| Bloques de datos y hash | 4096 bytes | Evitar inconsistencias de geometría y montaje |
| Digest | 32 bytes | 128 entradas digest por bloque hash |
| Hash | `H(salt || block)` | Mantener el orden esperado por dm-verity |
| Niveles | Raíz primero en el archivo del árbol | Geometría requerida por la construcción usada |
| Integridad negativa | No aceptar árbol, raíz o datos alterados | Evitar aceptación silenciosa |

El generador exige tamaño de imagen múltiplo de 4096 bytes. El manifiesto local puede contener rutas absolutas: **no publiques sus originales**, ni la imagen ni sus certificados/firmas privadas.

## 2. Problemas reales, correcciones y estado

| Problema observado | Causa confirmada o alcance del diagnóstico | Corrección / resolución | Estado |
|---|---|---|---|
| Árbol Merkle correcto pero EXT4 no montaba | EXT4 generado con bloques de **1024 B** frente al dispositivo dm-verity de **4096 B** | Regenerar la imagen EXT4 con bloques coherentes de 4096 B, recalcular todos los hashes y repetir el montaje `ro,noload` | **Verificado** en laboratorio |
| Integración inicial dependía de un proveedor `ahash` externo | Prototipo anterior diseñado para otro kernel y con proveedor experimental | Crear proveedor propio de **Crypto API `shash`**, empaquetado como módulo externo para Linux 6.18, y consumirlo desde dm-verity sin parchearlo | **Verificado** en QEMU, experimental |
| Camino de instrucciones SIMD y CPU virtual heterogénea | Algunas instrucciones no están anunciadas en CPUID virtual | Separar implementación portable/SSE2/SSE4.1/AVX2 y aplicar selección/fallback; la opción AVX-512 solicitada **no ejecutó instrucciones AVX-512**, solo fallback seguro | **SSE2, SSE4.1 y AVX2 ejecutados**; AVX-512 pendiente |
| Advertencias de mitigación `objtool` por retorno | La integración ensamblador/kernel requería coherencia con el mecanismo `return thunk` | Corregir la integración del código con las mitigaciones conservadas y volver a compilar/inspeccionar con `objtool` | **Advertencias previamente observadas resueltas** en candidato probado |
| Fallo de asignación de memoria inducido | Prueba de inyección ENOMEM controlada, no necesariamente defecto criptográfico | Manejar el error y verificar recuperación; ejecutar variante de QA bajo KASAN | **PASS** en alcance acotado; no prueba ausencia universal de bugs |
| Primera sesión KFENCE marcada FAIL | Problema de lectura/quoting del script de inspección; no se demostró corrupción de memoria del módulo | Corregir la instrumentación y repetir la prueba | **75/75 ciclos PASS** en ejecución posterior |
| Primer kernel de depuración KASAN no arrancaba | Fallo del entorno/kernel de depuración; causalidad criptográfica no demostrada | Ajustar el laboratorio de depuración y repetir | **Prueba controlada KASAN/ENOMEM PASS**, cobertura limitada |
| Raíz read-only: `Permission denied` en `/` | `fstab` y configuración SysVinit solicitaban acceso RW a la raíz protegida | Apuntar `/` al mapper verificado y usar configuración `read-only-rootfs` y montaje solo lectura | **Solución funcional de laboratorio anterior** |
| `S1 respawning too fast` y fallo de semilla `urandom` | Getty de `ttyS1` inexistente y ubicación de semilla en raíz read-only | Conservar `ttyS0`, retirar entrada `S1`; usar `/run` en laboratorio | **Laboratorio**; la semilla en tmpfs **no persiste**, no es solución de producción |
| Raíz inválida/firma inválida | El árbol o el root hash no coinciden; la firma no se valida | Detener el montaje verificado, sin fallback a raíz cruda | **Rechazo observado** en pruebas negativas |
| `shash export` con contexto superior a 64 B | Restricción explícita del proveedor experimental | Rechazar la operación fuera de límites en vez de truncar/aceptar silenciosamente | **Limitación conocida**, revisión API pendiente |
| `meta-solarispkn-verity` de Scarthgap vs capa crypto Wrynose | `LAYERSERIES_COMPAT` de series distintas | Mantener capas separadas; usar primero `meta-solarispkn-crypto` en Wrynose | **Integración entre ambas series pendiente** |

**Importante:** para una explicación histórica distinguimos el prototipo `ahash` antiguo de la distribución pública actual `shash`. No se afirma que todos los arreglos de arranque de la versión 0.5 estén revalidados íntegramente en la versión 0.6.0-rc1.

## 3. Evidencia funcional (sin benchmarks)

| Comprobación | Resultado del laboratorio | Qué demuestra y qué no |
|---|---|---|
| Vectores conocidos BLAKE3 en Linux Crypto API | **6/6** por modo solicitado: portable, SSE2, SSE4.1, AVX2, auto y AVX-512 solicitado | Correspondencia de digest; AVX-512 se validó **solo por fallback**, no ejecución real |
| Ciclo de vida `shash` | Marcadores de inicialización, reutilización, import/export, reinicialización y limpieza: **PASS** | Comportamiento de los caminos ejercitados |
| KASAN + ENOMEM inyectado | **PASS** en la repetición corregida; sin avisos KASAN observados | No equivale a análisis exhaustivo |
| KFENCE tras corregir el test | **75/75** ciclos sin error KFENCE reportado | Cobertura acotada, no garantía formal |
| Árbol con SHA-256 y BLAKE3 | Validación de niveles/root y rechazo de datos/árbol alterados: **PASS** en el laboratorio | Valida formato e integridad del caso probado |
| Arranque BLAKE3 con firma obligatoria (laboratorio) | Imagen intacta montó; firma de hash incorrecta y falta de exigencia de firma fueron rechazadas | **No** certifica UEFI Secure Boot |
| dm-verity BLAKE3: 1 GiB y 512 MiB | **6/6**: dos imágenes intactas llegaron a login; corrupción de datos o árbol en cuatro casos produjo error de device-mapper y **no** llegó a login | Pruebas controladas en TCG; no prueba toda la cadena de confianza ni la exactitud de cada revisión del módulo |
| Corrupción posterior al montaje, múltiples arquitecturas | **Pendiente** | Necesario para mayor cobertura |

El proyecto está en fase **experimental**. Una prueba funcional que termina bien no demuestra resistencia ante todos los fallos ni autorización segura de arranque.

## 4. Reproducir el árbol Merkle sin dispositivos privilegiados

En un entorno de prueba aislado, con Python 3, desde `solarispkn-verity/`:

```bash
python3 -m unittest discover -s cli/tests -p 'test_verity*.py' -v
```

Para las pruebas BLAKE3 del generador se necesita la dependencia Python `blake3`; si falta, el test existente informa `SKIP`. **No se debe contabilizar un SKIP como PASS**.

Ejemplo con una imagen de **prueba**, nunca sobre la raíz activa:

```bash
# Debe tratarse de una copia inmutable de tamaño múltiplo de 4096.
python3 cli/tools/verity_image.py build \
  --image demo.img --tree demo.tree --manifest demo.json \
  --algorithm blake3

python3 cli/tools/verity_image.py verify \
  --image demo.img --tree demo.tree --manifest demo.json
```

El comando `table` solo imprime la tabla de mapeo: **no crea dispositivos ni monta nada**. `sign-root` y `verify-signature` usan OpenSSL y material de clave local provisto por el operador; **no agregues claves ni certificados a Git**.

## 5. Requisitos antes de producción

- Reproducir en la **versión exacta** del módulo publicado los tests de firma, rechazo de datos/árbol y montaje real.
- Probar lectura posterior al montaje de bloques modificados; exigir fallo cerrado sin devolver datos dañados.
- Revisar y ejercer `shash` export/import y estados fuera de rango.
- Compilar con toolchain oficial Yocto del kernel objetivo y comprobar cambios de API/ABI, símbolos y mitigaciones.
- Ejecutar AVX-512 en hardware compatible, además de ampliar a ARM64/RISC-V cuando corresponda.
- Validar Secure Boot UEFI/OVMF completo con kernel, initramfs y línea de arranque autenticados.
- Revisar licencias, atribuciones y publicación independiente de cualquier recurso de terceros.

**Regla editorial:** el repositorio público contiene solo **código y pruebas funcionales reproducibles**. Rendimiento, datos de cargas de CPU/RAM/disco y mediciones comparativas pertenecen a una investigación separada y no forman parte de esta documentación.
