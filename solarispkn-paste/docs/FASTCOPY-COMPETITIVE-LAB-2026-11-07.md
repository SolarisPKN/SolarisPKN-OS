# SolarisPKN-Paste — Laboratorio competitivo contra FastCopy 5.12

Fecha: 2026-11-07 (Argentina). Versión instalada: 0.2.0-alpha.8. Motor experimental nuevo: no instalado, código local en el repositorio.

## Objetivo y contexto

Superar el rendimiento de FastCopy sin sacrificar verificación BLAKE3, temporales antes de la publicación, recuperación ni metadatos. Hardware: Intel i3-10105 (4C/8T), 40 GiB RAM, D: Toshiba HDD NTFS cluster 64 KiB, E: Gigabyte SSD NTFS cluster 4 KiB, C: WD SN530 NVMe NTFS cluster 4 KiB. El equipo estaba siendo utilizado, sin vaciar caches ni desactivar Defender.

FastCopy 5.12 ya usa varias optimizaciones relevantes: overlapped y direct I/O, diferenciación entre mismos o diferentes HDD, buffering para archivos pequeños, preasignación, verificación convencional con relectura del destino y modo perfect verify que re-lee origen/destino (Pro). No es razonable prometer superioridad absoluta aplicando una sola opción.

Referencias:
https://fastcopy.jp/help/fastcopy_eng.htm
https://fastcopy.jp/
https://learn.microsoft.com/en-us/troubleshoot/windows-client/performance/troubleshooting-slow-file-copying-in-windows
https://devblogs.microsoft.com/dotnet/file-io-improvements-in-dotnet-6/

## Datos y benchmarks principales

Dataset: 6.000 archivos sintéticos de 4–16 KiB, 61.440.000 bytes lógicos, D: HDD -> E: SSD, espacio de prueba protegido por sentinel, verificación BLAKE3 del origen y del temporal antes de publicar en Solaris.

| Motor/estrategia | Corrida(s), s | Integridad |
|---|---:|---|
| Solaris actual: 1 lector origen + 4 escritores/verificadores, handle reutilizado | 18,288 / 18,421 | BLAKE3 PASS |
| Solaris experimental: 4 lectores origen + 4 escritores/verificadores | 17,968 / 17,878 | BLAKE3 PASS |
| Solaris experimental: 2 lectores | 18,540 | BLAKE3 PASS |
| Solaris experimental: 1 lector + 4 escritores + 2 verificadores separados | 19,494 | BLAKE3 PASS |
| Solaris experimental: 1 lector + 4 escritores + 4 verificadores separados | 19,551 | BLAKE3 PASS |
| FastCopy 5.12: noexist_only /verify | 19,950 / 18,620 | VerifyFiles 6000, error 0, exit 0; 48 hashes SHA-256 extra correctos (fuera de cronómetro) |

Promedios de dos corridas: Solaris default **18,355 s**, Solaris cuatro lectores **17,923 s**, FastCopy **19,285 s**. En estas pasadas Solaris predeterminado finalizó aproximadamente 4,8 % antes que FastCopy y el experimental de cuatro lectores un 7,1 % antes. No es victoria universal: FastCopy tiene hash/semántica de temporales diferente, las condiciones de caché son cambiantes, solo hay dos pasadas y en pruebas anteriores FastCopy llegó a ~16,3 s. Comparar solamente la transferencia sin verificación sería metodológicamente incorrecto.

FastCopy usó su modo convencional de verificación (el log acredita Diff No overwrite with Verify, VerifyFiles=6000, No Errors). En la edición normal la verificación convencional vuelve a leer el destino; el modo Perfect Verify relee ambos y es Pro. En Solaris se calcula BLAKE3 del origen durante lectura y se verifica el temporal antes de publicar. El balance de seguridad es diferente aunque ambos verifican bytes de datos.

## Optimización 1: tres etapas independientes

Nuevo archivo src/SmallFilePipelineSplit.cs: productor ordenado del HDD, cola de escritores de temporales SSD y cola de verificadores BLAKE3 independientes. Memoria y staging acotados mediante Channel; cancelación y limpieza de archivos no publicados.

Resultado: **más lento** que el pipeline previo (19,49–19,55 s frente a ~18,35 s). Se mantiene opt-in mediante la propiedad SplitSmallFilePipeline=false por defecto. No instalar en producción.

## Optimización 2: 2/4 lectores del origen

Se agregó propiedad experimental SmallFileSourceReaders, valor por defecto 1, permitiendo 2 o 4 productores con índice atómico. Para HDD D: a E: SSD, cuatro lectores rondaron ~17,9 s, un cambio menor al de la variabilidad del sistema y con riesgo de aumentar movimientos de cabeza con caché fría. Se mantiene un lector por defecto.

## Falsa mejora detectada por caché en C NVMe -> E SSD

Se creó el mismo dataset sintético de 61 MB en C:, con preflight >=15 GiB libres y sentinel. Tres ensayos:
- Un lector, primera pasada: 43,980 s.
- Cuatro lectores, siguiente pasada: 19,853 s.
- Un lector, pasada posterior: 19,844 s.

Una aparente aceleración de más del doble con cuatro lectores desapareció al repetir el modo de un lector. El orden/caché y actividad de Windows dominaban el primer resultado. NO activar automáticamente cuatro lectores en SSD o HDD sobre la base de esa comparación.

El primer intento de benchmark fue bloqueado por una ruta C: mal construida, antes de copiar datos; se corrigió la ruta absoluta después de validar sentinel.

## Integridad, seguridad y limpieza

- Build experimental: **60 PASS / 0 FAIL**, incluyendo tests de tres etapas con hash inválido y tests de 2/4 lectores con 256 archivos y verificación completa.
- El binario instalado 0.2.0-alpha.8 sigue sin cambios y conserva GUI Visual a 250 ms, Ultra a 5 s, BLAKE3 y recuperación.
- No se modificaron Defender, políticas de I/O, driver ni modo kernel.
- Los datasets de C:, D: y E: se eliminaron mediante la lógica de sandbox con sentinel. C: final sobre 23 GiB libres.
- Reportes JSON y logs quedan en D:\researchs\SolarisPKN-Paste\reports, dentro del repositorio el código experimental sin commit ni push.

## Siguientes vías con potencial

1. ETW/WPA: medir tiempo y operaciones NTFS FileIo, DiskIo, hilos, caché, CPU e interacción con filtros antes de modificar algoritmos. Los 6000 archivos imponen latencia de metadatos importante.
2. Archivos grandes: implementar y probar overlapped I/O / IOCP con profundidad de cola configurable y bloques de 1–2 MiB manteniendo streaming BLAKE3 y staging, frente a FastCopy 2 MiB / 8 overlapped (según documentación).
3. Reutilización de handles y reducción de operaciones de metadata: optimizar solo lo que las trazas identifiquen; verificar ACL, ADS, fechas y EFS.
4. Adaptación por dispositivo real y caché fría/caliente: no inferir desde una sola pasada; 3–5 pasadas alternadas y sin desactivar protecciones.

Conclusión: en este lote concreto Solaris ya puede ganar a FastCopy con verificación; el trabajo adicional propuesto no justifica activar los experimentos por defecto. Mantener criterio medido y verificable.
