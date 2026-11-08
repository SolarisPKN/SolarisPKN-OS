# SolarisPKN-Updates · módulo independiente / reusable release gate

**Estado:** 0.1.0 alpha; primera integración: SolarisPKN-Paste (Windows).
No instalador automático se ejecuta todavía. La fuente `stable.json` está
**vacía** hasta que exista una release realmente aprobada.

## Decisión de arquitectura

1. **Cada repositorio/producto prueba su propio código.** Paste conserva el
   workflow de Windows, Debian y Ubuntu. Las aplicaciones futuras podrán
   referenciar este módulo como biblioteca, sin copiar código ni mantener otro
   sistema de actualización.
2. **SolarisPKN-Updates valida candidatos** mediante
   `.github/workflows/solaris-updates.yml`: contratos de compatibilidad,
   Windows Server 2022/2025, hashes, origen GitHub y versiones.
   Cuando los tres jobs (Windows, Debian y Ubuntu) de Paste terminan
   correctamente en `main`, el job `updates-candidate` invoca
   directamente el [workflow reutilizable](../.github/workflows/solaris-updates-candidate.yml)
   en la **misma ejecución de GitHub Actions**; descarga y analiza su
   artefacto exacto. El reporte queda en esa ejecución, sin depender de
   eventos posteriores. Todo es de **solo lectura**: no crea releases.
3. **GitHub Actions NO es el repositorio de actualizaciones de usuarios**:
   sus artefactos son temporales. Usaremos **GitHub Releases** con activos
   permanentes y manifiestos en `solarispkn-updates/channels/`.
