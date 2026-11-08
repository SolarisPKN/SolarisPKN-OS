# SolarisPKN-Paste: una sola entrada en el Explorador

## Windows 10 / 11 (menú contextual clásico)

La integración del Explorador se registra en **carpetas, fondo de carpetas y unidades** con una única entrada superior:

```text
SolarisPKN-Paste · Pegar   ›
    Pegar ahora
    Borrar huérfanos
    Pegar más recientes / antiguos
    Comparar BLAKE3 / tamaño
    Solo archivos existentes
    Solo pegar · limpiar filtros
    Preguntar conflictos
    Antifrag por unidad
    Configuración y ayuda...
    Pegar con UAC...
    ⚠ Pegar sin confirmar
```

* Las opciones son subcomandos. Navegar por ellas **no ejecuta** el pegado.
* «Pegar ahora» abre la vista previa, con verificaciones y confirmaciones existentes.
* «Pegar con UAC» es una acción deliberada y separada: pide elevación. No es un modo de mayor velocidad.
* Las opciones con riesgo conservan sus confirmaciones propias, aunque estén anidadas.
* Las opciones por usuario muestran ☑/☐ según su estado. Las instaladas bajo HKLM usan «Cambiar:» porque un proceso normal no puede reescribir HKLM al alternar ajustes.
* El instalador limpia las antiguas entradas superiores «Opciones», «Pegar con UAC» y «Configuración» al migrar el ámbito correspondiente. No se borran archivos ni backups.

## Dos tiempos de hover distintos

**Apertura del submenú:** usa la cascada nativa de Windows. El sistema operativo determina el tiempo de apertura y no hay una opción segura/permenú del Registro para fijar exactamente 3 segundos; **no modificamos la velocidad global del mouse o de todos los menús de Windows**.

**Ayuda de cada opción:** dentro de «Configuración y ayuda», cada casilla muestra una descripción en un globo de ayuda al permanecer el cursor **2 segundos**. Esto es un ToolTip de WinForms.

**Ayuda sobre las propias filas del menú clásico del Explorador:** queda pendiente. La registración estática por verbos de Registro no controla tooltips con retardo independiente. Una futura extensión COM `IExplorerCommand` puede implementar `GetToolTip`, y para aparecer en el menú moderno de Windows 11 requiere integración de aplicación empaquetada y firma. Hasta implementar y probar esa extensión no afirmamos que las filas del menú clásico muestran ayuda tras 2 segundos.

## Actualización en equipos ya instalados

El cambio modifica el registro del menú. Actualizar el código fuente o copiar un EXE no basta: **volvé a ejecutar el instalador** en el mismo ámbito que la instalación anterior (usuario o todos los usuarios) para volver a registrar el menú.

Windows 11: en el menú contextual moderno, seleccioná **Mostrar más opciones** para ver esta integración clásica. En instalaciones mixtas antiguas HKCU/HKLM pueden persistir entradas del ámbito anterior; desinstalá la integración antigua del ámbito que no uses antes de reinstalar.

## Pruebas y límites

El test `--installer-selftest` valida el modelo de acciones y rutas sin escribir el Registro; GitHub Actions compila el EXE y ejecuta ese test. Una inspección visual real de Explorer y del UAC debe hacerse interactuando en Windows; las CI no prueban un cursor humano ni garantizan los 3 segundos de apertura.
