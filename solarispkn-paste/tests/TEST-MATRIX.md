# SolarisPKN-Paste — matriz mínima de aceptación

Estado: **51/51 PASS en selftest automatizado limitado 2026-11-07 (build 0.2.0-alpha.8)**. La matriz de aceptación completa continúa abierta: NO marcar como PASS las pruebas interactivas o peligrosas que aún no fueron ejecutadas.

| Caso | Expectativa |
|---|---|
| Copy simple, origen intacto | NEW/UPDATE según filtro; origen no se toca |
| Cut simple entre unidades | destino verificado; origen eliminado solo luego |
| Copiar en misma carpeta | detectar identidad; no auto-destructivo |
| Cut con error de escritura | origen intacto, log parcial |
| Borrar huérfanos con ejemplo papa/papel/zanahoria | zanahoria eliminado únicamente en destino |
| Borrar huérfanos con selección parcial | confirmación crítica del ámbito, incluso NoConfirm |
| Más recientes vs más antiguos | excluir ambos simultáneos |
| Reloj distinto/mtime igual | comparación estricta UTC; no actualizar por fecha sola |
| Hash BLAKE3 mismo tamaño, distinto contenido | actualiza en modo BLAKE3 |
| Tamaño mismo, distinto hash | omite si solo tamaño |
| Solo existentes | no crea nuevos archivos |
| Solo existentes + borrar huérfanos | informa claramente efecto doble en preview |
| Filtrado múltiples opciones | AND claramente explicado |
| Symlink/junction en origen o destino | ignorar o bloquear explícitamente, nunca seguir |
| Clipboard CF_HDROP + Preferreddropeffect | distinguir copy vs move |
| Portapapeles de objeto virtual | error explicativo, no simular rutas |
| Archivo ocupado, permisos insuficientes | no borrar fuente ni huérfanos |
| Destino cambia después del preview | abortar y recalcular plan |
| Recycle Bin no disponible | no borrar permanentemente sin nueva autorización |
| Ruta UNC, raíz disco, System32 | bloqueo de borrado por seguridad |
| Windows 10 shell | comandos clásicos funcionales |
| Windows 11 shell moderno | COM + identidad, no solo Show More Options |
| Click principal y hover de opciones | comportamiento probado visual y funcional |
| Atajo teclado Ctrl+Alt+P | activa en carpeta destino correcta |
| Atajo Mouse XBUTTON1/XBUTTON2 | captura, activación y cancelación Esc |
| Uninstall | quita registro, ejecutable y residente sin archivos sobrantes |
| BLAKE3 vectores oficiales | comparación bit a bit |
| Archivos de 10+ GiB | memoria limitada, progreso/cancelar funcional |

## Evidencia selftest automatizado 2026-11-06

Compilación/publish .NET 10 Windows x64 y ejecución en sandbox local de `SolarisPKN-Paste.exe --selftest-headless`. **Primera iteración: 11 PASS, 0 FAIL**:
- plan nuevo verde; reemplazo amarillo; huérfano rojo; conflicto BLAKE3 distinto;
- C: y D: usan distintas claves de unidad (no prueba exclusión bajo carga);
- Solo pegar apaga los seis filtros;
- igual/diferente BLAKE3, tamaño igual;
- copia real verificada en sandbox sin tocar fuente;
- plan con destino cambiado después del análisis se rechaza antes de sobrescribir.

Falta ejecutar manualmente: instalación en sesión de usuario, Windows 10/11 shell, funcionamiento de bandeja y botones laterales, Cut real, huérfanos a Papelera, fallos de escritura, estrés concurrente de colas, cancelación y rollback.

## Evidencia adicional 0.2.0-alpha.3 (2026-11-06)

**Resultado final:** `23 PASS / 0 FAIL` en EXE Windows x64 autocontenido. Se añadieron:
- BLAKE3 verificado en `SmallFileTransfer` y en modo `StrictSmallFileDurability`;
- Backup por sobrescritura en disco de destino, sin consumir C: para D/E;
- Exclusión de carpeta `.solarispkn-recovery` al buscar huérfanos;
- Pipeline acotado de 256 archivos pequeños con checksums BLAKE3 completos y cero temporales residuales;
- Checks existentes para archivo 9 MiB, vacío y cambios tras vista previa.

**Benchmark aparte:** 6.000 archivos D: HDD -> E: SSD, Solaris 37,955 y 37,118 s con pipeline; vs 43,067 s sin pipeline; FastCopy 16,267 s con verificación integrada en la última pasada. Datos sintéticos eliminados. Ver `docs/AUDITORIA-MOTORES-2026-11-06.md`.

**No se marcó como PASS la QA que no se ejecutó:** Windows 11 moderno COM, integración de menú en sesión interactiva, Ctrl+X de carpetas, Corte/rollback en fallas, pérdida de energía, ACL/ADS, enlaces simbólicos maliciosos, estrés del mutex Antifrag, y colisiones concurrentes con otros procesos. Mantener versión alfa.

## Evidencia nueva alpha.4 (6/11/2026)

- 28 PASS/0 FAIL (base + 3 perfiles de discos C/D/E detectados + validación pipeline 256 archivos con 4 consumidores + BLAKE3).
- La última compilación comprueba NTFS C: clúster 4KiB/NVMe, D: clúster 64KiB/HDD, E: clúster 4KiB/SSD; transportes D/E reportados RAID por el driver.
- En el mismo dataset de 6.000 archivos D: -> E:, las pruebas 1/2/4/8 consumidores, reutilización de handle y auto están en D:\researchs\SolarisPKN-Paste\reports\.
- El usuario puede seleccionar 0=auto o 1-8 escritores en Ajustes. El modo HDD se limita a uno. El motor rápido no desactiva BLAKE3.
- Persisten pendientes GUI interactiva/Windows 11, operaciones CUT, colisiones de filesystem, NTFS ADS/ACL/reparse, cortes de energía, USB exFAT/FAT32, y benchmarks NVMe grandes con cola profunda.
- Consultar docs/HARDWARE-FILESYSTEM-ADAPTIVE-2026-11-06.md.

