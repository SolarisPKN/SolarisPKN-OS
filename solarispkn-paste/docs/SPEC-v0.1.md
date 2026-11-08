# SolarisPKN-Paste — especificación funcional y de seguridad v0.1

Fecha: 2026-11-06. Estado: **prototipo alpha compilado, con 11 pruebas automáticas PASS; integración interactiva pendiente**.

## 1. Interacción

- Clic derecho sobre el fondo de una carpeta => «SolarisPKN-Paste», que ejecuta pegado hacia esa ruta.
- Clic derecho sobre una carpeta => pegado hacia esa carpeta, no su padre.
- Hover: submenú con toggles mostrando estado **actual** y configuración. Preferir `IExplorerCommand` y `EnumSubCommands` nativo. El clic en padre y la expansión de submenú pueden tener comportamiento incompatible en algunas versiones; si no puede existir simultáneamente, ofrecer entradas hermanas «SolarisPKN-Paste» y «Opciones SolarisPKN-Paste». No prometer hover hasta test sobre Windows 11 real.
- En Windows 11 los comandos clásicos pueden aparecer en «Mostrar más opciones»; para el menú moderno usar COM con identidad de paquete.
- Atajos: detectados por un residente por usuario (no servicio SYSTEM). `RegisterHotKey` cuando sea viable; `WH_MOUSE_LL` / Raw Input para XBUTTON1/XBUTTON2 con consentimiento de usuario; no capturar pulsaciones permanentemente, no registrar teclas en logs. ESC = abortar asignación. Validar colisiones con atajos del sistema.

## 2. Lectura del portapapeles

- Interpretar objetos reales del sistema de archivos con `CF_HDROP`; soportar copiar uno o varios archivos/carpetas.
- `CFSTR_PREFERREDDROPEFFECT` informa Move (Cortar) o Copy (Copiar). No inferir el estado por color de icono o por texto. Comunicar el resultado al Shell mediante los formatos correspondientes, para no dejar el portapapeles en estado inconsistente.
- Si portapapeles ofrece archivos virtuales `FILEDESCRIPTOR`/`FILECONTENTS`, no usarlos hasta implementar un adaptador con pruebas (OneDrive/ZIP/e-mail); no tratarlos como rutas locales.
- Texto e imágenes como portapapeles se diseñarán después como modos distintos (no se presupone nombre de fichero).

## 3. Planificador

Entradas: snapshot del portapapeles, ruta de destino, opciones persistidas.
Salidas: acciones `NEW`, `UPDATE`, `DELETE_ORPHAN`, `SKIP`, `ERROR`, con ruta relativa, motivo, tamaño, mtime y, de aplicarse, hash BLAKE3.
- Comparar rutas relativas, normalizadas de forma case-insensitive en Windows; detectar colisiones y rutas reservadas.
- Recursión opcional (por defecto activada): preservar subdirectorios y directorios vacíos, sin seguir symlinks/junctions ni reparse points.
- Cortar origen=destino o anidamiento fuente/destino => detener.
- Verificar que los datos de previsualización no cambiaron entre análisis y aplicación; ante modificación, regenerar la vista o abortar.

## 4. Reglas de filtros

| Opción | En archivo que ya existe en destino | En archivo nuevo |
|---|---|---|
| Sin filtros | permite actualizar | copiar |
| Más reciente | origen.mtimeUtc > destino.mtimeUtc | copiar |
| Más antiguo | origen.mtimeUtc < destino.mtimeUtc | copiar |
| BLAKE3 distinto | hash(origen) != hash(destino) | copiar |
| Tamaño distinto | tamaño(origen) != tamaño(destino) | copiar |
| Solo existentes | no altera decisión de actualización | **no copiar** |
| Eliminar huérfanos | no cambia comparación de los comunes | identifica archivos exclusivos del destino para eliminación |

Si hay múltiples filtros comparativos habilitados, exigir que **todos** se cumplan (AND), salvo «más recientes/antiguos» que son mutuamente excluyentes. La UI debe explicar esto y evitar confusiones como activar BLAKE3 y tamaño a la vez: si tamaño coincide pero contenido es diferente, ese archivo se omitirá por la condición de tamaño.

## 5. Ámbito de huérfanos

«Huérfano» = existe en destino dentro del ámbito de sincronización y no existe como ruta relativa de origen en ese mismo ámbito.

Ejemplo:
- Origen: `C:\hola\papa.txt`, `C:\hola\papel.txt`
- Destino: `D:\papa.txt`, `D:\zanahoria.txt`
- Plan: `papa.txt` actualizar u omitir según filtros; `papel.txt` NEW; `zanahoria.txt` DELETE_ORPHAN.

La operación exige mostrar **la raíz exacta** que será inspeccionada; copiar solo una selección parcial podría hacer que archivos no seleccionados aparezcan como huérfanos. Por eso, cuando la fuente sea una selección de archivos y NO una carpeta completa, se requiere confirmación crítica separada que liste el ámbito aun si «Pegar sin confirmar» está habilitado. No permitir eliminar dentro de carpetas de sistema, destinos sensibles, rutas UNC remotas o raíz de volumen sin evaluación de riesgo específica.

## 6. Ejecución segura

