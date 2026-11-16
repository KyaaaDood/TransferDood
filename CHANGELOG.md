# Cambios / Changelog

## 0.1.0 — primera versión pública

Primera versión de **TransferDood**: envío de archivos entre Windows y Android por la red local.

**Envío y recepción**
- Detección automática de equipos en la red y conexión por IP.
- Archivos y carpetas completos, con progreso, velocidad, pausa/reanudar y cancelar.
- Texto y portapapeles.
- Historial de transferencias y lista de equipos bloqueados.

**Seguridad**
- Toda la conexión va cifrada con AES‑256‑GCM (ECDH P‑256 nuevo en cada conexión).
- Favoritos emparejados con código de 6 dígitos: reciben sin preguntar y el canal queda autenticado.
- Claves guardadas con DPAPI (Windows) y Keystore (Android).
- Límites contra abuso (conexiones, avisos pendientes, tamaños) y limpieza de nombres de archivo.
- Regla de firewall limitada a la subred local.

**Windows**
- Icono en la bandeja con menú: No molestar, silenciar en pantalla completa, inicio con Windows y menú del Explorador.
- Menú contextual «Enviar con TransferDood».
- Vista previa de imágenes, audio y texto; copiar el archivo recibido; «Abrir con…».

**Android**
- Servicio ligero en segundo plano, avisos flotantes para aceptar, abrir la carpeta de recibidos, instalar APK recibidos.
- Vista previa de imágenes, PDF, audio, video y texto; botón Compartir.
- Aparece también en el launcher de Android TV / Google TV (la interfaz es táctil; con control remoto puede ser incómoda).

**General**
- Español e inglés (automático o a elegir), tema claro/oscuro.

**Conocido / pendiente**
- Solo red local; no hay envío por internet.
- Interfaz de Android sin navegación pensada para control remoto de tele.
- El `.exe` de Windows no está firmado: SmartScreen puede avisar la primera vez.
