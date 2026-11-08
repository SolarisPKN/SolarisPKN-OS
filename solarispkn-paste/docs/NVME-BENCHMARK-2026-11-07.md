# SolarisPKN-Paste — NVMe WD PC SN530 256 GB, 7 noviembre 2026

Estado: investigación experimental, NO instalada ni activada para pegados de usuario.
Evidencia primaria: D:\researchs\SolarisPKN-Paste\reports\nvme-physical-20261107\ (JSON y XML).

## Hardware verificado
- NVMe C: WDC PC SN530 SDBPNPZ-256G-1002, 256 GB, firmware 21106000.
- Windows 10 IoT Enterprise LTSC 19044, i3-10105 4c/8t, ASUS PRIME B560M-A.
- Microsoft Standard NVM Express Controller, CurrentLinkSpeed=3 y CurrentLinkWidth=4: enlace PCIe Gen3 x4. No limitado a x1/x2.
- Estado Healthy/OK, temperatura 29–32 C en DiskSpd; TRIM (DisableDeleteNotify=0); BitLocker no activo.
- C: ~27.6 GB disponibles, 10,8% del volumen, al comenzar/terminar. Fixture de 2 GiB en 74 extensiones NTFS. Son factores a investigar, NO causa probada de bajo rendimiento.

## Valores WD, no confundir pico y sostenido
Fuente oficial: https://downloads.sandisk.com/downloads/um/pcsn530-pm.pdf
Para PC SN530 256GB: hasta 2.400 MB/s lectura, hasta 950 MB/s escritura pico, 220 MB/s escritura sostenida medida con Iometer de 100% LBA (otra carga), 170K IOPS lectura aleatoria y 120K IOPS escritura aleatoria máximos en condiciones de fabricante. Endurance 200TBW.

## DiskSpd 2.3 oficial de Microsoft
Ejecutable firmado Authenticode VALID. SHA256 DD4E57E1E8CCAF5D6437938F8AAB7F17E9A1E6D8FBA8A093006B7CADF16FAEA2.
Solo un fichero sintético en C:\SolarisPKN-Paste-NVMeLab-20261107\nvme-test-2GiB.bin protegido con sentinel; no se apuntó al volumen o dispositivo físico. Todos los 10 perfiles PASS. 6s medición, 1s calentamiento, sin caché de software (-Su); escrituras además write-through (-Sh).

| Perfil | MB/s decimales | IOPS |
|---|---:|---:|
| Secuencial leer 1MiB QD1 | 1300.23 | 1240 |
| Secuencial leer 1MiB QD8 | **1372.41** | 1308.83 |
| Secuencial leer 128KiB QD32 | 1079.66 | 8237.17 |
| Secuencial escribir 1MiB QD1 | 917.85 | 875.33 |
| Secuencial escribir 1MiB QD8 | **946.51** | 902.67 |
| Secuencial escribir 128KiB QD32 | 944.90 | 7209 |
| Aleatorio leer 4KiB QD1 | 41.69 | 10179 |
| Aleatorio leer 4KiB QD32 (4x8) | 43.63 | 10652.5 |
| Aleatorio escribir 4KiB QD1 | 107.69 | 26291.5 |
| Aleatorio escribir 4KiB QD32 (4x8) | 114.94 | 28061.33 |

6s NO permiten afirmar velocidad de escritura sostenida a largo plazo: puede actuar la caché SLC nCache 3.0. Bajo rendimiento frente a ficha técnica en lectura y aleatorios NO demuestra fallo: difieren espacio libre, archivo de 74 extensiones, controlador, tamaño del dataset y condiciones del fabricante. QD32 no ayudó bajo esta carga concreta.

## Copia 2 GiB del NVMe C al mismo NVMe C con SolarisPKN-Paste
Mismo fichero, staging temporal y FlushToDisk, BLAKE3 completo dentro del tiempo y SHA256 completo externo fuera de tiempo. Tres motores finalizaron PASS; una sola ronda, no ranking final.

| Motor | Segundos | MB/s útiles | SHA256 externo s |
|---|---:|---:|---:|
| FileStream 4MiB | 10.104 | 212.54 | 77.596 |
| CopyFile2 con búfer/flush | 15.275 | 140.59 | 51.121 |
| Overlapped 2MiB, QD4 | **6.916** | **310.53** | 76.610 |

CopyFile2: llamada inicial 1.217s, FlushToDisk 8.090s, BLAKE3 5.966s. El tiempo inicial de API no es velocidad física persistida. La cuarta modalidad, NativeDirectRead4, se inició pero la operación administrativa excedió su límite y no generó PASS/FAIL, por lo cual no se cuenta.

## Decisiones para el motor
- C NVMe -> E SATA y E SATA -> C NVMe: CopyFile2 buffered+flush+BLAKE3 ya tiene una variante adaptativa experimental opt-in para archivos nuevos >=1GiB.
- C NVMe -> C NVMe: Overlapped 2MiB QD4 es candidato por esta ronda; **NO cambiar default** antes de 5+ rondas alternadas, ensayo de caché frío y varios tamaños y comprobaciones de archivos especiales.
- Archivos diminutos: mejorar metadata/IOPS/archivos por segundo, evitando maximizar solo MB/s y sin prometer el techo secuencial con 6000 archivos independientes.
- No activar herramientas DiskSpd de Windows 11 (BypassIO/IoRing) en Windows 10 IoT LTSC.
- No mover ni borrar datos personales para liberar espacio; evaluar mantener más margen libre C para la caché dinámica SLC, sin atribuirle causalidad hasta medirlo.

## Seguridad / limpieza
- DiskSpd 2.3 instalado solo como herramienta de laboratorio en D:\researchs\SolarisPKN-Paste\tools\diskspd-2.3.
- Nueva fuente experimental NvmeSameDiskRunner.cs con subcomando run-nvme en BenchmarkRunner.cs. Compilación auto-contenida D:\researchs\SolarisPKN-Paste\builds\nvme-same-disk-candidate; build y --selftest-headless código 0.
- Se verificó ausencia de procesos Paste y el sentinel; los temporales en C:\SolarisPKN-Paste-NVMeLab-20261107 se eliminaron completamente. Espacio libre final C: 27.613.843.456 bytes. Reportes quedan en D:.
- No se cambió D:\Programas\SolarisPKN-Paste\SolarisPKN-Paste.exe ni el settings.json.
