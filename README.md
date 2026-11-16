<div align="center">

<img src="docs/icon.png" width="112" alt="TransferDood">

# TransferDood

**Envía archivos, carpetas, textos y el portapapeles entre Windows y Android por tu red local.**
Rápido, ligero y cifrado. Sin cuentas, sin servidores, sin telemetría.

[![Licencia: MIT](https://img.shields.io/badge/licencia-MIT-blue.svg)](LICENSE)
![Windows 10/11](https://img.shields.io/badge/Windows-10%20%2F%2011-0078D4?logo=windows&logoColor=white)
![Android 7+](https://img.shields.io/badge/Android-7.0%2B-3DDC84?logo=android&logoColor=white)
[![Versión 0.1.0](https://img.shields.io/badge/versi%C3%B3n-0.1.0-informational)](CHANGELOG.md)
![Cifrado AES-256-GCM](https://img.shields.io/badge/cifrado-AES--256--GCM-success)
[![Patreon](https://img.shields.io/badge/Patreon-KyaaaDood-F96854?logo=patreon&logoColor=white)](https://www.patreon.com/cw/KyaaaDood)

[Descargar](../../releases) · [Compilar](#-compilar) · [Seguridad](SECURITY.md) · [English](#-english)

</div>

---

## ✨ Características

| | |
|---|---|
| 📡 **Detección automática** | Encuentra los equipos de tu red y, si el descubrimiento no funciona, conecta **por IP**. |
| 📁 **Archivos y carpetas** | Con progreso, velocidad, pausa/reanudar y cancelar. |
| 📝 **Texto y portapapeles** | Mándalo de un equipo a otro y pégalo directo. |
| 🔒 **Cifrado AES‑256‑GCM** | Todo viaja cifrado, usando la aceleración por hardware del procesador (no frena la transferencia). |
| ⭐ **Favoritos** | Se emparejan con un código de 6 dígitos; reciben sin preguntar y el canal queda autenticado. |
| 🌙 **No molestar** | Pausa la recepción por 30 min, 1–2 h o hasta que lo desactives, o silénciala sola en pantalla completa (juegos). |
| 👀 **Vista previa y compartir** | Mira imágenes, audio y texto sin salir de la app (en Android también PDF y video); comparte o copia el archivo recibido. |
| 🪶 **Ligera** | Poca RAM y batería: el descubrimiento baja su ritmo cuando la app no está a la vista. |
| 🌐 **Español / English** | Automático según el idioma del dispositivo, o a elegir. |
| 🎨 **Tema claro / oscuro** | Sigue al sistema o lo eliges tú. |

**Windows:** icono en la bandeja, menú contextual «Enviar con TransferDood» en el Explorador, inicio con Windows y regla de
firewall automática (solo la subred local).
**Android:** servicio ligero en segundo plano, avisos flotantes para aceptar, abrir la carpeta de recibidos e instalar APK recibidos.

> **Versión 0.1.0** — primera versión pública. Funciona y se usa a diario, pero todavía es joven: si algo falla,
> abre un *issue*. Mira [CHANGELOG.md](CHANGELOG.md) para ver qué incluye.

## 📦 Requisitos

| Plataforma | Versión |
|---|---|
| Windows | 10 / 11 de 64 bits. La versión *ligera* necesita el **.NET 8 Desktop Runtime**; la *standalone* no necesita nada. |
| Android | 7.0 (API 24) o más nuevo, con pantalla táctil. No está pensada para Android TV / Google TV. |

## ⬇️ Descargar

En [**Releases**](../../releases):

- `TransferDood-win-x64.exe` — ligera (requiere .NET 8 Desktop Runtime)
- `TransferDood-win-x64-standalone.exe` — autónoma, pesa más
- `TransferDood-android.apk`
- `SHA256SUMS.txt` — para verificar las descargas

> El `.exe` no está firmado digitalmente, así que Windows SmartScreen puede mostrar un aviso la primera vez
> («Más información» → «Ejecutar de todas formas»). El código está aquí para que lo revises.

## 🚀 Uso rápido

1. Instala TransferDood en los dos equipos y conéctalos a la **misma red**.
2. Abre la app: verás al otro equipo en la lista. Arrastra archivos sobre él (Windows) o toca el equipo (Android).
3. El otro equipo acepta. Para no tener que aceptar cada vez, toca la ⭐ y empareja con el código de 6 dígitos.

**¿No aparece el otro equipo?** Usa **Por IP** y escribe su dirección. Revisa que ambos estén en la misma red (algunas
redes de invitados o de oficina aíslan los equipos entre sí).

## 🔨 Compilar

- **Windows:** instala el SDK de .NET 8 y ejecuta `windows\build.bat` (ligera) o `windows\build.bat full` (standalone).
- **Android:** abre `android/` con Android Studio (JDK 17+) o ejecuta `android\build.bat` / `./gradlew assembleRelease`.
- **GitHub Actions:** al subir una etiqueta `v*` se compilan ambos y se crea un *release*. Para firmar el APK con tu llave
  define los secretos `TD_KEYSTORE_BASE64`, `TD_KEYSTORE_PASSWORD`, `TD_KEY_ALIAS` y `TD_KEY_PASSWORD`.

```text
windows/   C# .NET 8 (WinForms), sin dependencias externas
android/   Kotlin nativo, sin librerías externas
```

## 🔐 Seguridad

Está hecha **solo para redes de confianza** (tu casa, tu oficina). Todo va cifrado y siempre se pregunta antes de aceptar
algo de un equipo que no es favorito. Modelo completo, límites y cómo reportar una vulnerabilidad: [SECURITY.md](SECURITY.md).
El protocolo está documentado en [`windows/PROTOCOL.md`](windows/PROTOCOL.md).

## 💬 Preguntas frecuentes

<details>
<summary>¿Funciona fuera de mi red local, por internet?</summary>

No. TransferDood solo trabaja dentro de la misma red local; no usa servidores intermedios.
</details>

<details>
<summary>¿Por qué Windows me pide permisos de administrador al inicio?</summary>

Una sola vez, para crear la regla del firewall que deja entrar conexiones desde tu subred local al puerto 47615.
</details>

<details>
<summary>No me llegan los avisos flotantes en Android</summary>

Abre los ajustes de notificaciones de la app y activa «Mostrar como aviso emergente / flotante» para el canal
*Archivos entrantes*. Algunas marcas (Xiaomi, Huawei, Samsung) lo desactivan por defecto en apps instaladas a mano.
</details>

## ❤️ Créditos

Hecho por **KyaaaDood** con Claude AI.

Si TransferDood te sirve, puedes apoyar el desarrollo en [**Patreon**](https://www.patreon.com/cw/KyaaaDood). ¡Gracias!

## 📄 Licencia

[MIT](LICENSE) © 2026 KyaaaDood

---

## 🇬🇧 English

**TransferDood** sends files, folders, text and clipboard between **Windows** and **Android** over your **local network**
— fast, light and encrypted (AES‑256‑GCM). No accounts, no servers, no telemetry.

- Automatic device discovery (or connect by IP), pause/resume, favorites paired with a 6‑digit code.
- Do‑not‑disturb mode, in‑app preview (images, audio, text; PDF and video on Android), share/copy received files.
- English and Spanish UI (automatic or manual), light/dark theme.
- Windows 10/11 x64 and Android 7.0+. Local network only. See [SECURITY.md](SECURITY.md) for the security model.

Download from [Releases](../../releases), or build it: `windows\build.bat` (needs the .NET 8 SDK) and `android/`
(Android Studio, JDK 17+). Made by **KyaaaDood** with Claude AI · [Patreon](https://www.patreon.com/cw/KyaaaDood) · [MIT](LICENSE).
