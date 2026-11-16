<div align="center">

<img src="docs/icon.png" width="112" alt="TransferDood">

# TransferDood

**Send files, folders, text and the clipboard between Windows and Android over your local network.**
Fast, lightweight and encrypted. No accounts, no servers, no telemetry.

[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
![Windows 10/11](https://img.shields.io/badge/Windows-10%20%2F%2011-0078D4?logo=windows&logoColor=white)
![Android 7+](https://img.shields.io/badge/Android-7.0%2B-3DDC84?logo=android&logoColor=white)
[![Version 0.1.0](https://img.shields.io/badge/version-0.1.0-informational)](CHANGELOG.md)
![AES-256-GCM encryption](https://img.shields.io/badge/encryption-AES--256--GCM-success)
[![Patreon](https://img.shields.io/badge/Patreon-KyaaaDood-F96854?logo=patreon&logoColor=white)](https://www.patreon.com/cw/KyaaaDood)

[English](README.md) · [Español](README.es.md)

[Download](../../releases) · [Build](#-build) · [Security](SECURITY.md) · [Changelog](CHANGELOG.md)

</div>

---

## ✨ Features

| | |
|---|---|
| 📡 **Automatic discovery** | Finds the devices on your network and, if discovery doesn't work, connects **by IP**. |
| 📁 **Files and folders** | Progress, speed, pause/resume and cancel. |
| 📝 **Text and clipboard** | Send it from one device to another and paste it right away. |
| 🔒 **AES-256-GCM encryption** | Everything is encrypted in transit, using the CPU's hardware acceleration (it doesn't slow transfers down). |
| ⭐ **Favorites** | Pair devices with a 6-digit code; favorites receive without asking and the channel is authenticated. |
| 🌙 **Do not disturb** | Pause receiving for 30 min, 1–2 h or until you turn it off, or mute it automatically in full screen (games). |
| 👀 **Preview and share** | View images, audio and text without leaving the app (PDF and video too on Android); share or copy the received file. |
| 🪶 **Lightweight** | Low RAM and battery use: discovery slows down when the app isn't in view. |
| 🌐 **English / Español** | Automatic (follows the device language) or your choice. |
| 🎨 **Light / dark theme** | Follows the system or your choice. |

**Windows:** tray icon, "Send with TransferDood" Explorer context menu, start with Windows and an automatic firewall
rule (local subnet only).
**Android:** lightweight background service, heads-up notifications to accept, open the received-files folder and install received APKs.

> **Version 0.1.0** — first public release. It works and is used daily, but it's still young: if something breaks,
> please open an issue. See [CHANGELOG.md](CHANGELOG.md) for what's included.

## 📦 Requirements

| Platform | Version |
|---|---|
| Windows | 10 / 11, 64-bit. The *light* build needs the **.NET 8 Desktop Runtime**; the *standalone* build needs nothing. |
| Android | 7.0 (API 24) or newer, with a touch screen. Not designed for Android TV / Google TV (it installs, but the UI is touch-first). |

## ⬇️ Download

From [**Releases**](../../releases):

- `TransferDood-win-x64.exe` — light (requires the .NET 8 Desktop Runtime)
- `TransferDood-win-x64-standalone.exe` — standalone, larger
- `TransferDood-android.apk`
- `SHA256SUMS.txt` — to verify the downloads

> The `.exe` is not digitally signed, so Windows SmartScreen may show a warning the first time
> ("More info" → "Run anyway"). The source code is right here for you to review.

## 🚀 Quick start

1. Install TransferDood on both devices and connect them to the **same network**.
2. Open the app: you'll see the other device in the list. Drag files onto it (Windows) or tap the device (Android).
3. The other device accepts. To skip the prompt every time, tap the ⭐ and pair with the 6-digit code.

**Can't see the other device?** Use **By IP** and type its address. Make sure both are on the same network (some guest
or office networks isolate devices from each other).

## 🔨 Build

- **Windows:** install the .NET 8 SDK and run `windows\build.bat` (light) or `windows\build.bat full` (standalone).
- **Android:** open `android/` with Android Studio, or run `android\build.bat` / `./gradlew assembleRelease`.
  Gradle 8.9 needs **JDK 17–21** (it does not work with Java 25 or Java 8).
- **GitHub Actions:** pushing a `v*` tag builds both and creates a release. To sign the APK with your own key, set the
  secrets `TD_KEYSTORE_BASE64`, `TD_KEYSTORE_PASSWORD`, `TD_KEY_ALIAS` and `TD_KEY_PASSWORD`.

```text
windows/   C# .NET 8 (WinForms), no external dependencies
android/   Native Kotlin, no external libraries
```

## 🔐 Security

TransferDood is built **for trusted local networks only** (your home, your office). Everything is encrypted, and you are
always asked before accepting anything from a device that isn't a favorite. Full model, limits and how to report a
vulnerability: [SECURITY.md](SECURITY.md). The protocol is documented in [`windows/PROTOCOL.md`](windows/PROTOCOL.md).

## 💬 FAQ

<details>
<summary>Does it work outside my local network, over the internet?</summary>

No. TransferDood only works inside the same local network; it uses no intermediary servers.
</details>

<details>
<summary>Why does Windows ask for administrator permission at first launch?</summary>

Once, to create the firewall rule that lets connections from your local subnet reach port 47615.
</details>

<details>
<summary>I don't get heads-up notifications on Android</summary>

Open the app's notification settings and enable "Pop on screen" / "Floating" for the *Incoming files* channel.
Some brands (Xiaomi, Huawei, Samsung) turn it off by default for sideloaded apps.
</details>

<details>
<summary>I installed two copies on my phone</summary>

Don't run two copies: they use the same port and get in each other's way. Uninstall both and install just one.
</details>

## ❤️ Credits

Made by **KyaaaDood** with Claude AI.

If TransferDood is useful to you, you can support its development on [**Patreon**](https://www.patreon.com/cw/KyaaaDood). Thank you!

## 📄 License

[MIT](LICENSE) © 2026 KyaaaDood
