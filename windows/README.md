# TransferDood

Envía archivos y carpetas entre equipos de la misma red, rápido y con muy poca RAM.
Esta es la parte de **Windows** (C# / .NET 8 / WinForms). La app de Android se hará aparte usando el mismo
protocolo (ver [PROTOCOL.md](PROTOCOL.md)).

## Qué hace

- Detecta automáticamente otros equipos con TransferDood en la red (broadcast UDP).
- Interfaz clara/oscura (automática según Windows), con tarjetas de dispositivos y lista de transferencias.
- Envía **arrastrando archivos o carpetas** sobre un dispositivo, con los botones *Enviar archivos* / *Enviar carpeta*,
  o con **clic derecho → Enviar con TransferDood** (sin abrir la ventana).
- Nombre del equipo editable (vacío = nombre del dispositivo), carpeta de recibidos configurable.
- Vive en la bandeja del sistema, puede **iniciar con Windows** y pide confirmación antes de recibir.
- Regla de **firewall automática**: la primera vez Windows muestra un aviso de administrador (UAC) y listo.

## Compilar

Necesitas el [SDK de .NET 8](https://dotnet.microsoft.com/download/dotnet/8.0). Después, en esta carpeta:

```bat
build.bat          :: versión ligera -> dist\TransferDood.exe (muy pequeña, requiere el .NET 8 Desktop Runtime)
build.bat full     :: versión autónoma -> dist-full\TransferDood.exe (no requiere instalar nada, pesa más)
```

Para probar sin publicar: `dotnet run`.

## Uso

1. Abre `TransferDood.exe` en los dos equipos (misma red).
2. Aparecen como tarjetas. Arrastra archivos a una tarjeta, o elige archivos y toca la tarjeta.
3. El otro equipo recibe un aviso en la esquina de la pantalla: **Aceptar** o **Rechazar**.
4. Los archivos llegan a `Descargas\TransferDood` (se puede cambiar en *Ajustes*).

Al cerrar la ventana el programa sigue en la bandeja (usa unos pocos MB). *Salir* está en el menú de la bandeja.

## Notas

- **Windows 11:** la opción de clic derecho aparece en *Mostrar más opciones* (menú clásico). Para que salga en el
  menú nuevo haría falta empaquetar la app (MSIX); se puede hacer más adelante.
- **Redes con aislamiento de clientes** (algunas redes de oficina u hospital) bloquean el broadcast: ahí los
  equipos no se verán entre sí. Un futuro "conectar por IP / QR" lo resolvería.
- **Seguridad:** todo va cifrado con AES-256-GCM (ver PROTOCOL.md y SECURITY.md); pensada para redes de confianza. El firewall limita el acceso a la subred local.
- **Desinstalar:** `TransferDood.exe --cleanup` quita el inicio con Windows, el menú contextual y la regla de firewall
  (pide permiso de administrador). Los ajustes están en `%APPDATA%\TransferDood\settings.json`.

## Estructura

| Archivo | Para qué sirve |
|---------|----------------|
| `Program.cs` | Instancia única y recepción de archivos desde el menú contextual |
| `TrayApp.cs` | Bandeja, ventanas, cola de envío, avisos |
| `Discovery.cs` | Descubrimiento UDP |
| `Transfers.cs` | Cliente y servidor TCP, lista de transferencias |
| `MainForm.cs`, `Controls.cs`, `SettingsForm.cs`, `PromptForm.cs`, `Theme.cs` | Interfaz |
| `SystemIntegration.cs` | Inicio con Windows, menú contextual, firewall |
| `Settings.cs` | Ajustes (JSON) y textos de marca |


## Favoritos y avisos
- Estrella en cada tarjeta: los favoritos reciben sin preguntar. Se administran en Ajustes > Dispositivos favoritos.
- Aviso de entrada y de «recibido» son ventanitas propias (sin notificaciones de Windows, sin robar el foco).
- Sin barras de scroll nativas: rueda del ratón sobre listas.
- Seguridad: toda la conexión va cifrada (AES-256-GCM); los favoritos se emparejan con código, firman sus envíos y autentican el canal. Las claves se guardan con DPAPI.


## Novedades

- **Emparejar con código (favoritos seguros):** toca la ⭐ de un equipo; aparece el mismo código de 6 dígitos en ambos y confirmas con *Coincide*. Desde entonces sus envíos llegan sin preguntar y van firmados (nadie puede hacerse pasar por él). Los favoritos de la versión anterior no tenían clave: hay que emparejarlos otra vez.
- **Conectar por IP** (botón *Por IP*): para redes que bloquean la búsqueda. Las IP se guardan y se consultan siempre.
- **Enviar texto o portapapeles** (botón *Enviar texto* o menú de la bandeja *Enviar portapapeles…*): el texto recibido se copia solo al portapapeles.
- **Historial:** las últimas 60 transferencias se conservan al cerrar (`%APPDATA%\TransferDood\history.json`). *Limpiar* lo borra.
- **Bloquear:** clic derecho en una tarjeta > *Bloquear este equipo*. Se gestiona en Ajustes > Favoritos y bloqueados.
- **Pausar / Reanudar** en las transferencias de archivos (la pausa se reanuda sola a los 90 s).


## Idioma, carpeta e instalación de APK

- **Idioma:** Ajustes > Idioma: *Automático* (sigue el del dispositivo), *Español* o *English*. En Windows el cambio se aplica al volver a abrir la app; en Android, al instante.


## Novedades

- Nombre nuevo: **TransferDood** (mismo ícono). En Windows se migran solos los ajustes, el menú contextual, el inicio automático y la regla de firewall del nombre anterior. En Android el paquete no cambió, así que se actualiza encima de la versión anterior.
- Descubrimiento adaptativo: anuncio cada 4 s con la app a la vista y cada 30 s en segundo plano.
- Vista previa dentro de la app: imágenes, audio y texto (Windows); imágenes, PDF, audio, video y texto (Android). En Windows los PDF y videos se abren con la app predeterminada para no engordar el programa.
- Compartir: en Android botón «Compartir» (menú de apps). En Windows «Copiar» (copia el archivo al portapapeles para pegarlo en cualquier lado), «Abrir con…» y clic derecho sobre la transferencia.
