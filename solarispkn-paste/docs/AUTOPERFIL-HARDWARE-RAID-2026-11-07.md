# SolarisPKN-Paste — Autoperfilado de CPU, discos, RAID y USB

Estado: implementación candidata experimental, probada en Windows. La versión alpha.8 instalada NO fue reemplazada. Fecha del laboratorio: 7 de noviembre de 2026 (ART).

## Objetivo
La primera ejecución genera hardware-profile.json bajo %LOCALAPPDATA%\SolarisPKN\Paste. Inventaría CPU (nombre, lógicos, memoria, SSE2, AVX2, AVX512), realiza un BLAKE3 de solo memoria (4 MiB de buffer, 64 MiB por ronda, tres rondas), y registra volúmenes con modelo, fabricante si es visible, serie enmascarada/huella SHA-256, interfaz, tipo de medio, sistema de archivos, identidad y composición RAID visible. Reutiliza el perfil si el hardware no cambió; lo refresca al arrancar y al recibir eventos de dispositivo en el residente de bandeja (WM_DEVICECHANGE, debounce 750 ms). No ejecuta benchmarks de escritura al conectar pendrives.

## Hardware observado en sesión real de usuario
| Volumen | Modelo | Medio | Bus controlador | Dispositivos físicos visibles |
|---|---|---|---|---|
| C: | WD PC SN530 256GB | SSD | NVMe | 1 SSD |
| D: | TOSHIBA HDWQ140 4TB | HDD | RAID | 1 HDD |
| E: | GIGABYTE GP-GSTFS31480GNTD 480GB | SSD | RAID | 1 SSD |

BusType=RAID no implica arreglo RAID. Get-Disk y Get-PhysicalDisk, cruzados por número, modelo y capacidad, permitieron desambiguar D:/E: y ver su naturaleza real. No se identificó un arreglo Storage Spaces. Una tarjeta RAID propietaria puede ocultar miembros físicos; en ese caso la política permanece conservadora. La serie real se trata solo en memoria: el JSON almacena enmascarado y hash, no serial completo. No se envía telemetría a servidores.

CPU: Intel Core i3-10105, 8 lógicos, ~39,82 GiB RAM, AVX2, BLAKE3 memoria 1307,21 MiB/s en una ejecución orientativa. Segunda ejecución: perfil estable, ChangeCount sin cambios. Un CPU de 2 lógicos, RAM menor a 4 GiB o BLAKE3 bajo 400 MiB/s limita escritores a 1; el valor de corte es conservador y no sustituye un perfilador de carga real.

## Estrategias automáticas
- HDD en cualquier extremo o miembro RAID: prioridad mecánica, un lector y un escritor, cola física, bloqueo de las unidades de origen y destino por procesos Solaris en orden fijo. No se usa paralelismo agresivo sobre el cabezal.
- HDD origen → SSD destino: un lector de HDD + un escritor de SSD pueden trabajar en pipeline acotado, sin fan-out de HDD.
- SSD origen → HDD destino, o HDD→HDD: serialización con cola de disco para evitar intercalado excesivo de solicitudes.
- SSD y SSD confirmados, incluidos miembros RAID enteramente SSD: pipeline paralelo con escritores acotados por CPU/RAM y por mediciones. En este equipo E: SATA = 4 escritores; C: NVMe = 1 escritor, según benchmark previo de miles de archivos pequeños.
- RAID de composición oculta: ConservativeQueue, 1 escritor; nunca se asume que todo es SSD.
- USB: un HDD confirmado queda mecánico; pendrive/SSD extraíble sin perfil de rendimiento comprobado usa cola conservadora y ninguna prueba de escritura invasiva.
- La coordinación entre procesos por mutex afecta solo a SolarisPKN-Paste; no puede frenar Explorer, Robocopy o terceros.
- La cola reduce movimientos de cabezal y choques entre tareas; NO desfragmenta archivos existentes ni garantiza cero fragmentación en RAID/HDD.