## Evidencia alpha5 (6/11/2026)

- Build .NET 10 Windows x64 autocontenida: **35 PASS / 0 FAIL**.
- Nuevas pruebas de regla de tamaño FAT32 (4 GiB menos 1 byte) y aceptación teórica 10 GB para exFAT/NTFS (no se montó USB real).
- CopyFile2 en 9 MiB buffered/no-buffering, BLAKE3 del stream principal, preservación ADS NTFS y ruta integrada PasteEngine CopyFile2 staging+verificación+origen conservado.
- Prueba 10 GB D: HDD a E: SSD con tres métodos, datos sintéticos borrados; ver docs/BENCHMARK-COPYFILE2-2026-11-06.md.
- CopyFile2 permanece DESACTIVADO por defecto porque fue más lento end-to-end que el motor asíncrono en 10 GB; QA de ACL, ADS completo, directorios especiales, kernel y pruebas de falla pendientes.


## QA adicional alpha6 — 6 de noviembre de 2026

**40 PASS / 0 FAIL** sobre Windows .NET 10 x64 autocontenido. Nuevas pruebas:
- Mover un archivo de 9 MiB mediante rename en mismo volumen NTFS sin transferir bytes ni perder BLAKE3.
- Conservar flujo alternativo NTFS del archivo Cut.
- Inyectar una colisión durante lote de dos renames; rollback del primero sin sobrescribir el archivo creado por otro proceso.
- Carpeta mixta: 256 pequeños + 9 MiB grande, selección conservadora para HDD->HDD y datos correctos.
- BLAKE3 del archivo grande después del pipeline, origen intacto.
- SelfTest bajo TEMP de usuario para no escribir al lado del EXE.

**Evidencia de rendimiento adicional:** 1 GiB D HDD->C NVMe y C NVMe->E SSD, Cut metadata de 1 GiB en 33,8 ms; 6.001 archivos mixtos 49,762 s sin pipeline vs 23,229 / 23,808 s con pipeline real; Linux WSL2 ext4/9P y Robocopy /MT:4 /MT:8 con verificación externa. Documentado en docs/LAB-ADAPTIVE-IO-WINDOWS-WSL-2026-11-06.md.

**No se validó**: interacción manual Explorer, rollback ante fallos eléctricos, ACL/ADS/EFS en todas las copias, symlinks maliciosos bajo carrera, Cut de carpeta entera, pruebas destructivas en archivos reales, clonado/reflink en ReFS, WSL sobre Linux nativo, ni transferencias de alta carga prolongadas. Mantener alfa.


## QA de progreso Visual/Ultra, alpha.7

- 47 PASS / 0 FAIL, con 7 pruebas nuevas de contadores atómicos, intervalo de 5000 ms, avance parcial y final, verificación BLAKE3 y Cut metadata sin velocidad falsamente grande.
- 6000 archivos de D HDD a E SSD, misma arquitectura y BLAKE3, dos pasadas alternadas sin contadores vs dos con contadores/snapshot periódico, sin penalización grande observable: docs/UI-PROGRESS-PERFORMANCE-2026-11-06.md.
- No se probó aún de manera interactiva con capturas GUI el gráfico real, botón Cancelar o comportamiento de ventanas mientras otro software mantiene el foco. Marcar como PENDIENTE, no como aprobado.

## Pruebas Visual en tiempo real vs Ultra a 5 segundos — alpha.8 (2026-11-07)

- Visual: 250 ms por muestra (4 Hz), gráfico verde GDI+ con scroll de muestras a la izquierda, 240 muestras/60 s, velocidad suavizada; Ultra: 5000 ms sin gráfico.
- 4 nuevas pruebas: selección independiente de intervalos, suavizado EMA creciente/decreciente, límite circular de 240 muestras, **render real DrawToBitmap GDI+** con píxeles verdes. Las 47 pruebas anteriores permanecen.
- No inferir transferencia más rápida por menor frecuencia: el core es idéntico. Aún pendiente una comparación de throughput con ventanas reales en primer plano, pruebas de Alt+Tab/minimización y cancelación manual.
- Informe: docs/PROGRESS-VISUAL-ULTRA-2026-11-07.md.


## Benchmark competitivo, ramas experimentales NO instaladas (2026-11-07)

- El último release instalado sigue siendo alpha.8 y su baseline de **51/51 PASS**. Las nuevas fuentes experimentales (SmallFilePipelineSplit y lectores 2/4) pasaron **60/60 PASS** en pruebas bajo SYSTEM, NO equivalen a release certificado.
- 6000 archivos D HDD -> E SSD: Solaris original 18,288 / 18,421 s; cuatro lectores 17,968 / 17,878 s; FastCopy 5.12 con /verify 19,950 / 18,620 s. FastCopy log: VerifyFiles=6000, cero errores.
- Pipeline de verificación separado fue más lento (19,494 / 19,551 s), default OFF.
- C NVMe -> E SSD: 1 lector frío 43,980 s; 4 lectores calientes 19,853 s; repetición 1 lector caliente 19,844 s; falsa aceleración de cache confirmada.
- Decisión: conservar un lector por defecto y no activar nuevas ramas experimentales. Archivos de sandbox C/D/E eliminados, informes JSON conservados D:.
- Evidencia: docs/FASTCOPY-COMPETITIVE-LAB-2026-11-07.md.
