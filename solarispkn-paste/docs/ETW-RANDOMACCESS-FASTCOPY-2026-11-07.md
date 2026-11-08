# SolarisPKN-Paste — ETW, RandomAccess y FastCopy

Fecha: 2026-11-07 ART. Estado: investigación experimental local, sin instalar en aplicación 0.2.0-alpha.8.

## Entorno, límites y metodología

CPU i3-10105 (4C/8T), 40 GiB de RAM; D Toshiba HDD NTFS, E Gigabyte SSD NTFS, C WD NVMe. El equipo estaba en uso, sin controlar caches, antivirus o actividades externas. Los benchmarks usan sandboxes acotados y marcados, no datos del usuario. Las mediciones de FastCopy y Solaris incluyen verificación de destino, pero usan algoritmos/semánticas de publicación diferentes.

## ETW Windows Performance Recorder integrado

WPR DiskIO+FileIO durante 52 segundos (transferencia sintética Solaris 6.000 archivos D->E, 61.440.000 bytes). Total 3.632.191 eventos, 0 perdidos. Conteos de TODO Windows, NO solo del proceso Solaris: 6.044 Rename y otros 6.044 RenamePath; 73.174 FileIo Create; 12.158 FileCreate; 129.633 QueryInfo; 12.222 SetInfo; 80.510 Close; 68.268 Cleanup; 24.033 FileIo Read; 14.554 FileIo Write; 1.602 DiskIo Read y 7.128 DiskIo Write. Cifras agregadas no bastan para causalidad ni tasas de Solaris. Raw ETL 772 MiB y PDBs ELIMINADOS tras extraer resumen. Solo queda JSON sanitizado D:\researchs\SolarisPKN-Paste\etw\20261107\tiny-6000-sanitized-summary.json. WPR detenido.

## Experimento archivos grandes con E/S offsets asíncrona

Nuevo src/OverlappedCopyEngine.cs usa RandomAccess.ReadAsync y WriteAsync con 2–8 operaciones pendientes, buffers acotados, hash BLAKE3 de origen en orden, flush a disco, comprobación BLAKE3 del archivo temporal antes de publicar, y espera de operaciones pendientes ante fallo/cancelación. PasteOptions.RandomAccessOverlapped permanece FALSE por defecto. No se cambió el ejecutable instalado.

Un archivo de 1GiB D HDD->E SSD, caché potencialmente caliente:
- FileStream original: 5,308 y 4,908 s.
- Overlap 2 operaciones, chunk 2MiB: 5,304s.
- Overlap 4 operaciones, chunk 2MiB: 4,927s.
- Overlap 8 operaciones, chunk 2MiB: 4,872s.
No hay ventaja robusta en 1GiB.

10.000.000.000 bytes D HDD -> E SSD, tiempo total con BLAKE3 y publicación verificada:
- Motor FileStream: 98,268 y 91,829 s; media 95,049 s.
- Motor RandomAccess overlap 4/chunk 2MiB: 89,210 y 89,731 s; media 89,471 s.
La variante experimental redujo el promedio 5,9% en estas dos pasadas. No universal.

## FastCopy 5.12: el competidor gana en 10GB

FastCopy 5.12, CLI noexist_only /verify, D HDD -> E SSD nuevo, registro del propio FastCopy: TotalTime 01:13; VerifyFiles=1, VerifyRead=9536MiB, No Errors. Cronómetro externo completo 74,468 s, incluyó copia y verify de FastCopy. Después se hizo SHA256 COMPLETO extra e independiente de origen y destino, concordantes: 475,905 s adicionales, explícitamente FUERA DEL CRONÓMETRO de copia. Evidencia: D:\researchs\SolarisPKN-Paste\reports\benchmark-20261107-large-fastcopy-verify.json y fastcopy-10gb-verified.log. FastCopy sigue siendo más rápido que el nuevo motor en este gran archivo.

## Metadatos de archivos pequeños

FastMetadataPath=false por defecto: omite consultas File.Exists y Directory.Exists redundantes, conservando File.Move(overwrite:false) que rechaza colisiones de modo atómico. Prueba de carrera forzada sin sobrescritura de tercero. 6000 archivos D->E original 19,802 / 19,948 s, experimental 19,944 / 19,362 s. Variación comparable a ganancia; no activar por defecto todavía.

SmallFileHandleTimestamps=false por defecto: establece la fecha de modificación a través del SafeFileHandle ya abierto en el mismo temporal, evitando nueva apertura/close por ruta. Pruebas de 256 archivos comprobaron LastWriteTimeUtc exacto y todos hashes BLAKE3, sin temporales residuales. Incluido en 75/75 tests SYSTEM. A/B en 6000 archivos pendiente.

## BLAKE3 paralelo

La dependencia existente Blake3 3.0.2 dispone de Hasher.UpdateWithJoin. Nuevo flag ParallelBlake3OnLarge=false por defecto: origen y destino pueden usar cálculo paralelo del MISMO hash canónico. 83 PASS / 0 FAIL incluyendo 1MiB +/-1 byte, 9MiB, integración completa y rechazo de hash incorrecto.

1GiB D->E:
- Overlap d4/2MiB con BLAKE3 serial: 4,927 s, de corrida anterior.
- Overlap d4/2MiB con BLAKE3 paralelo: 9,236 s, PEOR.
- Overlap d4/4MiB con BLAKE3 serial: 5,344 s.
- Overlap d4/4MiB con BLAKE3 paralelo: 4,363 s una pasada.
- La segunda corrida de paralelo con 4MiB tardó 20,251 s, pero se superpuso con otro benchmark de 6000 archivos de otro proceso en los mismos discos; ESTA MEDICIÓN SE INVALIDA.

No activar de forma predeterminada sin repetir en condiciones aisladas, medir CPU/throughput del archivo 10GB y verificar preservación de metadatos y cancelación.

## Prueba aislada de BLAKE3 paralelo en archivo de 10 GB

Una primera transferencia de 10.000.000.000 bytes mediante RandomAccess depth=4, chunk=4MiB y Hasher.UpdateWithJoin (origen y destino), manteniendo FlushToDisk y BLAKE3 verificado antes de publicar, terminó en **85,379 s (PASS)**. Referencia: Overlap depth=4, chunk=2MiB y BLAKE3 serial 89,210 / 89,731 s (media 89,471); FileStream serial 98,268 / 91,829 s (media 95,049). FastCopy 5.12 /verify 74,468 s.

En la corrida de 10 GB con BLAKE3 paralelo se observa 10,2% menos tiempo que el promedio FileStream original, 4,6% menos que el promedio overlapped serial, pero todavía 14,6% más tiempo que FastCopy. **Solo una pasada aislada** con BLAKE3 paralelo, no promocionar automáticamente. Hubo otra sesión de benchmark Solaris sobre el mismo dataset a partir de las 01:56, así que la segunda pasada de 1GiB del mismo modo se INVALIDA y no se incluye en estas medias.

## Estado

Versión instalada 0.2.0-alpha.8, UI Visual 250ms y Ultra 5000ms intactos; código experimental no instalado/commit/push. Los datasets D/E se limpiarán solo una vez que no haya procesos usando las carpetas de prueba. Se deben preservar los informes JSON y las copias originales ajenas al sandbox. ETW raw ya eliminado. Medir futuro ETW por proceso (WPA o ETL parser) antes de atribuir eventos a Solaris.
