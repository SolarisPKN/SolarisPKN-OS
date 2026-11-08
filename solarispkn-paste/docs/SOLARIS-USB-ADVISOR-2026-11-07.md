# SolarisPKN-Paste · Solaris USB Advisor 🔌⚡

**Fecha de laboratorio:** 7/8 de noviembre de 2026
**Estado:** implementado en código y compilación candidata aislada. **NO se reemplazó ni se modificó la alpha.8 instalada.**

## Funcionalidad

El residente WinForms escucha WM_DEVICECHANGE y agrupa eventos con una espera de 750 ms. En el arranque registra el inventario USB sin notificaciones masivas. Al conectar un dispositivo nuevo, hace una lectura de los hubs de Windows y, cuando hay datos suficientes, muestra una ventana pequeña con mascota Solaris estilizada junto al reloj.

La ventana muestra nombre o VID/PID del dispositivo, velocidad de enlace negociada, capacidades SuperSpeed, puertos lógicos accesibles y libres, y diferencia **teórica** de señalización. Nunca equipara los máximos del USB con velocidad real de archivos ni afirma conocer qué puerto está adelante o atrás del gabinete.

Incluye dos checkboxes persistentes: "No volver a preguntar con este dispositivo/modelo" y "No volver a preguntar con ningún dispositivo"; botones "Expulsar seguro y comprobar" y "Dejarlo así". La expulsión solo se habilita si Windows identifica de forma inequívoca un dispositivo USBSTOR único. Revalida que siga conectado y solicita CM_Request_Device_EjectW bajo clic y confirmación; nunca expulsa automáticamente. Si Windows veta la extracción, informa el error. Luego permite comprobar la nueva velocidad al reconectar.

Las preferencias se guardan localmente en:
%LOCALAPPDATA%\SolarisPKN\Paste\usb-advisor.json

El residente ofrece "Analizar puertos USB ahora" y "Reactivar avisos USB"; Ajustes incluye controles independientes para sugerencias y avisos al detectar un modelo nuevo.

## Escáner técnico

Se usan SetupAPI y GUID_DEVINTERFACE_USB_HUB para obtener handles de hubs, y los IOCTL USB_GET_HUB_INFORMATION_EX, USB_GET_NODE_CONNECTION_INFORMATION_EX, USB_GET_NODE_CONNECTION_INFORMATION_EX_V2, USB_GET_PORT_CONNECTOR_PROPERTIES. Se reconocen conectores compañeros USB2/USB3 para evitar contar dos interfaces del mismo enchufe. Un puerto no se considera disponible si es interno, su estado es desconocido, el companion está ocupado o el controlador no permite confirmar sus propiedades.

La identidad de dispositivo para silenciar avisos se calcula desde VID/PID y se almacena como SHA-256; dispositivos de idéntico modelo pueden compartir preferencia. Está pendiente identificar dispositivos iguales por serie PnP sin divulgar identificadores privados.

## Mediciones reales en esta PC

- Hubs USB inspeccionados: **2**.
- Posiciones lógicas: **26**.
- Dispositivos actualmente conectados: **7**.
- Conectores SuperSpeed aparentemente libres, físicamente accesibles y confirmados por propiedades del puerto: **5**. Su ubicación física exacta aún no se identificó.
- Aviso de limitación real: **SAMSUNG Mobile USB Composite Device** negocia **480 Mb/s (USB 2.0)** y el dispositivo indica compatibilidad con SuperSpeed. Windows detecta conectores alternativos compatibles.
- USB 2.0 High-Speed: máximo nominal de enlace **480 Mb/s**.
- USB 3.0 SuperSpeed: máximo nominal de enlace **5.000 Mb/s**.
- Ganancia teórica de señalización de usar SuperSpeed frente a 480 Mb/s: **4.520 Mb/s = 4,52 Gb/s**; el rendimiento real depende de protocolo, cable, controlador, flash y almacenamiento.

No se conectaron, retiraron ni expulsaron dispositivos durante el diagnóstico.

## Pruebas ejecutadas

El candidato compiló en Windows x64 Release self-contained. Las cuatro comprobaciones pasaron con código de salida 0:
- Escáner USB real
- Política USB sintética y consejo del Samsung real
- Selftest general de SolarisPKN-Paste
- Autoperfilado de hardware

La vista previa real en el escritorio del usuario estuvo activa **30,245 s** y cerró con **exit 0**, sin instalarse ni quedarse residente. No se capturó pantalla, por lo que esa prueba no equivale a inspección visual pixel a pixel.

Evidencias:
D:\researchs\SolarisPKN-Paste\reports\usb-inspector-20261107-final.json
D:\researchs\SolarisPKN-Paste\reports\usb-advisor-policy-20261107-final.json
D:\researchs\SolarisPKN-Paste\reports\usb-solaris-ui-preview-user-20261107.json

Ejecutable candidato:
D:\researchs\SolarisPKN-Paste\builds\usb-solaris-advisor-final-candidate-20261107\SolarisPKN-Paste.exe

## Limitaciones y QA pendiente

Todavía hay que verificar la conexión y extracción real de pendrives USB, dos dispositivos idénticos VID/PID, veto del sistema con archivos abiertos, controladores UASP y hubs USB4/Thunderbolt. El botón de extracción solo admite la ruta USBSTOR identificada de forma única, no UASP ni dispositivos MTP. El identificador de puerto es **lógico**, no una etiqueta física de la carcasa. Un teclado USB 2.0 no será más rápido en USB 3.0: la recomendación solo busca reservar conectores rápidos.

Esta funcionalidad requiere que el residente SolarisPKN-Paste esté activo en la bandeja del usuario. La instalación stable existente no fue alterada.

## Código fuente agregado/modificado

- UsbPortInspector.cs
- UsbAdvisorPolicy.cs
- UsbAdvisorToast.cs
- UsbSafeEject.cs
- TrayResident.cs
- Program.cs

Referencias primarias:
https://learn.microsoft.com/en-us/windows-hardware/drivers/debugger/usbview
https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/usbioctl/ni-usbioctl-ioctl_usb_get_node_connection_information_ex_v2
https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/usbioctl/ni-usbioctl-ioctl_usb_get_port_connector_properties
https://learn.microsoft.com/en-us/windows/win32/api/cfgmgr32/nf-cfgmgr32-cm_request_device_ejectw
