# SolarisPKN-Paste — Windows / WSL2 / Hardware I/O Lab

**Fecha:** 2026-11-06 (Argentina). **Candidata:** 0.2.0-alpha.6. **Estado:** alfa; laboratorio acotado sobre datos sintéticos; no certificado para producción.

## Objetivo

Mantener menú contextual de Windows y GUI mínima para previsualizar, resolver conflictos y confirmar. Toda la optimización queda debajo de la interfaz. No se instalaron drivers, servicios, residentes, ni se modificaron el firmware, BIOS o antivirus.

## 1. Hardware y límites

| Unidad | Medio | Sistema de archivos / clúster |
|---|---|---|
| C: | WD PC SN530 NVMe | NTFS, 4 KiB |
| D: | Toshiba HDWQ140 HDD 4 TB | NTFS, 64 KiB |
| E: | Gigabyte 480 GB SSD | NTFS, 4 KiB |

CPU: i3-10105 (4C/8T), RAM: 40 GiB. D/E reportan bus RAID por el driver de Windows, aunque el medio físico es HDD/SSD. **Unidad L: no estaba montada y no se tocó.**

El usuario estaba viendo una película mientras corrían las pruebas. No se limpió la caché del sistema ni se controló por completo la actividad del antivirus; resultados orientativos, nunca promesas universales.

Los archivos sintéticos solo estuvieron bajo las carpetas de prueba con sentinel de D:\researchs\SolarisPKN-Paste\benchmarks\benchmark-20261106, E:\SolarisPKN-Paste-Benchmark\benchmark-20261106 y C:\SolarisPKN-Paste-Benchmark\benchmark-20261106. Al finalizar, las tres carpetas ya NO existían. C: permaneció sobre 10 GiB libres.

## 2. Copia de 1 GiB entre discos (Solaris, BLAKE3)

| Recorrido | Segundos | Integridad |
|---|---:|---|
| D: HDD -> C: NVMe | 3,618 | PASS |
| C: NVMe -> E: SSD | 4,875 | PASS |

Fuente recién creada y caché potencialmente caliente. No equivale a un test de velocidades físicas sin caché.

JSON: D:\researchs\SolarisPKN-Paste\reports\benchmark-20261106-cross-windows.json.

## 3. Movimiento dentro del mismo volumen — optimización implementada

En Cut D: -> otra carpeta D:, para archivos NUEVOS sin sobrescritura, sin huérfanos, seleccionados como archivos y no directorios, el motor ahora usa File.Move (rename por metadatos) en lugar de copiar todos los bytes y reciclar el origen.

- Preflight de todos los archivos y comprobación de colisiones.
- Journal de intención y confirmación, con flush.
- Rollback best-effort de los anteriores en orden inverso si el siguiente falla, sin sobrescribir archivos de terceros.
- Conserva el mismo objeto NTFS: bytes, tiempos, ACL y ADS.
- Cut entre volúmenes, conflictos y carpetas conservan la ruta original, sin atajos arriesgados.

**Benchmark:** mover 1 GiB entre dos carpetas de D: tardó **0,0338 s**, porque no transfirió físicamente 1 GiB. BLAKE3 del origen/destino se comprobó fuera del cronómetro, y se restauró el archivo sintético. Se probaron Cut de 9 MiB, conservación de ADS y rollback ante una colisión inyectada.

Fuente Microsoft: https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-movefile

## 4. Carpetas mixtas — gran mejora

Problema anterior: un archivo grande (>64 KiB) obligaba a que miles de pequeños abandonaran SmallFilePipeline.

Nuevo motor: pequeño nuevo (<=64 KiB), 128 o más, a un productor HDD y varios verificadores/escritores SSD; archivos grandes en TransferEngine async con BLAKE3. Caché de topología física: una consulta por volumen de origen, no por archivo.

**Dataset:** 6.001 archivos (6.000 de 4–16 KiB + uno de 1 GiB), total 1.135.181.824 bytes, D: HDD -> E: SSD.

| Configuración | Segundos | Resultado |
|---|---:|---|
| Sin pipeline | **49,762** | PASS |
| Con pipeline, pasada 1 | **23,229** | PASS |
| Con pipeline, pasada 2 | **23,808** | PASS |

**~52–53 % menos tiempo.** La segunda pasada registró explícitamente pipelineSelected=true y 47 notificaciones de progreso. Todo con BLAKE3 integrado, comprobación independiente del archivo grande y staging protegido. Pocas pasadas, no estimación estadística universal.

JSON en D:\researchs\SolarisPKN-Paste\reports\benchmark-20261106-mixed-no-pipeline.json, -mixed-pipeline.json y -mixed-pipeline-round1.json.

## 5. Robocopy 4/8 hilos — copia vs verificación

Dataset: 6.000 archivos, 61.440.000 bytes, D: -> E:. Robocopy /MT:n /E /R:0 /W:0 /COPY:DAT. La verificación fue **externa**, abriendo cada archivo origen/destino desde PowerShell 5.1 con SHA-256. Robocopy no ejecutó esos hashes.

| Robocopy | Solo copia | SHA-256 externo | Total |
|---|---:|---:|---:|
| /MT:4 | **1,768 s** | 40,241 s | 42,009 s |
| /MT:8 | **1,448 s** | 65,694 s | 67,142 s |

