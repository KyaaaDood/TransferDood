# TransferDood para Android

App nativa en Kotlin, **sin ninguna librería externa** (ni Flutter, ni Compose, ni AndroidX): la interfaz está
dibujada a mano, así que el APK queda diminuto y gasta muy poca RAM. Habla el mismo protocolo que la versión de
Windows (ver [PROTOCOL.md](PROTOCOL.md)).

## Qué hace

- Detecta automáticamente los equipos con TransferDood en la misma red (broadcast UDP).
- **Enviar archivos** (varios a la vez) y **Enviar carpeta** completa, o desde el menú **Compartir** de cualquier app
  (galería, archivos, navegador…). Si compartes texto o un enlace, se manda como archivo `.txt`.
- Tocas un dispositivo y se envía; el otro lado recibe un aviso para aceptar o rechazar.
- **Favoritos**: toca la ⭐ de un dispositivo y sus envíos llegan sin preguntar. Se administran en Ajustes.
- Recibir: aviso moderno con **Aceptar / Rechazar**, también desde la notificación si la app no está abierta.
- Tema claro / oscuro / automático, nombre del dispositivo editable (vacío = nombre del teléfono).
- Carpeta de recibidos configurable. Por defecto: `Descargas/TransferDood`.
- Progreso, velocidad y botón de cancelar en cada transferencia.

## Compilar

Lo más fácil: abre esta carpeta con **Android Studio** (instala el SDK y el JDK solos) y usa
*Build > Build APK(s)*. Si Android Studio ofrece actualizar el plugin de Gradle, puedes aceptar.

Desde la terminal (necesitas JDK 17 y el SDK de Android):

```bat
build.bat
```

Genera `TransferDood.apk` en esta carpeta. Se firma con la llave de depuración para que se pueda instalar directo
(activa "instalar apps desconocidas" para tu explorador de archivos). Requiere Android 7.0 o superior.

## Cómo funciona la recepción

Android no deja escuchar conexiones con la app cerrada sin un servicio en primer plano, así que:

- **Con la app abierta** el teléfono es visible y puede recibir. Al cerrarla, el servicio se apaga solo.
- **Ajustes > Mantener disponible en segundo plano** deja el servicio encendido con una notificación fija
  (con botón *Detener*). Gasta un poco más de batería, pero otros equipos pueden enviarte con la app cerrada.
- Tras reiniciar el teléfono hay que abrir la app una vez (Android no permite arrancar este tipo de servicio solo).

En Android 13+ acepta el permiso de notificaciones la primera vez: sin él no verás los avisos de envío entrante
cuando la app esté en segundo plano (y se rechazan solos a los 60 segundos).

## Notas

- Si eliges otra carpeta de recibidos, Android solo permite carpetas que no sean la raíz ni `Download` completa.
- Al recibir una carpeta completa que ya existe, los archivos se **mezclan** dentro de ella (los repetidos se
  renombran como `nombre (1).ext`).
- Si la red tiene aislamiento de clientes (algunas redes de oficina u hospital), no se verán entre sí; funciona
  igual conectando ambos al hotspot del teléfono. Un futuro "conectar por IP / QR" lo resolvería.
- Toda la conexión va cifrada (AES-256-GCM); usar en redes de confianza. Las claves de favoritos se guardan con el Keystore de Android.

## Estructura

| Archivo | Para qué sirve |
|---------|----------------|
| `Protocol.kt` | Constantes y tipos del protocolo |
| `Discovery.kt` | Descubrimiento UDP |
| `Transfers.kt` | Cliente y servidor TCP, estado de cada transferencia |
| `Storage.kt` | Dónde y cómo se guardan los archivos recibidos (MediaStore / carpeta elegida / Android 7-9) |
| `Files.kt` | Nombres, tamaños y recorrido de carpetas a enviar |
| `Core.kt`, `CoreService.kt`, `Notifier.kt` | Estado global, servicio en primer plano y notificaciones |
| `MainActivity.kt`, `SettingsActivity.kt`, `Ui.kt` | Interfaz y tema |


## Nota de seguridad sobre favoritos
Los favoritos ahora se emparejan con un código y los envíos van firmados, así que no basta con copiar el ID de un equipo. Ahora toda la conexión también va cifrada (AES-256-GCM).


## Novedades

- **Emparejar con código (favoritos seguros):** toca la ⭐ de un dispositivo; aparece el mismo código de 6 dígitos en ambos y confirmas con *Coincide*. Desde entonces sus envíos llegan sin preguntar y van firmados (nadie puede hacerse pasar por él). Los favoritos de la versión anterior no tenían clave: hay que emparejarlos otra vez.
- **Conectar por IP** (botón *Conectar por IP*): para redes que bloquean la búsqueda. Las IP se guardan y se consultan siempre.
- **Enviar texto o portapapeles** (botón *Enviar texto*; también desde *Compartir* en otras apps): el texto recibido se copia solo al portapapeles.
- **Historial:** las últimas 60 transferencias se conservan al cerrar (`%APPDATA%\TransferDood\history.json`). *Limpiar* lo borra.
- **Bloquear:** mantén pulsada una tarjeta > *Bloquear este equipo*. Se gestiona en Ajustes > Favoritos y bloqueados.
- **Pausar / Reanudar** en las transferencias de archivos (la pausa se reanuda sola a los 90 s).


## Idioma, carpeta e instalación de APK

- **Idioma:** Ajustes > Idioma: *Automático* (sigue el del dispositivo), *Español* o *English*. En Windows el cambio se aplica al volver a abrir la app; en Android, al instante.
- **Abrir carpeta de recibidos:** botón en la pantalla principal (funciona con la carpeta predeterminada o con la que elijas).
- **APK:** al recibir un `.apk` el botón de la transferencia dice *Instalar*. La primera vez Android pide permitir "instalar apps desconocidas" para TransferDood; después abre el instalador de paquetes.


## Novedades

- Nombre nuevo: **TransferDood** (mismo ícono). En Windows se migran solos los ajustes, el menú contextual, el inicio automático y la regla de firewall del nombre anterior. En Android el paquete no cambió, así que se actualiza encima de la versión anterior.
- Descubrimiento adaptativo: anuncio cada 4 s con la app a la vista y cada 30 s en segundo plano.
- Vista previa dentro de la app: imágenes, audio y texto (Windows); imágenes, PDF, audio, video y texto (Android). En Windows los PDF y videos se abren con la app predeterminada para no engordar el programa.
- Compartir: en Android botón «Compartir» (menú de apps). En Windows «Copiar» (copia el archivo al portapapeles para pegarlo en cualquier lado), «Abrir con…» y clic derecho sobre la transferencia.
