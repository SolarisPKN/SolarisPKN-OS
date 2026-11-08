# Investigación de motores de copia: FastCopy vs TeraCopy
Fecha: 2026-11-06. SolarisPKN-Paste. **Fuentes públicas, documentación oficial; sin desmontar ni ejecutar software propietario.**

## Material descargado a D:\researchs
- FastCopy 5.12.0 (oficial https://fastcopy.jp/archive/FastCopy5.12.0_installer.exe): \`D:\researchs\FastCopy\FastCopy5.12.0_installer.exe\` (5.189.304 bytes), SHA-256 \`14CCC9D2EBBDFBCBCF0C6E4004773D12BB98DE197A572959473A2D9D3B000E3D\`, firma Authenticode válida.
- TeraCopy 4.0.5 (oficial https://codesector.com/files/teracopy4.0.5.exe): \`D:\researchs\TeraCopy\teracopy4.0.5.exe\` (15.405.824 bytes), SHA-256 \`22F050B51C80A344CBDAF368C7F6A9266EFCA4D95B5109D606466906A421FF7E\`, firma Authenticode válida.
- FastCopy fuente histórica GPLv3 (https://github.com/shirouzu/FastCopy/archive/refs/heads/master.zip): \`D:\researchs\FastCopy\legacy-source-GPLv3.zip\` (606.957 bytes), SHA-256 \`FC328C2850A2FFA660A7231728F29E5E7A6A4417815FF54F950AD0C13FDAD9CB\`.
- Ningún EXE externo fue instalado ni ejecutado. Firma válida confirma firma verificable en el host, no equivalencia de rendimiento ni ausencia de fallos.

## Observaciones verificadas
FastCopy 5.12.0 (https://fastcopy.jp/help/fastcopy_eng.htm):
- Selecciona algoritmos según dispositivo de origen/destino y si comparten HDD. Para HDD mismo dispositivo: lecturas por lotes y escrituras por lotes, para otros: lectura/escritura asíncronas concurrentes.
- Aplica I/O asynchronous/overlapped, buffers grandes y en casos específicos lectura no cacheada. Soporta nombres largos y filtros. Publica mejora del 20-30% en entornos SSD particulares con preasignación privilegiada: **afirmación del fabricante, no rendimiento garantizado**.
- FastCopy v5.12 es propietario, la licencia actual prohíbe ingeniería inversa y exige Pro en lugares de trabajo. La rama FastCopy v3 histórica declara GPLv3: usar sus fuentes en nuestro programa implicaría revisión legal/licenciamiento, por lo que **no copiamos código GPLv3**.

TeraCopy 4.0.5 (https://codesector.com/teracopy y https://support.codesector.com/en/articles/8789942-copying-and-verifying-files):
- Colas, historial, reintentar operaciones fallidas/omitidas, gestionar conflictos, transferencias multihilo, verificación.
- Realiza hash de origen durante el copiado y compara con hash leído del destino después, con modos de integridad.
- Soporta BLAKE3 y xxHash, checksum files, informes, múltiples destinos en planes de pago.
- Pro/Business es la licencia adecuada para uso comercial según matriz oficial. Su interfaz no es código público reutilizable.
- En octubre de 2026 4.0.5 corrigió un posible deadlock en progreso, y comprobaciones de espacio al mover dentro de la misma unidad; podemos tomar estos casos como advertencias de diseño (https://blog.codesector.com/).

## Cambios originales implementados en SolarisPKN-Paste (fuentes, pendientes QA del build nuevo)
- TransferEngine.cs: FileStream.ReadAsync/WriteAsync con dos buffers del ArrayPool, lectura anticipada y cálculo incremental de BLAKE3. Hash del destino re-leído para validar integridad. Flush a disco.
- PreallocationSize opcional con fallback por sistema de archivos. Buffers ajustables 1-8 MiB; archivo pequeño usa 128 KiB.
- StorageTopology.cs: IOCTL_STORAGE_GET_DEVICE_NUMBER para identificar dispositivo físico, IOCTL_STORAGE_QUERY_PROPERTY para estimar HDD con seek penalty. Fallback por unidad si Windows/driver impiden consultar.
- PasteQueue.cs: Antifrag usa mismo mutex para particiones de igual dispositivo físico cuando identificación es posible, y por unidad si no es posible. No coordina Explorer ni programas externos.
- PasteEngine.cs: reintentos acotados solo en etapa temporal previa al reemplazo y sin borrar origen. Archivo temporal se verifica antes de ser movido a destino.
- Ajustes: motor rápido, preasignación, buffer; sigue verificando BLAKE3 incluso en ruta de copia legacy.

## Limitaciones, riesgos y plan de test
- No se implementó FileFlagNoBuffering ni Direct I/O: requiere alineación de buffers, tamaños y offsets sectoriales y tiene alto riesgo de regresión en USB/red. Medir antes de activarlo.
- No copiar hilos en paralelo dentro del mismo HDD por defecto: podría ser más lento; la estrategia se elige por topología con fallback conservador.
- El uso de \`PreallocationSize\` no equivale a SetFileValidData ni requiere privilegios: no omitimos inicialización de datos ni debilitamos seguridad.
- No se realizaron pruebas de tiempo comparativas contra FastCopy/TeraCopy externos: no afirmar «más rápido».
- Se necesitan tests reales: 0 bytes, 1 MiB, >4 MiB, HDD/SSD/USB/SMB, cortes de energía, cancelación, reintentos, disco lleno, permisos, symlink/junction, CUT, huérfanos, envío a Papelera, performance cronometrada con caché controlada.
- Guardar logs y archivos temporales fuera de rutas críticas; evitar excluir Microsoft Defender del análisis por defecto.

## Próxima evaluación
Primero obtener 0 errores de compilación y selftest; después instalar solo en equipo de prueba; comparar a igualdad de carga y verificación activada: Explorer, FastCopy, TeraCopy y SolarisPKN-Paste. No dar por completado QA interactivo ni lanzar a producción prematuramente.
