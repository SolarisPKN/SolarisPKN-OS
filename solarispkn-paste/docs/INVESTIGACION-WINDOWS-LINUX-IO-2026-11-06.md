# SolarisPKN-Paste: investigación aplicada de sistemas de archivos y E/S
Fecha: 2026-11-06 (Argentina).
Objetivo: máxima velocidad, integridad y comportamiento predecible, manteniendo menú contextual y una pequeña interfaz de conflictos/confirmaciones. No se requieren residentes, servicios ni controladores kernel para el motor estándar.

## 1. Por qué Linux parece más rápido que Windows en algunas copias (y viceversa)

No hay un ganador universal. Importa el filesystem real, el sistema de origen/destino, el medio y su caché, la API, el método de verificación y la política de durabilidad. En particular:
- Una función de copia puede devolver éxito después de escribir en page cache sin que los datos estén físicamente persistidos.
- La lectura de archivos recién creados puede satisfacerse en RAM: el resultado depende del cache working set respecto a la RAM. Comparar 1 GiB caliente contra 10 GiB frío produce conclusiones engañosas.
- Linux copy_file_range deja al kernel/filesystem implementar reflink o copia del lado del servidor cuando son compatibles. No garantiza zero-copy ni elimina lectura física por sí solo. En archivos sparse, puede expandir agujeros; requiere verificación.
- Windows CopyFile/CopyFileEx/CopyFile2 pueden aprovechar ODX cuando el subsistema de almacenamiento lo admite. Es particularmente relevante en almacenamiento servidor/SAN/SMB compatible, no automáticamente en SSD consumidores.

Referencias:
https://man7.org/linux/man-pages/man2/copy_file_range.2.html
https://learn.microsoft.com/en-us/windows-hardware/drivers/ifs/offloaded-data-transfers
https://learn.microsoft.com/en-us/windows/win32/fileio/file-caching

## 2. Windows: ruta de copia automática frente a motor propio

### Windows Cache Manager

Windows usa buffered I/O por defecto y lazy writes. Es muy útil para muchos archivos pequeños; forzar un FlushFileBuffers por archivo degrada este patrón. FILE_FLAG_SEQUENTIAL_SCAN ayuda con lectura ordenada. FILE_FLAG_NO_BUFFERING puede ahorrar presión de caché en archivos muy grandes, pero no es automáticamente más rápido y las lecturas/escrituras directas deben satisfacer restricciones de alineación. El firmware controla caches propias que no se pueden tratar como page cache de Windows. Integridad (hash coincidente) y durabilidad (datos persistidos ante corte de energía) son objetivos separados.

Fuentes:
https://learn.microsoft.com/en-us/windows/win32/fileio/file-caching
https://learn.microsoft.com/en-us/windows/win32/fileio/file-buffering
https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-flushfilebuffers

### CopyFile2

La API CopyFile2 realiza copia de origen a destino en modo usuario, conservando atributos extendidos, NTFS alternate data streams, OLE structured storage y atributos de archivo. No conserva automáticamente los ACL/descriptores de seguridad del origen. Ofrece progreso/cancelación, COPY_FILE_NO_BUFFERING para archivos muy grandes y aprovecha offload cuando está disponible. El backend propio debe conservar staging y verificación BLAKE3; no ejecutar copias destructivas directamente.

El backend de FileStream permite fusionar lectura con hash fuente, lo que potencialmente evita releer el HDD por segunda vez. Por eso CopyFile2 no tiene por qué superar el motor propio cuando ambos verifican el archivo completo. En CopyFile2 se evalúa hash del origen y temporal por separado, en paralelo solo entre medios independientes o SSD; en un único HDD se hace secuencial para no provocar seeks.

Fuentes:
https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-copyfile2
https://learn.microsoft.com/en-us/windows/win32/api/winbase/ns-winbase-copyfile2_extended_parameters
https://learn.microsoft.com/en-us/windows-hardware/drivers/ifs/offloaded-data-transfers

