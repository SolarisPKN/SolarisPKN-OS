# SolarisPKN-Paste · Plan de 13 mejoras aprobado (11/11/2026)

> Estado real por implementación. No confundir código escrito con funciones probadas en Explorer ni instalar automáticamente módulos experimentales.

## Fases, alcance y aceptación

| Nº | Mejora | Estado (11/11) | Criterio antes de activarla por defecto |
|---|---|---|---|
| 01 | Reanudación de copia interrumpida | Pendiente | Reinicio simulado, verificación de origen y de todos los segmentos del temporal; no publicar un archivo incompleto |
| 02 | Continuar ante errores | **Primera implementación optativa** | Un archivo bloqueado no interrumpe la copia de otro, informa operación parcial y nunca elimina originales; aún falta test USB desconectado y red |
| 03 | AutoTune 2.0 | Perfil básico preexistente; 2.0 pendiente | A/B repetidas por tipo y tamaño de archivo; fallback conservador, sin activación sin evidencia |
| 04 | Turbo archivos pequeños | Pipeline acotado preexistente | Medir enumeración y metadatos frente a FastCopy con 1k, 6k y 100k archivos sintéticos; cero errores BLAKE3 |
| 05 | HDD Batch 2.0 | Motor experimental preexistente | Pruebas frías misma unidad HDD vs motor serial y FastCopy; no activar si hay regresión |
| 06 | Duplicación estilo Explorador | **Archivos y carpetas no vacías implementados en pruebas sintéticas** | Validar selección múltiple, carpetas vacías, Windows 11 Explorer real, conflictos, copias simultáneas y enlaces |
| 07 | Metadatos y sparse | Pendiente (CopyFile2 conserva parte de metadatos) | Validación NTFS/ReFS/exFAT, ADS, ACL, EFS, sparse, hardlinks y modo degradado explícito |
| 08 | VSS | Pendiente | Snapshot consistente y liberado correctamente; ningún borrado/commit si snapshot falla |
| 09 | SMB/NAS | Pendiente | Comprobar capacidades reales del servidor y activar server-side copy solo con prueba de integridad y fallback |
| 10 | Block Clone / Reflink | Pendiente | Gate por FSCTL/volumen compatible, Copy-on-Write verificado; no asumir soporte NTFS |
| 11 | USN Journal | Pendiente | Identificador del journal, recuperación por discontinuidad y full scan cuando falte historial |
| 12 | Windows 11 Shell moderno | Menú clásico preexistente; COM moderno pendiente | Instalación reversible, sin bloquear Explorer, prueba de clipboard en sesión de usuario |
| 13 | Benchmark competitivo | Harness interno preexistente; suite competitiva pendiente | FastCopy/TeraCopy/Robocopy/Explorer medidos con semántica y verificación equivalentes, datos sintéticos aislados, mínimo cinco rondas y JSON auditable |

## Regla de seguridad del motor
- NO se cambió el ejecutable instalado del usuario: versión alpha.8 en una ubicación personalizada.
- Los experimentos 01/03/05/07/08/09/10/11 no se habilitan por defecto. No confiar en clones/reflinks, snapshots o logs incompletos como evidencia de integridad.
- Ninguna optimización puede deshabilitar silenciosamente el BLAKE3 del modo seguro.
- **Modo 02** se habilita explícitamente en Ajustes y se rechaza por código con `Cortar` o `Borrar huérfanos`; cada fallo queda en el journal y no se informa un éxito falso.
- Pruebas de escritura exclusivamente en sandboxes de laboratorio con datos sintéticos, nunca en archivos personales ni sin una reserva de espacio conocida.

## Evidencia en Windows 11, sesión aislada de mantenimiento
- Stage 1: build y publicación autocontenida correctos, 0 advertencias; 101/101 selftests; 15/15 pruebas sintéticas del portapapeles; pruebas de registro del menú e instalador sin errores.
- Stage 2: publicación autocontenida correcta; 101/101 selftests; 19/19 integración sintética del portapapeles; shell-registry e installer selftest EXIT 0.
- El caso de fallo se simuló manteniendo abierto un archivo con FileShare.None; el segundo archivo se transfirió y el origen bloqueado se conservó.
- **NO probado aún:** portapapeles real de Explorer, menú visible en sesión de usuario, instalación del nuevo EXE, archivos vacíos de carpetas duplicadas, cortes de energía, carga de miles de archivos en los nuevos modos y rendimiento comparativo nuevo. Por eso la aplicación sigue siendo alfa.

## Secuencia sugerida después del arreglo base
1. Stage 3: reanudación con journal duradero y política de errores USB/red.
2. Stage 4: medición automatizada + AutoTune 2.0 + optimizaciones archivos pequeños/HDD (solo habilitar ganadores).
3. Stage 5: compatibilidad NTFS sparse/ACL/VSS, SMB server-side, ReFS Block Clone, USN (módulos independientes).
4. Stage 6: integración moderna Win11 COM y QA interactiva del instalador.

**Estado del PR:** stage1 en #15, stage2 separado. La instalación existente continúa sin cambios hasta el test interactivo.
