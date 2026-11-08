# SolarisPKN-Paste — Visual vs Ultra rendimiento

**Fecha:** 2026-11-07 (Argentina). **Versión:** 0.2.0-alpha.8.

## Dos ritmos de progreso

El modo Visual debe tener un gráfico animado, parecido al del Explorador de Windows. Los cinco segundos pertenecen EXCLUSIVAMENTE al modo Ultra rendimiento.

| Capacidad | Visual | Ultra rendimiento |
|---|---|---|
| Timer WinForms | **250 ms (4 Hz)** | **5000 ms (0,2 Hz)** |
| Barra, porcentaje, archivos, ETA | Sí | Sí |
| Velocidad | EMA suavizada con constante ~1,2 s | Promedio de 5 s |
| Gráfico verde GDI+ | Sí, desplazamiento hacia la izquierda | No |
| Historial | 240 muestras × 250 ms = 60 s | Ninguno |
| Nombre de archivo actual | Sí | No |
| Verificación BLAKE3 / staging / journal | Sí | Sí |

## Implementación

- TransferProgress.IntervalFor(ultra) selecciona el ritmo de refresco de WinForms sin afectar a los mecanismos de copia.
- TransferProgressWindow lee solamente snapshots de contadores atómicos en memoria: ningún acceso a disco, filesystem, CPU, controlador o firmware es necesario para mostrar el progreso.
- La velocidad Visual usa una media móvil exponencial dependiente del tiempo y amortigua picos de pequeños archivos sin falsear los datos transferidos.
- SpeedHistoryGraph usa 240 muestras de frecuencia fija, puntos separados 250 ms y muestra nueva a la derecha: el trazo se desplaza progresivamente hacia la izquierda. Pintura GDI+ con DoubleBuffered, Antialias y relleno verde.
- Ultra no redibuja ni mantiene una serie histórica; actualiza sus números una vez cada 5 segundos.
- Cancelar responde inmediatamente; el timer no condiciona la cancelación. El porcentaje solo alcanza 100% una vez que la copia ha pasado las verificaciones finales.

## Rendimiento y límites de las pruebas

Visual sí hace más trabajo gráfico que Ultra. Eso no significa que la velocidad física del disco mejore proporcionalmente al desactivar la animación: el almacenamiento y la verificación pueden dominar la operación.

El autotest genera 24 fotogramas consecutivos del gráfico de 550×190 px mediante DrawToBitmap con historial lleno. Registra tiempo de ese lote y verifica píxeles verdes en el bitmap. Este test mide pintura GDI+ **en memoria**, no FPS/consumo real en un escritorio compositor ni diferencia de throughput durante una operación visible. La ventana interactiva y su botón Cancelar siguen pendientes de QA manual.

En la versión anterior se midieron 6000 archivos D HDD -> E SSD con telemetría atómica a 5 segundos: 17,867 s promedio sin contadores y 17,674 s con contadores. Ese benchmark NO compara directamente los dos ritmos visuales de esta versión, por lo cual no se declara ventaja de velocidad numérica sin más ensayos.

## Instalación y observación GDI+ (alpha.8)

- Binario instalado: D:\Programas\SolarisPKN-Paste\SolarisPKN-Paste.exe, versión **0.2.0-alpha.8**, SHA-256 **DDF5C0D694D129E2A27E7597CD836A170BA168AAB48321AD722696C972EF173E**.
- Backup exacto alpha.7: D:\researchs\SolarisPKN-Paste\backups\SolarisPKN-Paste-before-0.2.0-alpha.8-20261107.exe.
- Selftest definitivo **51 PASS / 0 FAIL** como SYSTEM y **51 PASS / 0 FAIL** como usuario estándar. Ningún test visual abrió una ventana sobre el escritorio.
- Microbenchmark de **24 pintados GDI+ a memoria** (550×190 px): **70,788 ms bajo SYSTEM** y **114,950 ms con el usuario estándar**, aproximadamente **2,95–4,79 ms por frame**. Es un test sintético de DrawToBitmap, no costo exacto de la ventana real ni consumo de GPU. A 4 fotogramas/s, Visual hace más trabajo que Ultra, pero falta A/B de velocidad de copia con GUI visible.
- Registros/menú contextual y configuración del usuario conservados, sin reiniciar Explorer. Datos sintéticos de benchmarks anteriores permanecen eliminados.

## QA y seguridad

- 4 nuevos tests: ritmos de 250/5000 ms, EMA, historial 60s/240 muestras y dibujo GDI+ validado; acumulables con las 47 pruebas de alpha7.
- No se alteran BLAKE3, temporales, conflictos, journal, backups, cancelación, límite FAT32 ni reserva >=10 GiB libres en C:.
- Sigue siendo versión alfa hasta completar QA en el Explorador, ACL/ADS completos, caída de energía y estrés/recuperación.
- Archivos de implementación: src/TransferProgress.cs, src/TransferProgressWindow.cs, src/Program.cs y src/SelfTest.cs.

Sin drivers kernel ni servicios nuevos. Selector en Ajustes -> Visualización durante copia.