### Clonado de bloques

FSCTL_DUPLICATE_EXTENTS_TO_FILE permite a determinados filesystem copiar rangos lógicos sin mover bloques físicos. No funciona automáticamente para copiar entre D: HDD y E: SSD; ni para NTFS común de Windows 10 asumir reflinks como con Btrfs. Solo activar tras confirmar soporte de filesystem y escenario. No manipular directamente bloques de un volumen montado.

Fuente: https://learn.microsoft.com/en-us/windows/win32/api/winioctl/ni-winioctl-fsctl_duplicate_extents_to_file

## 3. Windows NTFS/FAT32/exFAT/ReFS: capacidades y costos

NTFS utiliza MFT, metadatos y journal; pequeños archivos pueden residir en MFT bajo condiciones especiales, pero crear, abrir, cerrar y renombrar miles de archivos implica costo significativo. NTFS permite ACL, EFS, alternate data streams, hardlinks, sparse y puntos de análisis. FileStream manual copia stream principal y hora de modificación: puede perder metadata que importaba. CopyFile2 conserva ADS y atributos, pero no ACL. Un copiador avanzado necesita avisar cuando la fidelidad de metadatos no está garantizada.

FAT32 limita un archivo a 4 GiB menos 1 byte; no soporta ADS, hardlinks ni ACL de NTFS. ExFAT soporta archivos grandes, pero no tiene journal de metadatos como NTFS; timestamps y permisos diferentes. ReFS tiene capacidades de integridad/clonado distintas según versión y soporte. La selección de estrategia debe usar detection de capacidad, no solo nombre de formato.

En la PC real: D: Toshiba HDD NTFS con clusters 64 KiB, E: SSD Gigabyte NTFS 4 KiB y C: WD SN530 NVMe NTFS 4 KiB. D y E reportan transporte RAID por el driver aunque sus medios son distintos. Un archivo de ~4 KiB en D podría ocupar como mínimo un cluster de 64 KiB cuando no es residente en MFT.

Fuentes:
https://learn.microsoft.com/en-us/windows/win32/fileio/filesystem-functionality-comparison/
https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-getdiskfreespacew

## 4. Linux: diferencias internas concretas

### ext4 (asignación y journal)

Ext4 usa extents, multiblock allocation, delayed allocation, grupos de inodos y journaling jbd2. La asignación diferida permite a Linux colocar mejor los bloques cuando llegan en conjunto y mejora localidad, sobre todo en HDD. En el modo común data=ordered el journal protege principalmente metadatos: no es una transacción que garantice todos los bytes de usuario tras un apagón. Las barreras fsync tienen costo; los mecanismos de agrupación de E/S dejan de ayudar si hacemos sync por archivo.

Documentación oficial:
https://www.kernel.org/doc/html/latest/filesystems/ext4/allocators.html
https://www.kernel.org/doc/html/latest/filesystems/ext4/journal.html

### XFS

XFS usa un sistema transaccional de metadatos y delayed logging: agrupa cambios antes de forzar el log al medio. La documentación explica que las transacciones son asincrónicas por defecto y fsync asegura lo necesario cuando importa la durabilidad. Un backend debe medir grandes extents, concurrencia y latencias de log antes de asumir que más hilos mejoran el rendimiento.

Documentación oficial: https://docs.kernel.org/filesystems/xfs/xfs-delayed-logging-design.html

### Btrfs

Btrfs emplea copy-on-write, checksums, subvolúmenes, snapshots y reflinks. Una copia dentro del mismo sistema compatible puede compartir extents sin copiar todo el contenido físico. Eso acelera duplicación pero no es idéntico a una segunda copia independiente y físicamente materializada. Entre dos discos independientes, reflink normalmente no aplica. CoW puede influir en fragmentación cuando reescribimos archivos a menudo; no hay receta única para todo Btrfs.

