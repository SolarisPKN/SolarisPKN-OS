# SolarisPKN-Paste — benchmark de copia D: (HDD) → E: (SSD)

**Fecha:** 6 de noviembre de 2026. **Estado:** pruebas sintéticas ejecutadas, datos eliminados. **Resultados de una sola pasada:** exploratorios, NO ranking estadístico definitivo.

## Entorno

- **C:** SSD NVMe WDC PC SN530 de 256 GB, deliberadamente excluido de los archivos grandes. Final: **24.886.358.016 bytes libres** (>10 GB).
- **D:** HDD TOSHIBA HDWQ140 ~4 TB, origen del benchmark, disco físico #2.
- **E:** SSD GIGABYTE GP-GSTFS31480GNTD ~480 GB, destino, disco físico #1.
- **Carga A:** 6.000 archivos sintéticos de 4, 8, 12 y 16 KiB (61.440.000 bytes en total).
- **Carga B:** 1 archivo sintético de 10.000.000.000 bytes (10 GB decimales / ~9,31 GiB).
- Origen exclusivamente bajo \`D:\researchs\SolarisPKN-Paste\benchmarks\benchmark-20261106\`; destino exclusivamente bajo \`E:\SolarisPKN-Paste-Benchmark\benchmark-20261106\`. Ambas raíces tuvieron sentinel de propiedad.
- Operaciones secuenciales para no competir por disco; cada prueba recibió carpeta vacía, comprobación de tamaño/cantidad y test de integridad descrito abajo. No se manipularon archivos personales.

## Resultados de 6.000 archivos chicos

| Programa y tipo de prueba | Copia / operación (s) | Verificación adicional (s) | Tiempo integral (s) | Rendimiento integral (MB/s) |
|---|---:|---:|---:|---:|
| FastCopy **5.12.0**, /verify integrado | — | integrada | **18,096** | **3,396** |
| TeraCopy **4.0.3.2 portable** | 18,452 | 20,830 SHA-256 externo | **39,283** | **1,564** |
| .NET/Windows File.Copy + BLAKE3 de fuente/destino | — | incluida | **54,803** | **1,121** |
| SolarisPKN-Paste (motor asíncrono + BLAKE3) | — | incluida | **55,782** | **1,101** |

TeraCopy tiene una copia pura de **18,452 s**; su tiempo de **39,283 s** suma la verificación externa posterior. FastCopy reportó **No Errors, VerifyFiles=6.000**. SolarisPKN-Paste ejecutó comprobación BLAKE3 durante y después del copiado. La copia de referencia .NET no es un benchmark de la interfaz del Explorador, sino un baseline programático.

## Resultados de archivo grande de 10 GB

| Programa y tipo de prueba | Copia sin verificación separada (s) | Verificación externa (s) | Total reportado (s) | Velocidad del total (MB/s) |
|---|---:|---:|---:|---:|
| **SolarisPKN-Paste**, motor rápido con BLAKE3 fuente/temporario | no desglosada | incluida | **85,771** | **116,59** |
| **FastCopy 5.12.0**, /verify integrado | no desglosada | incluida | **96,817** | **103,29** |
| **TeraCopy 4.0.3.2**, copia pura + SHA-256 externo | **62,801** | **468,606** | **531,407** | **18,82** |

- FastCopy: registro íntegro confirma **No Errors**, **VerifyFiles=1**, **ErrFiles=0**, archivo final exacto de 10.000.000.000 bytes. El wrapper PowerShell no expuso el exit code correctamente; se conservó el falso reporte de fallo inicial para auditoría y se creó reporte reconciliado por evidencia. La duración medida fue **96,8166771 s**.
- SolarisPKN-Paste: \`RUN_PASS\`, hash BLAKE3 comparado, archivo destino verificado, duración **85,771 s**. El archivo temporal fue eliminado después de la operación.
- TeraCopy: \`EXIT=0\` y archivo copiado exacto de 10 GB en **62,801 s**. El verificador separado **PowerShell Get-FileHash SHA256** leyó origen y destino completos, coincidencia de digest, **468,606 s adicionales**. NO atribuir esta lenta verificación al motor de TeraCopy: prueba métodos de verificación muy diferentes.
- Un solo ensayo; orden, caché del sistema, antivirus, fragmentación del disco y actividad de aplicaciones no se controlaron completamente. **No afirmar que un producto es globalmente más rápido**.

## Hallazgos

1. **6.000 archivos pequeños:** FastCopy v5.12.0 fue ~3,08× más rápido que nuestro motor por tiempo total (55,782 / 18,096). TeraCopy copió en tiempo similar al de FastCopy, pero su integridad se comprobó con SHA-256 externo.
2. **Archivo grande:** SolarisPKN-Paste tardó ~11,41% menos que FastCopy en este ensayo (85,771 frente a 96,817 s) con verificadores integrados distintos. Sin repetición no es una ventaja concluyente.
3. **TeraCopy copia pura:** 62,801 s es una buena línea de base de transferencia sin incluir la verificación externa.
4. **Sobre el nombre Antifrag:** la cola agrupa por disco físico cuando es posible; no desfragmenta NTFS ni controla los procesos ajenos.
5. **Espacio en C:** no se generaron archivos grandes allí. Solo registros auxiliares pequeños del sistema/software; se conserva mucho más que el mínimo exigido.

## Próximos pasos de ingeniería (no implementados en esta ronda)

- **P1 — Archivos pequeños:** perfil optimizado con escrituras en lotes y menos sincronizaciones/renames/logs por archivo; mantener verificación y evaluar opción de durabilidad estricta. El cuello probable es el costo por archivo, no el rendimiento BLAKE3.
- **P2 — Benchmark reproducible:** tres o más pasadas por programa, orden aleatorio, caché y temperatura controladas, separación exacta del tiempo de copia y hash; incorporar percentiles, CPU, RAM, IOPS, latencia, HDD/SSD y transferencia cruzada SSD↔HDD.
- **P3 — Integración:** probar Windows Explorer interactivo, Cut, recuperación ante fallos, pruebas de saturación/cola, archivos enlazados/ACL/ADS/USB/SMB y checkouts que requieran permisos del usuario.
- **P4 — Comparación de verificación:** seleccionar el mismo hash y política de integridad en todos los programas cuando la configuración lo permita. Evitar comparar un \`Get-FileHash\` SHA-256 externo con el verificador integrado de FastCopy como si fueran idénticos.

## Descargas

- \`D:\researchs\FastCopy\FastCopy5.12.0_installer.exe\` y portable extraído a \`D:\researchs\FastCopy\FastCopy5.12.0_x64\`.
- \`D:\researchs\TeraCopy\teracopy4.0.5.exe\` (instalador **no ejecutado**) y versión portable oficial \`D:\researchs\TeraCopy\portable\TeraCopy\TeraCopy.exe\` (versión **4.0.3.2**, usada en pruebas).
- El ejecutable actual de SolarisPKN-Paste está instalado en \`D:\Programas\SolarisPKN-Paste\SolarisPKN-Paste.exe\`. Se conservó la integración de HKCU, no se cambió el controlador predeterminado de copias de Windows.
- Registros, JSON por prueba y scripts de ejecución permanecen bajo \`D:\researchs\SolarisPKN-Paste\reports\`.

## Limpieza verificada

A las 19:35:20 hora de Argentina, se ejecutó \`--bench cleanup\` con salida 0. Verificado:
- \`D:\researchs\SolarisPKN-Paste\benchmarks\benchmark-20261106\` **no existe**.
- \`E:\SolarisPKN-Paste-Benchmark\benchmark-20261106\` **no existe**.
- Se eliminó la tarea programada temporal usada para lanzar TeraCopy en la sesión interactiva.
- Espacio libre final: C: **24.886.358.016 bytes**, D: **1.982.530.387.968 bytes**, E: **112.958.681.088 bytes**.
- Los informes y programas de investigación se conservaron en D:. Ningún archivo personal se eliminó como parte de la limpieza.
