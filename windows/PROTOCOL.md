# Protocolo TransferDood v1

Pensado para que la app de Android (Kotlin) pueda implementarse con sockets simples,
sin librerías pesadas. Todo usa el **puerto 47615**, en UDP (descubrimiento) y TCP (transferencia).

## 1. Descubrimiento (UDP broadcast)

Cada dispositivo escucha en `0.0.0.0:47615/UDP` y envía paquetes JSON (UTF-8) por **broadcast**
(`255.255.255.255` y el broadcast dirigido de cada subred, por ejemplo `192.168.1.255`).

```json
{"v":1,"t":"hello","id":"<id único del dispositivo>","name":"Mi PC","os":"windows","port":47615,"ttl":15}
```

| Campo | Significado |
|-------|-------------|
| `v`   | Versión del protocolo (1) |
| `t`   | `hello` (anuncio), `query` (¿quién está ahí?) o `bye` (me voy) |
| `id`  | Identificador estable del dispositivo (se ignoran los paquetes con el propio id) |
| `name`| Nombre visible (máx. 48 caracteres) |
| `os`  | `windows` o `android` (solo para elegir el icono) |
| `port`| Puerto TCP de recepción |

Reglas:

- Enviar `hello` cada ~4 s. Enviar `query` al abrir la app o al pulsar "Buscar".
- Al recibir `query`, responder `hello` **por unicast** a la IP de origen.
- Un dispositivo se considera desaparecido si no se oye nada de él en 15 s, o al recibir `bye`.
- La IP del dispositivo es la IP de origen del paquete UDP.
- En Android hay que tomar un `WifiManager.MulticastLock` mientras se escucha, y escuchar solo con la app abierta.

## 2. Transferencia (TCP)

El emisor abre una conexión TCP a `ip:port` del receptor.

```
Emisor → Receptor : "SDD1" (4 bytes ASCII) + largo de la cabecera (int32 big-endian) + cabecera JSON
Receptor → Emisor : 1 byte  (1 = aceptado, 0 = rechazado)
Emisor → Receptor : bytes de los archivos, uno tras otro, en el orden de la cabecera, sin separadores
Receptor → Emisor : 1 byte  (1 = todo recibido y guardado)
```

Cabecera:

```json
{
  "from": "Mi celular",
  "id": "<id del emisor>",
  "total": 123456,
  "files": [
    {"name": "foto.jpg", "size": 100000},
    {"name": "MiCarpeta/sub/nota.txt", "size": 23456}
  ]
}
```

- `name` puede incluir subcarpetas con `/` (así se envían carpetas completas).
- El receptor **debe sanear** los nombres: quitar `..`, caracteres inválidos y nombres reservados de Windows,
  y nunca escribir fuera de su carpeta de recibidos. Si el nombre ya existe, agrega ` (1)`, ` (2)`…
- Tras la cabecera el emisor espera hasta 75 s la respuesta (el usuario puede tardar en aceptar).
- Si pasan 30 s sin recibir datos, el receptor cancela.
- Los archivos se escriben como `nombre.sdpart` y se renombran al terminar; si falla, se borra el parcial.

## 3. Seguridad (versión actual)

- El receptor pide confirmación al usuario (a menos que active "Aceptar sin preguntar").
- En Windows la regla de firewall limita el acceso a la subred local (`remoteip=localsubnet`).
- **No hay cifrado ni autenticación** en v1: úsalo en redes de confianza. Pendiente para v2: TLS con certificado
  autofirmado y/o PIN de emparejamiento.


## Extensiones (v2, compatibles hacia atrás: campos opcionales)

Encabezado TCP, campos nuevos:

