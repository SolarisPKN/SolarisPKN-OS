# SolarisPKN-Paste | Motor adaptativo: CPU, filesystem y almacenamiento
Fecha: 2026-11-06 (ART). Build: 0.2.0-alpha.4 (instalada). Estado: alfa, no certificado para datos críticos.

## Hardware leído de Windows
| Unidad | Dispositivo | Bus informado | FS | Clúster | Escritores automáticos |
|---|---|---|---|---|---|
| C: | WD PC SN530 SSD NVMe 256 GB | NVMe | NTFS | 4 KiB | 4 (sin benchmark en C:) |
| D: | Toshiba HDWQ140 HDD 4 TB | RAID, HDD detectado | NTFS | 64 KiB | 1 |
| E: | Gigabyte GP-GSTFS31480GNTD SSD 480 GB | RAID, SSD detectado | NTFS | 4 KiB | 4 |

CPU: Intel Core i3-10105, 4 núcleos / 8 procesadores lógicos, L3 6 MiB. RAM: 40 GiB DDR4 informados en dos canales con capacidades asimétricas. GPU: NVIDIA GTX 1070 + Intel UHD 630. Las unidades D/E informan bus RAID porque el controlador presenta así los dispositivos: no implica que sean el mismo medio físico. No se modificó firmware, driver, BIOS ni caché de escritura.

## Diseño ya implementado
- FileSystemProfile.cs detecta formato NTFS/FAT32/exFAT/ReFS, clúster con GetDiskFreeSpaceW, tipo de medio y bus cuando Windows lo admite.
- StorageTopology.cs consulta propiedades Win32 para separar HDD de unidades SSD/NVMe y agrupar Antifrag.
- SmallFilePipeline.cs conserva UN lector ordenado para el HDD y 1 a 8 consumidores de escritura/verificación SSD para lotes de >=128 archivos NUEVOS de hasta 64 KiB. Channel bounded limita la memoria en vuelo a unas decenas de buffers de 64 KiB, sin presión masiva de RAM.
- Writer SSD por defecto = mínimo entre 4 y la mitad de procesadores lógicos; HDD solo 1; USB máximo 2; medio desconocido, 1. Configurable 0=automático o 1 a 8 en Ajustes.
- Se verifica BLAKE3 de la fuente y del temporal de destino; se publica el archivo solo después de verificar. La verificación rápida puede reutilizar un mismo FileStream ReadWrite para evitar una apertura extra por archivo. No equivale a persistencia física ante corte eléctrico.
- La ruta de archivos grandes y las operaciones Cut, sobrescritura y borrado huérfano conservan las protecciones anteriores. En C: se protege el mínimo de 10 GiB libres.
- Blake3.NET 3.0.2 utiliza SIMD de CPU con detección dinámica, sin programación de GPU. Fuente: https://www.nuget.org/packages/Blake3/3.0.2

## Benchmarks reales (6.000 archivos de 4–16 KiB, 61.440.000 bytes, HDD D: hacia SSD E:)
| Configuración | Tiempo (segundos) |
|---|---|
| Solaris primera versión | 55,782 |
| Solaris un escritor + verificador | 40,033 |
| Solaris 2 escritores | 21,513 y 24,924 |
| Solaris 4 escritores con dos handles por archivo | 20,594 y 20,574 |
| Solaris 8 escritores | 21,426 |
| Solaris 4 escritores, mismo handle para escribir/verificar | 19,810 y 18,166 |
| Solaris modo adaptativo auto alpha.4 | 21,609 |
| FastCopy 5.12 con verificación integrada | 16,267 (pasada anterior) y 19,075 (última comparación) |

Los resultados son de muy pocas pasadas: la variación de caché, antivirus y carga es apreciable. No se ha certificado superioridad de Solaris frente a FastCopy. Sí se ha comprobado que varios escritores/verificadores aceleran significativamente esta carga, y 8 hilos no superan a 4.