Fuentes:
https://btrfs.readthedocs.io/en/latest/
https://www.kernel.org/doc/html/latest/filesystems/

### Linux O_DIRECT

El manual indica que O_DIRECT generalmente degrada rendimiento si la aplicación no maneja sus buffers y patrones específicos; tiene requisitos de alineación distintos entre FS y kernel. El rendimiento aparente no debe manipularse comparando una ruta sin caché contra otra con datos en RAM.

Fuente: https://man7.org/linux/man-pages/man2/open.2.html

## 5. Matriz de estrategia de SolarisPKN-Paste

- Muchos archivos pequeños HDD->SSD: un lector HDD secuencial, 4 consumidores/verificadores SSD según benchmark local, Channel bounded, ArrayPool, staging y BLAKE3; no asignar 12 hilos inútiles.
- SSD->SSD o NVMe->NVMe: medir profundidad de cola y concurrencia del origen y destino, limitar CPU/memoria; el valor óptimo puede diferir de HDD->SSD.
- Mismo HDD: evitar múltiples lectores y escritores aleatorios, mantener una cola y preasignar archivos grandes.
- Archivo grande entre discos: comparar motor asíncrono con BLAKE3 durante copia frente a CopyFile2 más hashes independientes, y activar por mediciones reales según FS/medio.
- Archivos con ADS, EFS, atributos read-only o sparse: probar fidelidad antes de activar optimizaciones que solo copian bytes; no declarar compatibilidad sin QA.
- FAT32: rechazar archivos mayores a 4 GiB menos 1 byte antes de transferir.
- Linux: futuro backend específico basado en statfs, copy_file_range, fallocate, io_uring, reflink, fsync; NO implementar falsamente estas funciones en una extensión Windows.

## 6. Qué se implementó en el prototipo experimental

1. NativeWindowsCopy.cs: copia con CopyFile2 al temporal, con opción buffered/unbuffered, cancelación, checksum fuente/destino BLAKE3 y comprobación de tamaño/mtime. No cambia el destino publicado hasta verificar el temporal.
2. Flags NativeCopyForLarge y NativeCopyUnbuffered desactivados por defecto; solo aplicables en tamaños >= 8 MiB. No se agregó ningún control visible en la GUI.
3. TransferEngine.cs: reducción de notificaciones de progreso en archivos grandes para no sobrecargar el hilo de la GUI.
4. SelfTest: 9 MiB buffered, 9 MiB no-buffering, NTFS alternate data stream; verificación BLAKE3.
5. BenchmarkRunner: 1 GiB y 10 GB sintéticos bajo D:/E: con marca de propiedad, informes JSON y limpieza dirigida.

## 7. Cómo evitar benchmarks engañosos

Reportar siempre:
- tamaño y cantidad de archivos, source/dest, filesystem, cluster, bus, tipo de disco, RAM/CPU;
- tiempo de planificación, transferencia y verificación, y si fuente estaba en page cache;
- política de flush/durabilidad, necesidad de leer por segunda vez el origen para el checksum;
- pasadas repetidas con orden alternado, pruebas de caché fría y caliente, e integridad completa;
- efecto del antivirus, filtros de sistema de archivos, drivers y temperatura.

El prototipo nuevo NO se promociona a instalador predeterminado sin justificar rendimiento y seguridad con resultados. No se cambió Explorer, servicios o drivers, y no se desactivó Microsoft Defender.

## 8. Pendientes críticos

Fidelidad ACL/ADS/sparse/enlaces, permisos estándar, EFS, cancelación bajo estrés, crash consistency, cambios concurrentes del origen, gran volumen NVMe->NVMe, documentación operativa del modo de conflictos y pruebas reales de GUI Windows. En Windows, usar ETW/WPA para medir metadatos, latencia de FS, page cache e I/O físico sin inventar métricas.

**No se utilizó código propietario de FastCopy/TeraCopy ni ingeniería inversa de sus binarios comerciales.**
