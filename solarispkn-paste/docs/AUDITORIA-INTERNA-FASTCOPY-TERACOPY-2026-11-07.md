# Investigación de código y arquitectura: FastCopy y TeraCopy
Fecha: 2026-11-07, Argentina. Proyecto: SolarisPKN-Paste.

## Fuentes y límites de acceso

**FastCopy:** se clonó el repositorio oficial https://github.com/shirouzu/FastCopy en D:\researchs\FastCopy\source-code (solo investigación). SHA de commit 72655443c775af09a69cc2fd2aa6a602b4d987c9; último cambio 2019-02-25, versión 3.63. El código es GPLv3; no se copió ningún fragmento al proyecto Solaris. **No confundir este código de 2019 con la implementación no publicada de FastCopy 5.12.0 de 2026.** La ayuda de esa versión sí está disponible: https://fastcopy.jp/help/fastcopy_eng.htm.

**TeraCopy:** el portátil instalado corresponde a versión 4.0.3 según su Whatsnew.txt. Es de código cerrado y su License.txt prohíbe descompilar o desensamblar: análisis restringido a los archivos/configuración accesibles y a documentación pública del fabricante. No se realizó ingeniería inversa del ejecutable ni de las bibliotecas. La versión 4.0.5 fue anunciada el 2026-10-08 y no es la versión comparada aquí.

## FastCopy 3.63: qué dice realmente el C++

- **Subprocesos:** src/fastcopy.cpp líneas 895-914 lanza hilos para abrir archivos, leer, escribir y, al activar verificación, dos hilos de digest de origen y destino.
- **Cola de apertura:** src/fastcopyr.cpp líneas 989-1038, OpenFileQueue y OpenThreadCore; adelantan apertura y reutilizan objetos con listas de elementos libres/ocupados. Interesante cuando abrir archivos por red/SMB cuesta más que leer sus bytes.
- **Lecturas con múltiples operaciones pendientes:** src/fastcopyr.cpp líneas 1503-1595, ReadFileProcCore, utiliza varias estructuras OVERLAPPED y entrega las lecturas realizadas a una cola para escritura.
- **Escrituras múltiples:** src/fastcopyw.cpp líneas 623-744, WriteFileCore, usa solicitudes WriteFile con offsets y controla la finalización. Alinea tamaño a sectores para I/O sin caché y corrige longitud al final.
- **Hash independiente del lector/escritor:** src/fastcopy.cpp líneas 1367-1414, RDigestThreadCore, procesa digest del origen; líneas 1428-1525, WDigestThreadCore, compara destino y origen; src/fastcopyw.cpp líneas 806-841, CheckDigests, coordina la cola.
- **HDD igual vs distinto:** src/fastcopy.cpp líneas 390-418 y 1709-1743, así como src/fastcopyr.cpp 32-104, detectan disco compartido y cambian de lectura a escritura por lotes en el mismo HDD. En discos diferentes solapan las etapas.
- **E/S adaptativa:** src/fastcopyr.cpp líneas 1344-1392 y src/fastcopyw.cpp 350-410 reducen el tamaño solicitado ante ERROR_NO_SYSTEM_RESOURCES y reintentan. Es robustez, no necesariamente una aceleración.
- **Direct I/O y enumeración:** src/fastcopyr.cpp 1042-1097 configura CreateFileW con FILE_FLAG_NO_BUFFERING, FILE_FLAG_SEQUENTIAL_SCAN y a veces FILE_FLAG_OVERLAPPED. src/fastcopy.cpp 640 habilita FIND_FIRST_EX_LARGE_FETCH.

La documentación oficial actual de FastCopy 5.12 recomienda I/O unit **2 MiB** con **8 solicitudes overlapped**, verificación diferida, caché de Windows para archivos muy chicos, grupos de discos y afinidad de CPU. Estas recomendaciones del autor son actuales; no atribuir al código 2019 los cambios internos hechos después.