4. **El cliente** usa `stable.json`, no el último workflow ni la última rama,
   y filtra `approved=true`, CI exitoso declarado, versión más reciente,
   Windows, arquitectura y build de OS, URL de Releases y SHA-256.
   Descarga en `%LOCALAPPDATA%\SolarisPKN\Updates\<producto>\downloads\`
   sin tocar la instalación actual; un hash incorrecto cancela la descarga.
5. **Promoción a estable:** antes de publicar una entrada de catálogo hace
   falta verificar manualmente el run SHA real, la identidad del paquete,
   una firma Authenticode válida, pruebas de instalación Windows 10/11
   (incluidas UAC y rollback) y revisión/aprobación humana. Un campo JSON
   `validation.passed=true` no demuestra que se hayan ejecutado esas pruebas:
   el control debe verificarse también en el pipeline de publicación.

## Workflow de calificación de una release real

[solaris-updates-qualify-release.yml](../.github/workflows/solaris-updates-qualify-release.yml)
se inicia **manualmente** sobre `main` indicando el producto, la versión,
el tag GitHub Release ya publicado y el ID del run que compiló en `main`.
El workflow ejecuta verificaciones en Windows Server 2022 y 2025 y exige:

- Run de Paste correcto, de este mismo repositorio, sobre la rama `main`.
- Release pública **no prerelease ni draft**.
- Firma Authenticode **Valid** con huella de publicador anclada a la
  variable del repositorio `SOLARISPKN_WINDOWS_SIGNER_THUMBPRINT`;
  si no está configurada, el proceso falla **cerrado**.
- Atestación Sigstore/GitHub que verifique **exactamente el EXE firmado**
  descargado desde Releases.
- Autotests sin manipular instalaciones locales, más hash y tamaño.

El workflow produce `stable-proposal-windows-2022.json` y
`stable-proposal-windows-2025.json`, pero **no escribe el catálogo estable,
no publica releases ni instala nada a usuarios**. Los dos informes deben
coincidir y revisarse, además de exigir QA interactiva en Windows 10/11 y
rollback. Un mantenedor incorpora la entrada a `stable.json` mediante
PR y revisión. El entorno `stable-release-review` debe configurarse
expresamente con revisores obligatorios en GitHub: nombrar un entorno en YAML
**no crea por sí mismo protección de aprobación**.

**Bloqueo actual:** no hay certificado Authenticode de editor configurado
ni una atestación del instalador *firmado*. Por eso actualmente el workflow
no puede aprobar una release, por diseño. Se requiere firma del binario
**antes** de generar su atestación de procedencia.

## Frecuencias en Paste

| Configuración | Comportamiento |
| --- | --- |
| Desactivadas | Ninguna búsqueda automática |
| Solo manual (predeterminado) | El usuario presiona «Buscar ahora» |
| Al iniciar sesión | Consulta al iniciar el **residente de Paste** |
| Semanal | Consulta al iniciar/residir si transcurrieron 7 días desde la última búsqueda correcta |

El residente hace una comprobación silenciosa cada cuatro horas para
evaluar la opción semanal. No se ejecutan tareas del Programador de Windows:
si el residente no se ejecuta, la actualización no se consultará sola.
Con la versión actual el usuario recibe aviso y puede descargar y verificar
el archivo, pero **NO se ejecuta ningún instalador descargado**.

La configuración vive en `%LOCALAPPDATA%\SolarisPKN\Updates\solarispkn-paste\settings.json`,
fuera del archivo de configuración del motor de copia.

## Licencia y multiplataforma

Biblioteca Windows: GPL-3.0-only (módulo independiente). En Debian/Ubuntu,
los paquetes instalados desde un repositorio firmado deben actualizarse
mediante **APT**, sin un actualizador que ejecute scripts como root o
reemplace archivos propiedad de dpkg. Se contempla luego soporte de
`apt policy` y actualizaciones del paquete sin duplicar el gestor de
paquetes del sistema.

## Bloqueos reales antes de auto-instalar

- El Setup actual de Windows se distribuye **sin firma de editor**.
  Hash SHA-256 y HTTPS son comprobaciones de integridad, no sustituyen
  la identidad certificada de un publicador.
- Falta certificado Authenticode, identidad anclada en el actualizador,
  verificación segura de confianza, procedimiento de rotación de claves,
  instalador transaccional, cierre del residente, restauración/rollback y
  pruebas interactivas de Windows 10/11.
- Falta un workflow de **promoción de release** con entorno protegido,
  firma/atestación verificada y aprobación. La CI actual produce candidatos
  *no aprobados*, nunca autoriza una actualización por sí sola.
- No hay forma de prometer compatibilidad Windows 10/11 basándose solamente
  en GitHub-hosted Windows Server 2022/2025.
- La firma del propio manifiesto debe verificarse o la publicación debe
  protegerse con controles de GitHub; todavía no hay una firma de catálogo
  verificada por el cliente.

En particular: **no fusionar «build pasó» con «release autorizada»**.

## Para probar

En GitHub Actions, `SolarisPKN-Updates - policy, OS matrix and release candidates`
valida feed, pruebas negativas y ejecuta:

```powershell
dotnet build solarispkn-paste/src/SolarisPKN.Paste.csproj -c Release
dotnet solarispkn-paste/src/bin/Release/net10.0-windows/SolarisPKN-Paste.dll --updates-selftest
```

Dentro de Paste: `Configuración > MÓDULOS EXTERNOS > SolarisPKN-Updates`,
o `SolarisPKN-Paste.exe --updates`, o bandeja > SolarisPKN-Updates.

La ausencia de entradas en estable significa «no hay update aprobada»,
**no** que todos los últimos builds ya sean seguros.

## Contrato JSON de release

Una entrada aprobada incluye (ejemplo conceptual, NO publicado en estable):

```json
{
  "version": "0.4.0",
  "approved": true,
  "notes": "Notas revisadas",
  "validation": {"passed": true, "runId": 123456, "sourceSha": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"},
  "asset": {
    "platform": "windows", "architecture": "x64",
    "minOsBuild": 19045, "maxOsBuild": 0,
    "fileName": "SolarisPKN-Paste-Setup.exe",
    "url": "https://github.com/SolarisPKN/SolarisPKN-OS/releases/download/paste-v0.4.0/SolarisPKN-Paste-Setup.exe",
    "sha256": "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
    "bytes": 1000000
  }
}
```

El objeto vive en `products[].releases[]` de `stable.json`. Esos IDs
son **ilustrativos**, no un release real.
