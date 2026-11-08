# SolarisPKN-Paste · Auditoría de arquitectura y rendimiento FastCopy / TeraCopy

**Fecha:** 2026-11-06 (Argentina). **Proyecto:** SolarisPKN-OS / solarispkn-paste. **Resultado:** prototipo \`0.2.0-alpha.3\` implementado, pruebas automatizadas y A/B de 6.000 archivos ejecutadas.

## 1. Límites, procedencia y licencias

Material ya descargado en \`D:\researchs\FastCopy\` y \`D:\researchs\TeraCopy\`; no se extrajo ni reprodujo código propietario.

- FastCopy **5.12.0**: software actual propietario; su EULA **prohíbe la ingeniería inversa** de la versión actual. Estudiamos su documentación pública y comportamiento observable documentado, **no desensamblamos/decompilamos** binarios de FastCopy v5. La EULA exige licencia Pro para usos laborales/no domésticos. Fuentes: https://fastcopy.jp/help/fastcopy_eng.htm y https://fastcopy.jp/pro/license .
- FastCopy histórico: el archivo \`D:\researchs\FastCopy\legacy-source-GPLv3.zip\` contiene la rama pública anterior con licencia GPLv3; se extrajo solo para lectura en \`D:\researchs\FastCopy\reference-source-readonly\FastCopy-master\`. Se inspeccionaron estructura y nombres de API; **no se incorporó, pegó ni portó código GPL al programa**. Incorporar código requeriría una revisión de licencias.
- TeraCopy portable **4.0.3.2** disponible en \`D:\researchs\TeraCopy\portable\TeraCopy\`; instalador **4.0.5** conservado aparte. No tenemos código fuente de la versión propietaria, por lo que se analizó **documentación del fabricante, comportamiento del programa y release notes**, sin afirmar haber visto su código. https://blog.codesector.com/2026/ y https://support.codesector.com/en/articles/8789942-copying-and-verifying-files .

## 2. Arquitectura FastCopy: inspección descriptiva de código histórico GPL

La rama pública separa responsabilidades en módulos:
- \`src/fastcopyr.cpp\`: enumeración, apertura, lectura de orígenes y consultas de metadatos, incluidos \`FindFirstFileExW\`, \`CreateFileW\` y lectura preparada para I/O overlapped.
- \`src/fastcopyw.cpp\`: preparación de destino, \`WriteFileWithReduce\`, operaciones de escritura y tratamiento de problemas de I/O.
- \`src/fastcopyd.cpp\`: borrado independiente y recorrido de directorios.
- \`src/fastcopy.cpp\`: coordinación, colas y partes de la verificación y planificación; aparecen \`GetOverlappedResult\`, \`FIND_FIRST_EX_LARGE_FETCH\` y flags \`FILE_FLAG_NO_BUFFERING\`.
- \`src/shellext/\`: integración con Windows Shell; separada del motor de E/S.

La separación lectura/escritura mediante etapas es una **idea de arquitectura**, no reutilización de su implementación. La versión FastCopy 5.12 actual documenta, además: unidad I/O por defecto 2 MiB, hasta 8 solicitudes overlapped recomendadas, selección del uso de caché según tamaño, preferencia de cache NTFS para archivos bajo 64 KiB, CPU affinity configurable, grupos de disco físico, y mejoras de seguridad CFG/CET. Estas características no garantizan rendimiento en cualquier dispositivo.

**Fuente principal actual:** https://fastcopy.jp/help/fastcopy_eng.htm

## 3. TeraCopy: arquitectura documentada (NO fuente interna)

TeraCopy 4 documenta:
- Motor multihilo, colas de múltiples operaciones e integración con el menú moderno del Explorador.
- Cálculo de checksum de origen mientras copia y verificación del destino como fase independiente; configuraciones de BLAKE3/xxHash y otros algoritmos.
- Historial y caché SQLite en memoria para responsividad (changelog de TeraCopy 4).
- En 4.0.5 corrigieron un deadlock al actualizar progreso y fallos poco frecuentes con SHA-256/MD5: conviene aislar UI del hilo de transferencia y limitar actualizaciones de progreso. Ese bug del producto no significa que sea el mismo que nuestro software.

Fuentes: https://blog.codesector.com/2026/ ; https://support.codesector.com/en/articles/8789942-copying-and-verifying-files .

## 4. Defectos concretos detectados en SolarisPKN-Paste original

1. \`TransferEngine.CopyVerified\` forzaba \`Flush(flushToDisk:true)\` **por cada archivo**, aunque midiese 4 KiB. Es una barrera de durabilidad cara en NTFS; la documentación de Microsoft explica que \`FlushFileBuffers\` frecuente es ineficiente en muchas escrituras separadas: https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-flushfilebuffers .
2. El ejecutor consultaba la topología física (IOCTLs y handles) para **cada** archivo; se reemplazó por caché de resultado por volumen de origen dentro de cada plan.
3. Cada archivo abría un nuevo handle al journal \`File.AppendAllText\`; ahora el journal permanece abierto y para copias no destructivas nuevas hace flush por lote de 64 líneas. Movimientos, sobreescrituras y borrados siguen flushing por operación.
4. El ejecutor trabajaba **estrictamente de un archivo a otro**. Para casos de archivos nuevos pequeños entre discos diferentes, se añadió pipeline acotado de lectura y escritura independiente.
5. Los backups de sobrescrituras originales se guardaban en \`%LOCALAPPDATA%\SolarisPKN\Paste\backups\` (habitualmente C:): **riesgo real de ocupar C: con archivos gigantes del destino E:/D:.** Ahora los backups están bajo \`<destino>\.solarispkn-recovery\<id>\replaced\...\`. Se excluyen de "borrar huérfanos".
6. Nueva protección: al escribir en C:, un cálculo conservador del volumen de staging+backups bloquea operaciones que dejarían **menos de 10 GiB libres** antes de comenzar.

## 5. Código propio añadido/modificado

- \`src/SmallFileTransfer.cs\`: ruta especializada para archivos <=64 KiB, FileStream síncrono buffered (caché de SO), BLAKE3 de fuente durante lectura, relectura/verificación del temporal. Opción \`StrictSmallFileDurability\` fuerza flush físico por archivo; desactivada por defecto para acelerar.
- \`src/SmallFilePipeline.cs\`: productor secuencial de lectura de HDD + consumidor de SSD con \`Channel\` bounded de 32 elementos y \`ArrayPool<byte>\` (aprox. 2 MiB de datos en vuelo). Verifica BLAKE3 del archivo temporal antes del rename. Solo aplica a **128 o más archivos NUEVOS <=64 KiB**, modo **COPIAR**, nunca en operaciones Cut/Update/huérfanos, y se omite si origen/destino comparten HDD mecánico. Cancelación, devolución de buffers y cleanup de temporales.
- \`src/PasteEngine.cs\`: selección de estrategia, journal agrupado, caché de topología, respaldo en destino, reserva C: y recheck justo antes de mover el temporal. Actualización de progreso GUI cada 128 archivos en pipeline.
- \`src/Program.cs\`: opciones visuales para small-files, pipeline y sincronización física estricta.
- \`src/BenchmarkRunner.cs\`: modos A/B \`fast-strict\`, \`fast-no-pipe\`, \`fast\` y tiempos de análisis/ejecución separados.
- La ruta para archivos grandes conserva su algoritmo asíncrono + BLAKE3; **NO se implementó I/O sin búfer ni IOCP de FastCopy en esta iteración**.

## 6. Pruebas de corrección

\`23 PASS / 0 FAIL\` con \`--selftest-headless\` sobre sandbox aislado:
- 16 pruebas previas de comparaciones, archivo 9 MiB, datos vacíos, plan obsoleto, etc.
- 4 pruebas de ruta small, backup en disco destino, exclusión en huérfanos y flush estricto.
- 3 pruebas de pipeline 256 archivos: completitud, digest BLAKE3 por archivo y sin temporales residuales.

No se ha certificado GUI interactiva Windows 11, cancelación con pérdida de energía, ACL, ADS, hardlinks, Copy/Move masivo ni resistencia ante disco lleno. Evitar llamarlo producción estable.

## 7. Benchmark real: 6.000 archivos de 4–16 KiB (61.440.000 bytes)

Equipo: origen **D: HDD TOSHIBA HDWQ140**, destino **E: SSD Gigabyte**, carpetas temporales bajo raíces específicas con sentinel en D/E; sin usar C: para datasets. Una pasada por modo, caché/Defender no totalmente controlados.

| Motor / configuración | Tiempo (s) | Comentario |
|---|---:|---|
| SolarisPKN-Paste primera versión previa | **55,782** | Hash BLAKE3 |
| Solaris build actual, ruta antigua con sync física | **48,705** | Journal y consultas optimizados |
| Solaris, ruta small-cache sin pipeline | **43,067** | BLAKE3 verificado |
| Solaris, ruta small con pipeline (pasada 1) | **37,955** | BLAKE3 verificado |
| Solaris, ruta small con pipeline (pasada 2) | **37,118** | BLAKE3 verificado |
| FastCopy 5.12 /verify (comparación pasada 1) | **18,096** | Verificación integrada |
| FastCopy 5.12 /verify (comparación pasada 2) | **16,267** | Verificación integrada |
| TeraCopy portable 4.0.3.2 | **18,452 solo copia**; 39,283 con SHA-256 externo | No comparar SHA-256 PowerShell como verificador integrado |

La ruta Solaris actual representa ~**33,5 % menos tiempo** que 55,782 s; FastCopy aún necesita menos de la mitad del tiempo en pequeños. **No afirmar victoria de Solaris sobre FastCopy.**

## 8. Desglose real de tiempo Solaris (última pasada)

\`6.000 archivos\` con pipeline: **0,173 s análisis**, **36,938 s ejecución**, **37,118 s total**.
Por tanto, el planner consume menos del 0,5% del total; la siguiente optimización debe enfocarse en **aperturas/cierres, temp rename, escrituras y verificación** en la transferencia, no solo en enumeración o GUI.

## 9. Qué probar después (NO implementado ni medido)

1. **Win32 de alto rendimiento**: canal de \`ReadFile\`/\`WriteFile\` overlapped con varios requests (medición A/B, sin copiar implementación ajena); IOCP o \`ThreadPoolBoundHandle\` solo si ETW demuestra que vale la pena.
2. **Paquetes de escritura por carpeta**: disminuir opens/renames/atributos. No eliminar temporales ni BLAKE3 por defecto; modo acelerado sin staging solo con consentimiento específico, estado claro de riesgo y reparación de copias truncadas.
3. **Perfilado ETW**: medir actividad de \`CreateFile\`/IRP, Defender, latencias NTFS y CPU de cada fase; no añadir exclusiones antivirus silenciosas.
4. **Separar autenticidad, integridad y durabilidad**: BLAKE3 garantiza igualdad de contenido leído; una relectura desde caché no garantiza persistencia tras corte de energía. \`StrictSmallFileDurability\` fuerza barrera de durabilidad por archivo, con costo significativo.
5. **Repetir 3–5 veces** y alternar orden por herramienta en frío/calentado. La prueba actual contiene una o dos pasadas, no intervalo estadístico.

## 10. Seguridad y licencia (reglas que no se flexibilizan)

- FastCopy 5.12 v5 propietario: sin desensamblado, parcheo ni copia de código. Rama GPL histórica guardada separada y sin merge.
- TeraCopy v4 propietario: solo documentación pública y benchmarking; no supuesto acceso a código fuente.
- No ejecutar ni sustituir el Explorer Shell durante el benchmark; no instalar modificadores predeterminados de terceros.
- Copiar + BLAKE3 de verificación sigue siendo obligatorio en la ruta rápida. La sincronización física del archivo pequeño se activa cuando se requiere tolerancia adicional frente a fallas abruptas.
- Los backups grandes son datos del usuario: NO limpiar automáticamente \`.solarispkn-recovery\` sin plan explícito y confirmación.
- Mantener una copia del instalador/binary anterior para rollback cuando se actualice la instalación de usuario.

**Fuentes de Windows:** https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-findfirstfileexw ; https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-flushfilebuffers ; https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-createfilea .