La variación enorme de la fase de verificación refleja apertura masiva de archivos, CPU/FS y carga cambiante. **No comparar Robocopy copia sola contra Solaris con BLAKE3 incorporado**; ni concluir que Robocopy es lento por el costo de un verificador ajeno. Es evidencia a favor de fusionar hashing en el pipeline y reducir aperturas repetidas.

Fuente Microsoft: https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/robocopy/

## 6. Debian WSL2: ext4 frente a 9P

WSL Debian en sesión normal del usuario, kernel 6.6.87.2-microsoft-standard-WSL2. El broker SYSTEM no permite iniciar distros WSL de usuarios. Se usó una tarea programada temporal, retirada al terminar.

**Corrección metodológica:** /tmp resultó ser tmpfs (RAM), NO ext4. Se archivó esa primera pasada con etiqueta tmpfs y se repitió la prueba bajo /var/tmp, confirmado como ext4 real montado sobre /dev/sdd.

128 MiB sintéticos ext4 -> ext4, incluyendo fsync del archivo destino, más un SHA-256 independiente:

| Linux método | Copia + fsync (2 ensayos) | SHA-256 adicional |
|---|---|---|
| cp --reflink=never | 0,9381 / 0,9886 s | 0,4925 / 0,4608 s |
| os.copy_file_range | 1,4896 / 1,4566 s | 0,3818 / 0,3317 s |

cp --reflink=always falló (ext4 aquí no admite clones CoW). copy_file_range NO fue más rápido que cp bajo estas condiciones.

Las unidades Windows aparecen en WSL2 como montaje 9P (/mnt/d, /mnt/e). Copiar 64 MiB D->E mediante cp+fsync = 0,8736 s; SHA-256 posterior = 0,4078 s. copy_file_range devolvió EXDEV (cross-device), requiere fallback. **9P no es una medición nativa de ext4 frente a NTFS**; son capas distintas, con cachés y virtualización.

JSON: D:\researchs\SolarisPKN-Paste\reports\wsl-benchmark-20261106.json y -tmpfs-initial.json. Las carpetas de WSL ext4, /mnt/d y /mnt/e se eliminaron.

Fuentes Linux: https://man7.org/linux/man-pages/man2/copy_file_range.2.html ; https://www.kernel.org/doc/html/latest/filesystems/ext4/allocators.html ; https://btrfs.readthedocs.io/en/latest/

## 7. Investigación de FastCopy, TeraCopy y blitcp

- FastCopy v5 documenta overlapped/direct I/O, patrones HDD/SSD, preasignación, lecturas y escrituras concurrentes, CPU affinity y grandes buffers. Software propietario actual; no se desensambló, ni se usó su código. https://fastcopy.jp/help/fastcopy_eng.htm
- TeraCopy usa colas y verifica; su ensayo anterior utilizó SHA-256 PowerShell externo, NO equiparable a verificación integrada.
- blitcp documenta ordenar archivos según su ubicación física en disco (FIEMAP/FSCTL), escribir pequeños primero y deduplicar mediante hardlinks. **No activar hardlinks por defecto**: modificar uno alteraría el otro, violando la independencia esperada al copiar. Ordenar por offset físico solo se justifica con medición de seeks y costo de consulta. https://github.com/gekap/blitcp/blob/master/DOCUMENTATION.md
- Linux io_uring + copy_file_range aparece en un RFC de septiembre de 2026, no se asume incorporado a un kernel estable. https://lore.gnuweeb.org/io-uring/20260927-cfr-rfc-v1-0-5b9ddbe0490f%40gmail.com/T/

## 8. QA e instalación

- Versión alpha.6: nuevas pruebas de Cut por rename, ADS NTFS y rollback; carpeta mixta, verificación de BLAKE3, límite FAT32, CopyFile2 experimental. **40 PASS / 0 FAIL** en el autotest integrado.
- SelfTest utiliza TEMP del usuario, sin requerir permisos de escritura en carpeta de programa.
- La GUI conserva menú contextual, vista previa y confirmación; únicamente se limita el envío de estado al hilo WinForms a 8 Hz.
- Sin driver kernel, sin servicio ni residente nuevo. La instalación debe respaldar la versión anterior y dejar ajustes/registro del menú inalterados.
- Pendiente validar GUI de Explorer interactiva, ACL/ADS/EFS/hardlinks en copias ordinarias, Cut de directorios, pruebas de apagón y fallos, USB exFAT/FAT32, SMB y NVMe grande con múltiples colas.
- No declarar victoria definitiva sobre FastCopy ni estabilidad de producción por 1–2 pasadas.

## 9. Próximas optimizaciones NO implementadas

1. Tres etapas acotadas: lector HDD, escritores SSD, verificadores SSD antes del commit, benchmark A/B frente a 4 consumidores integrados.
2. ETW/WPA para identificar metadatos, espera del disco, filtros de antivirus y page cache.
3. Reflinks/copy-on-write únicamente para filesystem y escenario que permitan semántica equivalente; nunca vender clones lógicos como copias físicas.
4. Mejor fidelidad de metadatos (ADS/ACL/sparse/reparse), integridad y recuperación post-crash antes de optimizar más microsegundos.

**Separar observaciones, propuestas y trabajo pendiente.**
