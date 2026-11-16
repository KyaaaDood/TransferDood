# Seguridad / Security

## Modelo de seguridad (resumen)

TransferDood está pensado **solo para la red local** (misma Wi‑Fi / misma LAN). No usa servidores ni cuentas y no envía
telemetría.

- **Cifrado:** toda conexión va cifrada con AES‑256‑GCM. Las claves salen de un intercambio ECDH P‑256 nuevo en cada
  conexión (secreto perfecto hacia adelante). Se cifra todo: nombres de archivo, textos y contenido.
- **Favoritos:** al emparejar dos equipos (código de 6 dígitos que debe verse igual en ambas pantallas) se genera un
  secreto compartido. Ese secreto se mezcla en la clave de cada conexión, así el canal también queda **autenticado**
  y un intermediario en la red no puede suplantar a un favorito. Los envíos de favoritos se firman (HMAC‑SHA256 con
  marca de tiempo y nonce contra repeticiones) y solo ellos pueden recibirse sin preguntar.
- **Equipos que no son favoritos:** el canal está cifrado contra quien solo escucha, pero sin emparejar no hay forma de
  comprobar la identidad del otro equipo: alguien activamente en medio de la conexión (ataque de intermediario)
  podría hacerse pasar por él. Por eso **siempre se pregunta** antes de aceptar y se muestra el nombre y la IP.
- **Claves guardadas:** en Windows con DPAPI (ligadas a tu usuario); en Android con el Keystore del sistema.
- **Límites contra abuso:** máximo de conexiones simultáneas, máximo de avisos pendientes por equipo, tope de tamaño del
  encabezado, comprobación de espacio libre y limpieza de nombres/rutas recibidos (no se puede escribir fuera de la
  carpeta de recibidos).
- **Firewall (Windows):** la regla solo permite la subred local.
- **No se ejecuta nada automáticamente.** Instalar un APK siempre pasa por el instalador de Android.
- No se añade la marca de internet (Mark of the Web) a los archivos recibidos en Windows: SmartScreen no avisará al
  abrir un `.exe` recibido. Abre solo lo que esperabas recibir.
- No se ha hecho una auditoría externa. Es un proyecto personal sin garantía (ver LICENSE).

## Reportar una vulnerabilidad

Usa **GitHub → Security → "Report a vulnerability"** (reporte privado) en este repositorio. Por favor no abras un issue
público con detalles de una vulnerabilidad sin resolver.

---

TransferDood is meant for the **local network only**. All connections are encrypted with AES‑256‑GCM using a fresh
ECDH P‑256 exchange per connection; paired ("favorite") devices additionally mix a shared secret into the key, which
authenticates the channel. Unpaired devices get encryption against passive eavesdroppers only, so the receiver always
asks before accepting. No servers, accounts or telemetry. Please report vulnerabilities privately through GitHub's
"Report a vulnerability".
