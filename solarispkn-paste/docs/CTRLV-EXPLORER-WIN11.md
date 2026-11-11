# SolarisPKN-Paste — Ctrl+V opcional en el Explorador (Windows 11)

**Estado:** candidato experimental, sin instalación ni verificación interactiva del hook todavía.
**Implementación:** `ExplorerPasteShortcutPolicy.cs`, `TrayResident.cs`, `Program.cs`, `PasteEngine.cs`.

## Comportamiento solicitado

| Acción | Resultado |
|---|---|
| Activar "Usar SolarisPKN-Paste con Ctrl+V en carpetas del Explorador" | El residente intercepta Ctrl+V **solo** al pegar archivos físicos en una carpeta real del Explorador. El atajo personalizado anterior se limpia. |
| Copiar un texto y Ctrl+V en Explorer | El atajo nativo sigue siendo el que recibe el evento. |
| Ctrl+V en Word, Opera, VS Code, consola, aplicaciones empresariales | Windows conserva el comportamiento normal; la aplicación no interviene. |
| Ctrl+V dentro del campo de edición de nombre, búsqueda o barra de dirección | No se intercepta cuando Windows identifica un control editable. |
| Desactivar la casilla | Se libera el hook del residente y el atajo Windows vuelve a operar normalmente. El atajo Solaris queda **Sin asignar**. |
| Volver a asignar otro atajo | El usuario puede pulsar una combinación nueva o botón lateral del mouse. Ctrl+V solo puede usarse con la casilla especial. |
| Cerrar residente | Se libera el hook. Windows no fue modificado en el Registro ni deshabilitado globalmente. |

## Diseño y seguridad

- Un **solo residente por sesión**, protegido con mutex `Local\\SolarisPKN-Paste-Tray-1`.
- Modo opt-in **apagado por defecto**; nunca usa `RegisterHotKey(Ctrl+V)` global.
- Hook `WH_KEYBOARD_LL` procesa rápidamente la pulsación V y solo consulta el estado Ctrl/Alt/Shift/Win, clase de ventana, foco y disponibilidad de `CF_HDROP`. **No guarda ni registra pulsaciones ni contenido del portapapeles**.
- Se ignoran eventos de teclado inyectados, otros modificadores y otras aplicaciones.
- La operación pesada (resolver carpeta de Explorer, abrir vista previa y copiar) se **despacha por PostMessage al hilo de UI**; no se ejecuta dentro del hook.
- Un `FileSystemWatcher` aplica cambios de ajustes sin reiniciar Windows; falla de instalación del hook significa no interceptar.
- Un proceso de Settings puede iniciar el residente del usuario al habilitar la casilla; no instala un servicio, no se ejecuta como SYSTEM, ni crea un atajo de arranque sin pedirlo.
- No pretende "apagar Ctrl+V de Windows": solo consume eventos del Explorador dentro del alcance explicado. **Sin residente, Ctrl+V queda nativo automáticamente**.
- Las políticas corporativas de Windows Defender / WDAC / EDR pueden impedir usar el hook. No intentar eludirlas.

## QA realizado en laboratorio Windows 11

- Build y publicación .NET 10 Windows x64 autocontenida: **PASS**, sin advertencias (repetir tras últimas modificaciones).
- `--clipboard-integration-test`: **36 PASS / 0 FAIL**, con 10 verificaciones nuevas de alcance, texto, teclas inyectadas y edición de nombres.
- `--selftest-headless`: **101 PASS / 0 FAIL**.
- `--shell-registry-test` y `--installer-selftest`: exit 0.
- **Pendiente de prueba humana real**: Ctrl+V físico en carpeta Explorer, Focus Win11, activación/desactivación en tiempo real, reinicio de residente, interacción con antivirus/EDR; no se afirma que estas operaciones estén verificadas.

## Prueba manual segura

1. En una carpeta vacía y local de laboratorio, usá un archivo de texto **sintético**, sin datos laborales ni personales.
2. Iniciá `SolarisPKN-Paste.exe --settings` como usuario normal y marcá la casilla. Guardá.
3. Abrí otra ventana del Explorador en el destino, copiá el archivo ficticio con Ctrl+C y presioná Ctrl+V.
4. Esperado: se abre la vista previa de Solaris. Confirmá `COPIAR`, verificá que el origen siga intacto.
5. Abrí Bloc de notas, copiá y pegá texto: debe funcionar Ctrl+V nativo.
6. Volvé a Ajustes, desmarcá la casilla, guardá. Ahora Ctrl+V en Explorer debería ser el nativo y el atajo de Solaris aparecer **Sin asignar**.
7. Si se observa cualquier regresión, desde la bandeja de Solaris elegí **Salir**: el hook se desregistra. No hace falta cambiar configuración del sistema.

Fuentes técnicas:
- Microsoft: `LowLevelKeyboardProc` — https://learn.microsoft.com/en-us/windows/win32/winmsg/lowlevelkeyboardproc
- Microsoft: `GetGUIThreadInfo` — https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getguithreadinfo
- Microsoft: formatos del portapapeles Shell y `CF_HDROP` — https://learn.microsoft.com/en-us/windows/win32/shell/clipboard