| Campo | Uso |
|-------|-----|
| `kind` | `"files"` (por defecto), `"text"` o `"pair"` |
| `text` | (kind=text) texto a enviar, máx. 60 000 caracteres. `files` va vacío. El flujo es el mismo: el receptor responde 1/0, y tras entregar el texto manda el byte final 1. |
| `ts`, `n`, `mac` | Firma de un equipo emparejado: `mac = HMAC-SHA256(secreto, id + "|" + ts + "|" + n)` en hex minúsculas. `ts` = segundos Unix (tolerancia ±300 s), `n` = nonce aleatorio que no se puede repetir. Si es válida, el envío se acepta sin preguntar. |
| `pub` | (kind=pair) clave pública ECDH P-256 en formato SubjectPublicKeyInfo (base64). |

### Emparejar (favoritos con código)
1. Iniciador → receptor: encabezado `kind=pair` con su `pub`.
2. Receptor → iniciador: byte `3`, int32 BE con la longitud, su clave pública (SPKI).
3. Ambos calculan el código: `SHA256("SDD-SAS" + pubIniciador + pubReceptor)`, primeros 4 bytes como uint32 BE, módulo 1 000 000, a 6 dígitos (`123 456`). Cada usuario confirma que el código es igual en las dos pantallas.
4. Iniciador → receptor: byte 1 (coincide) o 0. Receptor → iniciador: byte 1 (ambos confirmaron) o 0.
5. Secreto compartido = `SHA256(X del ECDH)`. Se guarda en ambos y el equipo pasa a favoritos.

El código evita que alguien intercepte el emparejamiento (ataque de intermediario) y el secreto nunca viaja por la red.

### Bloqueo
Un equipo bloqueado recibe un byte `0` inmediatamente tras el encabezado y no aparece en la lista.

### Pausa
Es local (la app deja de leer/escribir). Como el otro lado espera datos, el tiempo sin datos permitido sube de 30 s a 120 s y una pausa se reanuda sola a los 90 s.

### Conexión por IP
Descubrimiento normal, pero el paquete UDP `query` se envía directo (unicast) a la IP indicada, cada 4 s. El otro equipo responde `hello` a quien le preguntó.


## Campo `ttl` (ahorro de batería)

`ttl` = segundos que otros equipos deben recordar este dispositivo sin volver a oír un `hello` (10–180, por defecto 15). Con la ventana abierta se anuncia cada 4 s (`ttl` 15); en segundo plano cada 30 s (`ttl` 75). Un equipo viejo sin `ttl` se trata como 15 s.


## Cifrado de la conexión (SDE1)

Antes del protocolo `SDD1` descrito arriba, cada conexión TCP hace un handshake y desde entonces **todo** va cifrado
con AES‑256‑GCM; el protocolo `SDD1` corre idéntico por dentro del canal.

```
Cliente → servidor:  "SDE1" | flags(1) | idLen(1) | id (UTF-8, ≤64) | pubLen(1)=91 | pub (SPKI P-256, 91 bytes)
Servidor → cliente:  flags(1) | pubLen(1)=91 | pub
```

- `flags` bit0 del cliente: «tengo un secreto de favorito para ti». Bit0 del servidor: «lo tengo yo también y lo usé».
- `shared` = ECDH crudo (32 bytes, coordenada x). `salt` = secreto de favorito (32 bytes) o 32 ceros si no se usó.
- `prk = HMAC-SHA256(key = salt, "tdood-e2e" | shared | pubCliente | pubServidor)`
- `kC2S = HMAC-SHA256(prk, "c2s" 0x01)`, `kS2C = HMAC-SHA256(prk, "s2c" 0x01)` (cada sentido usa su clave).
- Tramos: `[len(4, big-endian, ≤65536)] [cifrado(len)] [etiqueta GCM(16)]`. Nonce de 12 bytes = 4 ceros + contador de
  64 bits big-endian (empieza en 0 en cada sentido). AAD = los 4 bytes de `len`.
- Si la etiqueta no coincide (datos alterados o claves distintas) la conexión se cierra.
- Tiempo máximo del handshake: 10 s. El servidor limita a 16 conexiones simultáneas y 3 avisos pendientes por IP.