### Arquitectura histórica, simplificada

~~~mermaid
flowchart LR
 A[Enumerar] --> B[Cola abrir archivos]
 B --> C[Lecturas overlapped]
 C --> D[Hash origen]
 D --> E[Escrituras overlapped]
 E --> F[Cola de verificaciones]
 F --> G[Relectura destino y hash]
 G --> H[Comparar]
~~~

## FastCopy 5.12: ajustes REALES de los benchmarks en esta PC

El archivo de configuración del ejecutable utilizado, D:\researchs\FastCopy\FastCopy5.12.0_x64\FastCopy2.ini, confirma estas opciones vigentes:

| Clave original | Valor | Significado |
|---|---:|---|
| bufsize | 512 | Buffer principal de 512 MiB |
| max_ovlnum3 | 8 | Hasta 8 operaciones overlapped |
| max_ovlsize2 | 2 | Unidad de E/S de 2 MiB |
| nonbuf_minsize_ntfs2 | 64 | Umbral de 64 KiB para no buffering sobre NTFS |
| is_readosbuf | 0 | No utilizar caché de Windows en lecturas grandes |
| use_overlapio | 1 | Overlapped I/O activado |
| large_fetch | 1 | Enumeración de directorio con LARGE_FETCH |

Las entradas de verificación presentes en el INI incluyen defaults y perfiles, mientras la ejecución comparada usó explícitamente /verify, confirmado en los logs. No extrapolar el código de 2019 a las instrucciones actuales: **estas opciones pertenecen al binario 5.12**.

Este resultado dirige el próximo prototipo: **Solaris usa búferes pequeños y RandomAccess buffered**, mientras FastCopy dispone de búfer global 512 MiB, operaciones 2 MiB × 8 y lectura directa. Evaluar cache/direct con memoria alineada a sector y fallback seguro; no copiar la implementación GPL.

## TeraCopy 4: evidencia pública y configuración local

Fuente: https://support.codesector.com/en/articles/9905589-options-tab

El fabricante documenta N hilos lectores que calculan checksum, N x número de destinos hilos escritores y un hilo dedicado a reemplazos/actualizaciones de base de datos. TeraCopy 4 incorpora un motor multihilo nuevo, colas y cache SQLite en memoria para las listas y estado de la interfaz. No podemos describir sus estructuras internas porque no existe código fuente disponible.

El Options.ini ORIGINAL de la versión portátil en D:\researchs\TeraCopy\portable\TeraCopy tiene:
- Threads=2; Buffers=10; BufferSize=1048576 (1 MiB).
- HashName=xxHash3-64; TestAlways=0 originalmente.
- El benchmark usa clon independiente con TestAlways=1, no altera el original.

La distribución contiene las bibliotecas firmadas Blake3.dll, xxHashAVX2.dll y xxHashSSE2.dll. La mera presencia **no demuestra cuál biblioteca se ejecutó** en cada transferencia. El fabricante permite seleccionar BLAKE3 y xxHash3: https://support.codesector.com/en/articles/8789943-testing-files-with-teracopy.

## Experimento aislado con buffers, 6.000 archivos pequeños

Se compararon dos copias portátiles independientes de TeraCopy con archivos sintéticos (D HDD -> E SSD), 6.000 × 4–16 KiB, 61.440.000 bytes; Threads=2, Buffers=10, HashName=xxHash3-64 y TestAlways=1 en ambas. Orden de ejecución: 1 MiB, 64 KiB, 64 KiB, 1 MiB. Cada pasada completó 6.000 archivos y 48 muestras SHA-256 independientes correctas, fuera del cronómetro.

| Buffer | 1a pasada | 2a pasada | Promedio |
|---|---:|---:|---:|
| 1 MiB | 22,064 s | 21,296 s | **21,680 s** |
| 64 KiB | 22,367 s | 21,403 s | **21,885 s** |

