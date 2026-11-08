# SolarisPKN-Paste — lote mismo HDD, 6.000 archivos, 7/11/2026

## Resultado medido

Se probaron **6.000 archivos pequeños sintéticos de 4–16 KiB (61.440.000 bytes)** con origen y destino en **D:** (HDD Toshiba NTFS). Dos pasadas por estrategia, en orden batch/baseline/baseline/batch, sin limpiar caché y con el equipo en uso.

| Motor | Pasada 1 (s) | Pasada 2 (s) | Promedio (s) |
|---|---:|---:|---:|
| Original secuencial en mismo HDD | 37,359 | 37,280 | **37,320** |
| Batch experimental, lectura antes de escritura (hasta 32 MiB) | 14,967 | 15,189 | **15,078** |

**Velocidad relativa:** 37,3195/15,078 = **2,475x**. **Reducción de tiempo:** ~59,6 % en este escenario. No extrapolar a SSD, red, HDD distintos o archivos grandes.

Ambas rutas informaron **PASS**, 6.000 archivos y 61.440.000 bytes; BLAKE3 verificado durante cada transferencia y 48 comprobaciones adicionales BLAKE3 origen/destino fuera del cronómetro. Reportes JSON en `D:\researchs\SolarisPKN-Paste\reports\benchmark-20261106-same-hdd-*.json`, registros de tiempo en `benchmark-20261106-status.txt`.

## Estrategia

Se implementó en `src/SameHddBatchEngine.cs` un buffer de lotes acotados por bytes, default 32 MiB, para acumular archivos de hasta 64 KiB del **mismo HDD** antes de escribirlos. Cada archivo conserva comparación BLAKE3, staging `.spaste`, publicación sin overwrite y notificación al journal de PasteEngine. Se selecciona solamente para nuevas copias, si origen y destino son el mismo dispositivo rotacional verificado; no CUT, UPDATE, deletes, operaciones de durabilidad estricta, ni destino SSD.

La opción `PasteOptions.SameHddBatchExperimental` quedó **desactivada por defecto** en el árbol experimental. Es una mejora grande, pero faltan pruebas de corte eléctrico/rollback, cancelación a mitad de lote, ADS/ACL y ejecución como usuario interactivo antes de introducirla en el binario instalado.

## Otros desarrollos

- `src/NativeDirectReadEngine.cs`: lectura nativa Windows FILE_FLAG_NO_BUFFERING+FILE_FLAG_OVERLAPPED, buffer nativo alineado a 64 KiB, cola 1/4/8, copia de cola no alineada con lectura buffered, BLAKE3 origen y staging. A/B 1 GiB D HDD->E SSD: original 9,263/9,477 s (9,370), native direct d4 9,333/9,132 s (9,233), native direct d8 10,075/11,099 (10,587). No acelera de forma convincente (apenas 1,5 % para d4).
- `SingleDurableFlushExperimental`: no llamar a FlushAsync antes de Flush(true) preserva la barrera durable, pero A/B de 1 GiB 4,717/5,928 s baseline (5,323) y 6,250/4,875 s experimental (5,563), sin ganancia demostrada.
- Suite experimental: **98 PASS, 0 FAIL** tras añadir tests de batch HDD, direct I/O, BLAKE3, cancelación previa y flush.
- Se preserva instalado `D:\Programas\SolarisPKN-Paste\SolarisPKN-Paste.exe` alpha.8. No se copiaron motores GPL ni se descompiló TeraCopy.

**Pendientes antes de rollout:** verificar comportamiento ante fallos de mitad de lote, operaciones de metadatos (ADS/ACL), ejecutar batería bajo sesión interactiva, repetir mezcla de archivos y confirmar mejoras en escenarios sin caché.
