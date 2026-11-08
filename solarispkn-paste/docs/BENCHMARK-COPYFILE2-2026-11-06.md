# Benchmark CopyFile2 vs SolarisPKN-Paste: Windows vs API propia
Fecha: 2026-11-06. PC: Windows 10 IoT Enterprise LTSC build 19044, Core i3-10105 4C/8T, RAM 40 GiB, D: HDD Toshiba HDWQ140 NTFS clúster 64KiB, E: SSD Gigabyte NTFS clúster 4KiB.

## Alcance del ensayo

Tres rutas copiaron datos sintéticos desde D: a E: dentro de sandboxes exclusivos y marcados, sin tocar archivos del usuario. Cada ruta deja temporal y comprueba hash BLAKE3 completo del stream de datos principal antes de publicarlo. La comparación incluye transferencia, verificación y staging; no solo el tiempo de copiar bloques.

- Solaris FastTransfer: FileStream Async con lectura anticipada, BLAKE3 de fuente integrado a la transferencia; relectura del temporal de destino; preasignación y Flush(flushToDisk:true) al final.
- CopyFile2 buffered: motor Windows con caché, luego cálculo BLAKE3 de fuente y temporal en paralelo cuando origen/destino están en dispositivos físicos distintos; preserva atributos/ADS de Windows, NO automáticamente ACL.
- CopyFile2 NO_BUFFERING: mismo protocolo de verificación, pero COPY_FILE_NO_BUFFERING durante transferencia; hash posterior sigue buffered para ambas rutas.

## Ensayo de 1 GiB (1.073.741.824 bytes)

| Motor | Segundos totales | MiB/s efectivos | Integridad |
|---|---:|---:|---|
| CopyFile2 buffered | 1,737 | 589,37 | BLAKE3 PASS |
| Solaris streaming async | 4,868 | 210,35 | BLAKE3 PASS |
| CopyFile2 NO_BUFFERING | 11,452 | 89,42 | BLAKE3 PASS |

La carga de 1 GiB se generó inmediatamente antes del ensayo, sobre un equipo de 40 GiB RAM. Los valores más altos que el HDD provienen de una mezcla de page cache y E/S física, por lo que **NO son throughput de disco frío**. Comparar buffered vs no-buffering sin registrar el cache working set induce a error.

## Ensayo de 10 GB (10.000.000.000 bytes, 9,313 GiB)

| Motor | Segundos totales | MiB/s efectivos | Integridad |
|---|---:|---:|---|
| Solaris streaming async | **91,738** | **103,96** | BLAKE3 PASS |
| CopyFile2 NO_BUFFERING | 114,140 | 83,55 | BLAKE3 PASS |
| CopyFile2 buffered | 167,166 | 57,05 | BLAKE3 PASS |

En el mismo lote de 10 GB, Solaris terminó aprox. **19,6 % antes** que CopyFile2 unbuffered, y **45,1 % antes** que CopyFile2 buffered. Se midió una sola pasada por modo, sin control de caché fría ni estadística robusta.

Observabilidad no intrusiva durante CopyFile2 buffered: el proceso acumuló más de 25 GiB leídos en su contador de transferencias antes de terminar; se observó D: con lecturas físicas en un momento de ~201 MB/s mientras E: no registraba escrituras, lo que coincide con la verificación posterior de origen/temporal. Son muestras puntuales, no valores medios.

## Verificación funcional de Windows

El backend NativeWindowsCopy fue probado con CopyFile2 buffered/no-buffering en 9 MiB, BLAKE3. Además, **preservó el flujo alternativo ADS NTFS** de un archivo de prueba, cosa que la ruta FileStream del producto aún no conserva. Se validó la ruta completa PasteEngine CopyFile2 staging+hash y que Copiar no elimina origen.

Advertencia: los hashes BLAKE3 cubren el stream de datos principal, no verifican por separado cada ADS ni las ACL del archivo. CopyFile2 preserva ADS y atributos extendidos pero la documentación Microsoft confirma que no copia automáticamente descriptores de seguridad. Se requiere suite de metadatos antes de declarar fidelidad total.

## Decisión técnica implementada

- **Mantener FastTransfer async + BLAKE3 como motor predeterminado**, tanto para seguridad como rendimiento de 10 GB en D: HDD -> E: SSD.
- **Mantener CopyFile2 como backend experimental OPT-IN** (NativeCopyForLarge=false por defecto) solo para >=8 MiB, documentado. NativeCopyUnbuffered=false por defecto.
- No agregar pantallas, residentes, drivers ni permisos extra. La GUI sigue siendo el menú contextual + vista previa/conflictos/confirmación.
- Throttle de progreso al copiar grandes para evitar notificaciones por cada buffer (como máximo aproximadamente 6-7 por segundo en el path read-ahead).
- Guardrail 10GiB libres C: sigue vigente; staging de benchmark en D/E eliminado al finalizar.
- Candidata 0.2.0-alpha.5 pasó **35 PASS / 0 FAIL** en autotest. Todavía no está certificada para datos críticos.

## Por qué a veces Linux gana y a veces Windows

Linux puede tener ventaja cuando copy_file_range o reflinks evitan transferencia user-space o bloques CoW, o cuando ext4/XFS agrupan escrituras de metadatos con delayed allocation/logging. Windows también tiene ODX y CopyFile2 acelerado cuando hardware lo admite. En ambas plataformas hay sesgo fuerte de caché, algoritmo de verificación, controlador de volumen, filesystem, tamaño de archivo y media. Nunca atribuir todo el rendimiento al kernel o a usar más hilos.

## Limitaciones del estudio

Una pasada por motor y tamaño, las copias se ejecutaron en orden fijo, no se purgó caché ni se midió temperatura; no se utilizó un trace ETW continuo. No se probaron USB, SMB, WSL, ReFS, Linux real ni NVMe->NVMe. Se requiere una matriz repetida controlando esas variables antes de establecer heurísticas universales.

## Archivos de evidencia

D:\researchs\SolarisPKN-Paste\reports\benchmark-20261106-medium-fast.json
D:\researchs\SolarisPKN-Paste\reports\benchmark-20261106-medium-native2.json
D:\researchs\SolarisPKN-Paste\reports\benchmark-20261106-medium-native2-unbuffered.json
D:\researchs\SolarisPKN-Paste\reports\benchmark-20261106-large-fast.json
D:\researchs\SolarisPKN-Paste\reports\benchmark-20261106-large-native2.json
D:\researchs\SolarisPKN-Paste\reports\benchmark-20261106-large-native2-unbuffered.json
D:\researchs\SolarisPKN-Paste\reports\benchmark-20261106-status.txt

Fuentes Microsoft:
https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-copyfile2
https://learn.microsoft.com/en-us/windows/win32/fileio/file-caching
https://learn.microsoft.com/en-us/windows-hardware/drivers/ifs/offloaded-data-transfers

Fuentes Linux:
https://man7.org/linux/man-pages/man2/copy_file_range.2.html
https://www.kernel.org/doc/html/latest/filesystems/ext4/allocators.html
https://docs.kernel.org/filesystems/xfs/xfs-delayed-logging-design.html
https://btrfs.readthedocs.io/en/latest/