Diferencia media 0,205 s, **menor que la dispersión entre pasadas**. Reducir el buffer NO produjo mejora demostrable. No concluir que buffer pequeño es malo en todos los discos; solo este escenario.

Datos: D:\researchs\SolarisPKN-Paste\reports\teracopy-buffers-20261107\.

## Comparación con SolarisPKN-Paste

- En src/PasteEngine.cs, el análisis del lote de 6.000 archivos dura aprox. **0,18 s**, menos del 1 % del tiempo completo de 18–21 s. Optimizar solo la enumeración no resolvería el cuello de botella de este dataset.
- En src/SmallFilePipeline.cs, Solaris usa ArrayPool limitado, lectura secuencial, 4 escritores/verificadores por archivo, BLAKE3 de origen/destino y un temporal que se publica SOLO tras verificar. Mantener estas garantías.
- En src/TransferEngine.cs, la copia grande usa FileStream con read-ahead y BLAKE3, con flush y relectura del destino.
- En src/OverlappedCopyEngine.cs, hay un prototipo RandomAccess de depth 2/4/8, pero usa I/O buffered (sin NO_BUFFERING). Por tanto, todavía NO es equivalente a Direct I/O + OVERLAPPED de FastCopy.
- Un prototipo de verificadores separados para pequeños ya resultó más lento. No extrapolar eso a un pipeline de chunks grandes.

## Decisiones y pruebas siguientes, ordenadas por potencial

1. **P1: archivos grandes.** Prototipo independiente con Win32 FILE_FLAG_NO_BUFFERING + FILE_FLAG_OVERLAPPED, memoria alineada a tamaño físico de sector, tail buffered, profundidad 2/4/8, y fallback cuando el FS no lo soporta. A/B 1 GiB y 10 GB, con exactamente el mismo BLAKE3 de origen y destino, staging, flush y recuperación. No instalar sin resultados repetibles.
2. **P2: mismo HDD.** Medir caché interarchivo acotada (64–256 MiB): leer lote, luego escribir lote, para minimizar saltos de cabezal, con BLAKE3 y journaling.
3. **P3: verificación de grandes.** Acumular cola acotada de ficheros temporales listos para verificar, solapando verificación del archivo anterior con lectura del siguiente. Nunca publicar sin digest destino correcto.
4. **P4: robustez y metadatos.** Reintentos reduciendo I/O ante ERROR_NO_SYSTEM_RESOURCES; ACL, ADS, EFS, hardlinks, junctions, sparse files; soporte específico para SMB y cancelación segura.
5. **P5: afinidad CPU y grandes directorios.** Solo si mediciones muestran beneficio: prueba de afinidad separada y de enumeración Win32/LARGE_FETCH frente al enumerador .NET. Con 0,18 s de preanálisis el impacto estimado en 6.000 archivos locales es marginal.

## Límites de seguridad y licencia

**No copiar código GPLv3** en una aplicación de licencia propia sin resolver distribución y obligaciones. **No descompilar TeraCopy**. No desactivar Defender, BLAKE3 o el journal para mejorar un benchmark. No activar SetFileValidData indiscriminadamente: Microsoft alerta que puede exponer información residual de bloques no escritos (https://learn.microsoft.com/windows/win32/api/fileapi/nf-fileapi-setfilevaliddata). El I/O sin caché exige alineación física correcta: https://learn.microsoft.com/en-us/windows/win32/fileio/file-buffering.

**Limpieza verificada:** los 6.000 archivos sintéticos de D/E fueron eliminados mediante marcadores de propiedad, las dos copias portátiles temporales de TeraCopy y las tareas temporales quedaron eliminadas. Se conservaron los cuatro JSON del A/B y el repositorio público de FastCopy en research. C: tenía 23,10 GiB libres al cierre.

**Estado:** FastCopy clonado solo en research, TeraCopy original intacto; cambios experimentales ya existentes en Solaris permanecen desactivados. No se reemplazó el binario instalado alpha.8.
