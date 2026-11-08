# SolarisPKN-Paste — Progreso cada cinco segundos

**Fecha:** 2026-11-06 (ART). **Versión instalada:** 0.2.0-alpha.7.

## Diseño y prioridades

Dos modos en Ajustes -> Visualización durante copia:
- **Visual:** barra, porcentaje, velocidad MiB/s, archivos terminados, ETA, archivo actual y gráfico verde con hasta 48 muestras de cinco segundos (cuatro minutos).
- **Ultra rendimiento:** mismo contador básico y barra, sin gráfica ni nombre del archivo. La vista es más chica.

**Ambos modos refrescan cada 5000 ms.** Muestra inicial y final fuera de la cadencia normal. El botón Cancelar actúa inmediatamente. Los contadores se acumulan mediante Interlocked/Volatile: no hay consultas al sistema operativo, CPU, filesystem ni disk counters para dibujar progreso. El motor marca transferencias parciales de archivos grandes en memoria y completa los archivos solo después de BLAKE3 y publicación del staging. El pipeline de pequeños actualiza el contador por archivo ya confirmado.

La GUI deja de recibir callbacks por cada archivo o buffer. Un timer de Windows Forms lee TransferProgress.Read() cada 5 s. La gráfica GDI+ solo se redibuja al llegar una muestra. Ultra oculta directamente la gráfica. El progreso no llega al 100% hasta terminar correctamente la operación, incluida la verificación. ETA basada en velocidad reciente: estimación orientativa, no promesa exacta. CUT por rename del mismo volumen se identifica como movimiento de metadatos, nunca como velocidad falsa de miles de MiB/s.

**No se sacrifica seguridad:** BLAKE3, staging, protección de conflictos, backup y journal de recuperación permanecen. Desactivar logs esenciales para conseguir un par de milisegundos es una optimización engañosa. La confirmación de CUT se actualizó para informar que un movimiento por rename no pasa por Papelera.

## Benchmark de sobrecarga del seguimiento

Dataset: 6.000 archivos sintéticos de 4–16 KiB (61.440.000 bytes), D: Toshiba HDD -> E: Gigabyte SSD, pipeline de cuatro escritores con verificación de BLAKE3 de cada temporal. Misma versión de motor y misma configuración de buffering. No hay caché fría controlada.

| Configuración | Primera pasada | Segunda pasada | Promedio |
|---|---:|---:|---:|
| Contadores desactivados (fast-ui0) | 18,578 s | 17,156 s | 17,867 s |
| Contadores activados, lectura cada 5 s (fast-ui5) | 17,532 s | 17,816 s | 17,674 s |

Los resultados se ejecutaron en orden alternado: UI0, UI5, UI5, UI0. Cada prueba con seguimiento tomó tres muestras de contadores durante ~17–18 s. Diferencia media: 0,193 s a favor de la modalidad monitorizada, **menor que la variación entre ejecuciones**. No demuestra que el gráfico acelere, ni permite afirmar coste nulo. Sí muestra que el muestreo atómico no causó una penalización grande en este equipo.

**Límite importante:** benchmark headless de contadores y timer, SIN pintar realmente la ventana GDI+. La prueba manual con UI Windows y cancelación visual sigue pendiente. Una sola pintura por 5 s debería ser liviana pero no es una medición real del consumo GPU/CPU de la ventana.

## Pruebas, código y mantenimiento

Pruebas de la compilación final: **47 PASS / 0 FAIL como SYSTEM y 47 PASS / 0 FAIL con cuenta estándar**; incluye siete pruebas nuevas: intervalo fijo de 5 s, bytes parciales y porcentaje, fase BLAKE3, no contar doble bytes al completar, éxito en 100%, copia de 9 MiB con telemetría y CUT metadata sin falsa velocidad. Compilación definitiva validada. Ventana de progreso normal y minimizable, visible en la barra de tareas y Alt+Tab cuando se oculta la vista previa. Binario alpha7 SHA-256: F471BC5B5BEAE311C601E6C5700E6833A5160560206B0DAAB942AD96899C7D94.

Fuentes de implementación: src/TransferProgress.cs, src/TransferProgressWindow.cs, src/Program.cs, src/PasteEngine.cs, src/TransferEngine.cs, src/BenchmarkRunner.cs y src/SelfTest.cs.

Los JSON de benchmark se guardan en D:\researchs\SolarisPKN-Paste\reports\. Los archivos sintéticos solo se generan en sandboxes D/E con marcadores de propiedad; se deben limpiar al terminar. Mantener siempre al menos 10 GiB libres en C:.

**Estado del producto:** alfa, integración manual al Explorador pendiente de test interactivo y cobertura de fallos abruptos, ACL/ADS completos y cancelación.