## Diferencias de filesystem
**NTFS:** MFT, journaling, ACL, alternate data streams, timestamps y tamaños de clúster. D: tiene clústeres de 64 KiB; archivos de 4–16 KiB suelen consumir un clúster cada uno, incluso cuando se leen pocos bytes. En E: el clúster es de 4 KiB. La aplicación todavía no conserva fielmente ACL, ADS ni vínculos entre archivos: NO usar como clonador forense o copia exhaustiva de metadatos.

**FAT32:** límite de archivo 4 GiB menos 1 byte. El motor lo rechaza antes de ejecutar una transferencia mayor, evitando un truncado silencioso. Sin ACL/ADS, timestamps menos precisos. Falta prueba de dispositivo FAT32 real.

**exFAT:** bitmap de asignación, sin equivalencia completa al journaling y permisos NTFS; soporta archivos grandes. Se detecta y usa API Windows estándar; falta benchmark sobre medios USB/exFAT.

**ReFS:** perfil reconocido pero sin suite completa para integridad de datos, reflinks y metadatos, por lo que evita técnicas específicas no verificadas.

**ext4/XFS/Btrfs:** no son dispositivos Windows nativos en el EXE actual. Por SMB o WSL intervienen capas adicionales, no se puede optimizar su asignador de bloques directamente desde Windows. Un eventual backend Linux separado debería consultar statfs y soporte de io_uring, copy_file_range, fallocate, fsync y FICLONE para clones CoW de sistemas compatibles. Ext4 agrupa bloques/extents; XFS usa allocation groups y extents; Btrfs incorpora CoW, checksums, reflinks y snapshots, con consideraciones de fragmentación diferentes.

Documentación Microsoft:
- https://learn.microsoft.com/en-us/windows/win32/fileio/filesystem-functionality-comparison/
- https://learn.microsoft.com/en-us/windows/win32/fileio/exfat-specification
- https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-getdiskfreespacew

Documentación Linux:
- https://kernel.org/doc/html/latest/filesystems/ext4/overview.html
- https://btrfs.readthedocs.io/en/latest/
- https://www.kernel.org/doc/html/latest/filesystems/

## GPU, chipset, RAM y cachés
GPU no realiza escritura directa a NTFS: GPUDirect Storage de NVIDIA no está soportado en Windows y no es una optimización válida para esta GTX 1070 en este flujo. https://developer.nvidia.com/gpudirect-storage

BLAKE3 3.0.2 ya aprovecha SIMD del CPU y soporta multihilo para datos grandes; antes de hacer offload a GPU hay que medir transferencia PCIe y sobrecarga de memoria. No activar afinidad de CPU, sobreescritura de firmware ni caché write-back insegura sin pruebas.

La caché SLC/DRAM del SSD y la caché del sistema operativo se gestionan principalmente por drivers/firmware. Solaris solo controla patrones de I/O, longitud de cola, buffers y barreras de durabilidad. Verificación BLAKE3 del temporal no equivale a flush a medio físico. El modo estricto sigue disponible y obliga a un flush físico por archivo pequeño.

## QA y tareas pendientes
- Compilación Windows x64 autocontenida: correcta, sin errores. Selftest: 28 PASS, 0 FAIL.
- Las carpetas sintéticas de D/E se eliminaron; C: conserva más de 23 GiB libres.
- Pendiente: overlapped I/O o IOCP profundo para archivos grandes/NVMe, pruebas repetidas con ETW/PerfMon y caches fría/caliente, cancelación bajo estrés, verificación de integridad tras fallos de energía, ACL/ADS/hardlinks, GUI y contexto Windows 11 moderno, USB FAT32/exFAT, backend Linux independiente.
- FastCopy/TeraCopy propietarios no se descompilaron ni se reutilizó código. Las optimizaciones en Solaris son implementación original de API de Windows y .NET.

Ejecutable: D:\Programas\SolarisPKN-Paste\SolarisPKN-Paste.exe
Informes JSON de pruebas: D:\researchs\SolarisPKN-Paste\reports\