1. Capturar configuración y portapapeles; generar manifiesto inmutable + IDs de operación.
2. Presentar preview: verde nuevo, amarillo actualizar, rojo eliminar, gris omitir; contadores, bytes y raíz.
3. Hacer preflight de permisos, espacio, colisiones, rutas vinculadas y revalidación de plan.
4. Copiar a nombre temporal único **en el volumen de destino**, flush y verificar bytes/hash.
5. Mantener copia de respaldo de archivos sobreescritos; rename atómico cuando el sistema de archivos lo admita.
6. Solo si TODAS las copias del plan finalizaron sin errores, ejecutar eliminaciones de huérfanos hacia Papelera.
7. En `Cut`, eliminar los originales **solo tras verificación** individual. Ante cualquier error, conservar el origen; registrar operación parcial. Nunca eliminar originales que fueron omitidos.
8. Guardar log JSONL sin contenido de archivos ni secretos; operaciones/errores/fechas, reversión desde backups y/o Papelera.

No prometer atomicidad global: NTFS/redes/discos distintos no permiten transacciones multiarchivo universales. La aplicación debe indicar operaciones parciales y recuperables.

## 7. BLAKE3

- Hash de archivos completos y por streaming con memoria acotada.
- Comparación de bytes del digest, no solo diferencias de mtime/tamaño.
- Vectores oficiales y prueba contra hash externo, incluidos datos vacíos, 1 KiB, varios GiB y archivos que cambian mientras se leen.
- `Blake3` 3.0.2 de NuGet para .NET 8+, bajo BSD-2-Clause, sin cargar ni portar el módulo de kernel de SolarisPKN-Verity.
- Sincronización de hash NO ofrece autenticidad, firma ni confianza por sí sola.

## 8. Distribución y seguridad
- Build self-contained `win-x64` y, después de CI, opcional `win-arm64`.
- Instalador .exe con registro HKCU, desinstalador que quite clases y atajos, sin privilegios admin salvo elección explícita.
- No inyectar código complejo ni hashing al proceso Explorer.
- Política de doble confirmación para borrado, cortar y volumen sensible; historial y recuperación.
- No mezclar permisos del Guardian de SolarisPKN-IA con permisos de un usuario final. Integración opcional con Solaris/Guardian solo mediante adaptador y consentimiento.

## 9. Implementación del prototipo de 2026-11-06

El código alpha ya fue compilado en Windows x64 con .NET 10 como EXE autocontenido. La UI real del menú moderno Win11 sigue pendiente; se implementa el **menú clásico con dos entradas separadas**, una de pegado directo y otra de submenú de opciones, sin prometer hover dentro de la fila principal.

### Antifrag

- Por defecto activo. Cada instancia que pega hacia la misma **raíz de unidad** se bloquea sobre un `Mutex` Windows compartido (`Local\SolarisPKN-Paste-Drive-<hash de root>`), en orden de adquisición no garantizado FIFO estricto.
- Las operaciones a C: y D: pueden ser paralelas. Si ambos corresponden al mismo disco físico, la utilidad **no** detecta este detalle.
- La vista previa puede coexistir con otras ventanas y el usuario puede cancelar la espera. No existe persistencia de cola tras reinicio ni gestión de copiar/pegar de programas externos.
- No es un desfragmentador; solo serializa las tareas de este programa y puede mejorar o perjudicar throughput según HDD/SSD y patrón de acceso.

### Solo pegar y conflictos

- «Solo pegar» apaga DeleteOrphans, NewerOnly, OlderOnly, HashDifferent, SizeDifferent y ExistingOnly. No toca AntiFrag ni AskOnConflict.
- «Preguntar conflictos» compara BLAKE3 de archivos comunes. Los idénticos se omiten y los diferentes reciben la marca de conflicto en la previsualización.
- Cada fila tiene selección (checkbox) y un desplegable con Automático, Reemplazar, Omitir y Conservar ambos. «Elegir…» bloquea la ejecución hasta tomar una decisión.
- «Aplicar a todos» cambia únicamente los conflictos de actualización; «No preguntar más» desactiva las preguntas futuras, NO las confirmaciones críticas.
- Vista previa: verde nuevos, amarillo actualizar, rojo borrar huérfanos, gris omitir; botón «Comparar» con tamaño, fechas UTC, rutas y digests BLAKE3.

### Riesgos pendientes

- No existe QA del menú nativo Windows 11 ni del ciclo de vida del portapapeles Cortar. Los archivos en una carpeta cortada se mueven individualmente, pero todavía no se reproduce fielmente el movimiento de carpetas vacías.
- Revalidación del plan por metadatos no proporciona aislamiento transaccional frente a cambios concurrentes durante la escritura; usar primero con carpetas de prueba.
- No desfragmenta unidades; el nombre Antifrag describe una cola de operaciones, no una garantía del sistema de archivos.
- Desinstalación suprime HKCU pero el binario requiere eliminación manual cuando se cierra.

### Pruebas verificadas

Auto-test local, 2026-11-06, build self-contained Windows x64: **11 PASS / 0 FAIL**. Valida planificación y BLAKE3, cola por raíz (distinción de claves), copia real con origen intacto y rechazo de plan obsoleto. **No valida** estrés paralelo, GUI interactiva, Cortar real, enlaces y rutas UNC ni integración moderna.
