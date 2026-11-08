# SolarisPKN-Paste — perfil SSD adaptativo, experimento 2026-11-07

**Estado: candidato experimental compilado y verificado; NO instalado.**
Compilación de referencia: D:\researchs\SolarisPKN-Paste\builds\ssd-adaptive-ui-phase-candidate\SolarisPKN-Paste.exe
Versión estable instalada, intacta: D:\Programas\SolarisPKN-Paste\SolarisPKN-Paste.exe (alpha.8).

## Problema
La cifra del fabricante SATA (480 MB/s de escritura) describe operaciones secuenciales bajo condiciones especiales, no el tiempo para copiar+flush+verificar un archivo completo con BLAKE3 y publicación segura. El tiempo reportado por Windows puede parecer superior al límite físico debido a la caché; no debe confundirse con bytes persistidos en NAND.

## Implementación
- Nuevo subcomando únicamente de laboratorio --bench run-ssd <c2e|e2c> <stream|overlap4|overlap8|native|native-direct|direct4|plan-auto>.
- SsdBenchmarkRunner.cs: valida las rutas C:\SolarisPKN-Paste-Tune-20261107 y E:\SolarisPKN-Paste-Tune-20261107 mediante .sentinel, valida 1 GiB exacto y reserva >15 GiB en C:, exige verificación BLAKE3 en el motor y SHA-256 íntegro externo y elimina el resultado temporal.
- NativeWindowsCopy.cs: COPY_FILE_NO_BUFFERING desactivado para el modo ganador (buffered). Agregado FlushToDisk antes del segundo BLAKE3. Instrumentación de tres fases: CopyFile2, FlushToDisk, BLAKE3 completo. No confundir tiempo CopyFile2 con durabilidad.
- StorageTopology.cs: triestado para la propiedad Windows StorageDeviceSeekPenaltyProperty; desconocido no significa SSD, y el motor adaptativo exige SSD confirmado en origen y destino.
- PasteEngine.cs: nuevo interruptor AdaptiveVerifiedSolidStateCopy, **false por defecto**, solo copia NUEVA de archivo regular >=1 GiB entre discos físicos distintos, ambos confirmados SSD y NTFS, sin Cut. No reemplaza los modos seleccionados explícitamente, no desactiva BLAKE3 ni staging.
- Program.cs: control opt-in en Ajustes del candidato: "SSD adaptativo experimental (archivos nuevos >=1 GiB, SSD a SSD)". Nunca activado para la alpha.8 estable.

## Pruebas, mismo archivo sintético de 1 GiB en C: y E:
Todos los modos a continuación pasaron BLAKE3 de motor y SHA256 independiente. Tiempos de motor en segundos, incluyendo flush, BLAKE3 y publicación. Medidas en esta PC; **sin aislamiento completo de caché caliente**.

| Dirección | Motor | R1 | R2 | R3 | Mediana |
|---|---|---:|---:|---:|---:|
| C: NVMe -> E: SATA | FileStream | 4.797 | 4.612 | 4.688 | 4.688 |
| C: NVMe -> E: SATA | CopyFile2 con búfer/flush | 4.178 | 3.945 | 3.813 | **3.945** |
| E: SATA -> C: NVMe | Overlapped 4 | 3.448 | 3.666 | 3.589 | 3.589 |
| E: SATA -> C: NVMe | CopyFile2 con búfer/flush | 2.610 | 2.663 | 2.698 | **2.663** |

En las primeras rondas se probaron además: Overlapped8, CopyFile2 no-buffered, NativeDirectRead4. No superaron al CopyFile2 buffered de forma inmediata. Detalle en reports/ssd-tuning-20261107/*.json.

Selección de ruta integrada PastePlan/PasteQueue, con el interruptor adaptativo activado SOLO en el benchmark:
- C→E: 3.705 y 3.934 segundos, PASS, selectedAdaptive=true.
- E→C: 2.702 y 2.722 segundos, PASS, selectedAdaptive=true.

## Desglose instrumentado nuevo (muestra de referencia R4)
C→E CopyFile2 buffered:
- Llamada CopyFile2 0.330 s (3252 MB/s APARENTE, **caché, no velocidad SATA**)
- FlushToDisk: 2.290 s (puede involucrar tanto caché como escritura real)
- BLAKE3: 1.146 s
- Total: 3.768 s; 1 GiB / (copia+flush) = ~410 MB/s aparente después del vaciado, ~85 % de los 480 MB/s anunciados. **No prueba flujo físico directo de 410 MB/s**; puede existir caché en lectura y hardware.
E→C: CopyFile2 0.333 s; flush 1.149 s; BLAKE3 1.179 s; total 2.663 s. Lectura de fuente puede venir de RAM por caché caliente.

## Validación y límites
- dotnet publish Release win-x64 self-contained: PASS.
- --selftest-headless: código de salida 0. El test interactivo Windows Forms y el impacto en ACL/ADS/EFS bajo todos los casos todavía no fueron validados en esta nueva variante.
- Las dos pruebas plan-auto de integración nuevas confirmaron selección verdadera y SHA256 íntegro, sin tocar archivos personales.
- Los directorios fuente y destino sintéticos de 1 GiB en C: y E: se eliminaron con validación de marker y de nombres exactos. Espacio C: tras limpiar 27.614.937.088 bytes libres.
- NO se modificó la alpha.8 de D:\Programas\SolarisPKN-Paste, ni el settings.json real.

## Etapas técnicas para impulsar todos los tamaños
1. Medir capacidad física fuera de caché con DiskSpd en archivo sintético de tamaño acotado, nunca contra un volumen RAW; separar copy-only, flush y BLAKE3. Controlar temperatura del SSD y SLC cache.
2. Repetir C↔E para 128 MiB, 512 MiB, 2 GiB, 10 GiB (solo E cuando C no tenga margen), y pruebas frías/calientes; 5+ rondas con orden alterno y mediana/dispersion.
3. Para archivos diminutos, habilitar sólo perfiles ganadores y reducir creación/rename/NTFS, medir archivos por segundo, IOPS, latencia P95 y CPU por núcleo. No prometer 480 MB/s sobre 6000 archivos de 4 KiB.
4. Integrar por fases bajo QA ACL, ADS, timestamps, sparse, reparse, permisos, cancelación, cortes, recuperación y lectura post-flush; no reemplazar estable sin respaldo, test funcional de la GUI y autorización.
5. Activar la selección automáticamente y permitir fallback por unidad/tamaño únicamente después de pruebas repetidas bajo cada categoría.

## Referencias técnicas
Microsoft: CopyFile2 COPY_FILE_NO_BUFFERING, recomendado para archivos muy grandes pero no ganador en este sistema.
https://learn.microsoft.com/en-us/windows/win32/api/winbase/ns-winbase-copyfile2_extended_parameters_v2
https://learn.microsoft.com/en-us/windows/win32/fileio/file-buffering
DiskSpd, modos -Su, -Sw, -L y profundidad -o:
https://github.com/microsoft/diskspd/wiki/Command-line-and-parameters