## Componentes implementados
- HardwareAutoProfiler.cs: CPU/RAM/SIMD, benchmark BLAKE3 en RAM, firma host, volúmenes, caché persistente v2, serial enmascarado y cambio de hardware.
- RaidTopologyProbe.cs: Get-Disk/Get-PhysicalDisk/Get-VirtualDisk, modelos/serie, correlación exacta por número de dispositivo, reconocimiento de miembros Storage Spaces, fallback si el controlador oculta miembros.
- TransferHardwarePolicy.cs: prioridad HDD, rutas desconocidas conservadoras, límite CPU/RAM y self-checks para RAID mixto/SSD/USB.
- PasteQueue.cs: mutex de los dispositivos físicos de origen y destino, adquiridos siempre en orden para evitar deadlocks.
- PasteEngine.cs: política aplicable a pipeline de archivos pequeños con opción AutoHardwareProfile=true por defecto en la candidata, desactivable por usuario.
- TrayResident.cs: WM_DEVICECHANGE, debounce 750ms y actualización manual desde bandeja.
- Program.cs / Installer.cs: autoperfilado en instalación/arranque sin bloquear GUI, opción en Ajustes para mostrar hardware/serie enmascarada.
- HardwareRoutingRunner.cs: escenario de prueba aislado con datos sintéticos y SHA256 íntegro de los resultados.

## Pruebas
Candidato compilado Release Windows x64; selftest-headless PASS; hardware-profile-test PASS en SYSTEM y en sesión interactiva. Dos arranques de usuario verificaron persistencia y ausencia de microbenchmark repetido.

Siete rutas de copia sobre 192 archivos sintéticos de 4/8/12/16 KiB por escenario: BLAKE3 en motor y SHA-256 de TODOS los archivos de salida.

| Ruta | Política comprobada | Bloqueos físicos | Escritores | Resultado |
|---|---|---:|---:|---|
| D HDD → E SSD | MechanicalQueue | 2 | 1 | PASS |
| E SSD → D HDD | MechanicalQueue | 2 | 1 | PASS |
| D HDD → D HDD | MechanicalQueue | 1 | 1 | PASS |
| C NVMe → E SSD | SolidParallel | 1 | 4 | PASS |
| E SSD → C NVMe | SolidParallel | 1 | 1 | PASS |
| C NVMe → C NVMe | SolidParallel | 1 | 1 | PASS |
| E SSD → E SSD | SolidParallel | 1 | 4 | PASS |

Fuente de resultados: D:\researchs\SolarisPKN-Paste\reports\hardware-route-20261107\summary.json.
Fuente del perfil en Windows del usuario: D:\researchs\SolarisPKN-Paste\reports\hardware-raid-user-v2-check-20261107.json.

El RAID mixto (SSD+HDD), RAID totalmente SSD y RAID opaco se probaron con objetos de topología simulados en los invariantes de política; NO se conectó hardware RAID mixto real. No se probó físicamente hotplug de un pendrive en esta tanda. Para RAID propietaria avanzada hace falta integración adicional con los controladores compatibles; SMART/NVMe health y vendor pass-through no están implementados todavía.

## Seguridad, próximos pasos
No escribir nunca sobre dispositivos raw, no desactivar protección del SO, no ejecutar DiskSpd al conectar extraíbles. La futura promoción a estable requiere QA de ACL/ADS/EFS, enlaces, RAID hardware propietario, cancelación, recuperación y USB físico; no confundir colas con desfragmentación. El selector aún no aprende automáticamente una curva de IOPS de todos los dispositivos, sino que aplica topología y límites de CPU, con perfiles de velocidad en desarrollo.

Código fuente y documento quedan en el proyecto local. El ejecutable candidato se encuentra en:
D:\researchs\SolarisPKN-Paste\builds\raid-hardware-final-candidate-20261107\SolarisPKN-Paste.exe

## Referencias
- Código fuente CrystalDiskInfo (MIT): https://github.com/hiyohiyo/CrystalDiskInfo
- Microsoft Get-PhysicalDisk: https://learn.microsoft.com/en-us/powershell/module/storage/get-physicaldisk
- Microsoft Get-VirtualDisk: https://learn.microsoft.com/en-us/powershell/module/storage/get-virtualdisk
