package com.kyaaadood.transferdood

import android.util.Base64
import org.json.JSONObject
import java.io.EOFException
import java.io.IOException
import java.net.InetSocketAddress
import java.net.Socket
import java.net.SocketTimeoutException
import java.nio.ByteBuffer
import java.security.KeyFactory
import java.security.KeyPair
import java.security.KeyPairGenerator
import java.security.MessageDigest
import java.security.PrivateKey
import java.security.SecureRandom
import java.security.spec.ECGenParameterSpec
import java.security.spec.X509EncodedKeySpec
import javax.crypto.KeyAgreement
import javax.crypto.Mac
import javax.crypto.spec.SecretKeySpec

/** Firma de envíos de favoritos y emparejamiento por clave pública (ECDH P-256). Mismo esquema que Windows. */
object Crypto {
    private val rnd = SecureRandom()
    private val seen = HashMap<String, Long>()

    private fun hex(b: ByteArray) = b.joinToString("") { "%02x".format(it) }

    fun newNonce(): String = ByteArray(12).also { rnd.nextBytes(it) }.let { hex(it) }

    fun mac(secret: ByteArray, id: String, ts: Long, nonce: String): String {
        val m = Mac.getInstance("HmacSHA256")
        m.init(SecretKeySpec(secret, "HmacSHA256"))
        return hex(m.doFinal("$id|$ts|$nonce".toByteArray(Charsets.UTF_8)))
    }

    /** Añade la firma al encabezado si ya hay emparejamiento con ese equipo. */
    fun sign(h: JSONObject, peerId: String) {
        val secret = Core.prefs.secret(peerId) ?: return
        val ts = System.currentTimeMillis() / 1000
        val n = newNonce()
        h.put("ts", ts).put("n", n).put("mac", mac(secret, Core.prefs.deviceId, ts, n))
    }

    /** True si el encabezado trae una firma válida, reciente y no repetida de un favorito. */
    @Synchronized
    fun verifyTrusted(h: JSONObject): Boolean {
        val id = h.optString("id", "").take(64)
        val n = h.optString("n", "")
        val m = h.optString("mac", "")
        if (id.isEmpty() || n.isEmpty() || n.length > 64 || m.isEmpty()) return false
        val secret = Core.prefs.secret(id) ?: return false
        val ts = h.optLong("ts", 0)
        val now = System.currentTimeMillis() / 1000
        if (Math.abs(now - ts) > 300) return false
        val expect = mac(secret, id, ts, n)
        if (!MessageDigest.isEqual(expect.toByteArray(), m.lowercase().toByteArray())) return false
        seen.entries.removeAll { it.value < now }
        val key = id + n
        if (seen.containsKey(key)) return false
        seen[key] = now + 600
        return true
    }

    fun newKeyPair(): KeyPair {
        val g = KeyPairGenerator.getInstance("EC")
        g.initialize(ECGenParameterSpec("secp256r1"))
        return g.generateKeyPair()
    }

    fun sas(initiatorPub: ByteArray, responderPub: ByteArray): String {
        val d = MessageDigest.getInstance("SHA-256")
        d.update("SDD-SAS".toByteArray(Charsets.US_ASCII))
        d.update(initiatorPub)
        d.update(responderPub)
        val h = d.digest()
        val v = ((h[0].toLong() and 0xFF) shl 24 or ((h[1].toLong() and 0xFF) shl 16) or
                ((h[2].toLong() and 0xFF) shl 8) or (h[3].toLong() and 0xFF)) % 1_000_000L
        val t = v.toString().padStart(6, '0')
        return t.substring(0, 3) + " " + t.substring(3)
    }

    /** Secreto ECDH crudo (32 bytes), para derivar las claves del canal cifrado. */
    fun ecdhRaw(mine: PrivateKey, peerPub: ByteArray): ByteArray {
        val pk = KeyFactory.getInstance("EC").generatePublic(X509EncodedKeySpec(peerPub))
        val ka = KeyAgreement.getInstance("ECDH")
        ka.init(mine)
        ka.doPhase(pk, true)
        return ka.generateSecret()
    }

    fun derive(mine: PrivateKey, peerPub: ByteArray): ByteArray {
        val pk = KeyFactory.getInstance("EC").generatePublic(X509EncodedKeySpec(peerPub))
        val ka = KeyAgreement.getInstance("ECDH")
        ka.init(mine)
        ka.doPhase(pk, true)
        return MessageDigest.getInstance("SHA-256").digest(ka.generateSecret())
    }
}

/** Lado que inicia el emparejamiento (toca la estrella de un equipo). Devuelve null si salió bien, o el motivo del error. */
object PairClient {
    fun start(peer: Peer) {
        Thread({
            val err = run(peer)
            Core.toast(if (err == null) Loc.f("Emparejado con {0}", peer.name) else Loc.f("No se pudo emparejar: {0}", err))
            Core.notifyChanged()
        }, "sd-pair").apply { isDaemon = true }.start()
    }

    private fun run(peer: Peer): String? {
        try {
            val s = Socket()
            try {
                s.connect(InetSocketAddress(peer.address, peer.port), 6000)
            } catch (e: IOException) {
                return Loc.t("no se pudo conectar con el equipo.")
            }
            s.use {
                s.tcpNoDelay = true
                val ch = SecureChannel.client(s, "")
                val out = ch.out
                val inp = ch.inp

                val kp = Crypto.newKeyPair()
                val myPub = kp.public.encoded
                val header = JSONObject()
                    .put("from", Core.prefs.effectiveName)
                    .put("id", Core.prefs.deviceId)
                    .put("kind", "pair")
                    .put("pub", Base64.encodeToString(myPub, Base64.NO_WRAP))
                    .toString().toByteArray(Charsets.UTF_8)
                out.write(Protocol.MAGIC)
                out.write(ByteBuffer.allocate(4).putInt(header.size).array())
                out.write(header)
                out.flush()

                s.soTimeout = 15_000
                val first = inp.read()
                if (first != 3) return Loc.t("el otro equipo no aceptó emparejar.")
                val lb = ByteArray(4)
                inp.readFully(lb, 4)
                val len = ByteBuffer.wrap(lb).int
                if (len <= 0 || len > 4096) return Loc.t("respuesta no válida.")
                val peerPub = ByteArray(len)
                inp.readFully(peerPub, len)

                val code = Crypto.sas(myPub, peerPub)
                val req = IncomingRequest(Core.newRequestId(), peer.id, peer.name, 0, 0, "", kind = "pair", code = code, initiator = true)
                val ok = Core.ask(req)
                out.write(if (ok) 1 else 0)
                out.flush()
                if (!ok) return Loc.t("emparejamiento cancelado.")

                s.soTimeout = 75_000
                val r = inp.read()
                if (r != 1) return Loc.t("el otro equipo no confirmó el código.")
                Core.prefs.pair(peer.id, peer.name, Crypto.derive(kp.private, peerPub))
            }
            return null
        } catch (e: SocketTimeoutException) {
            return Loc.t("el otro equipo no respondió.")
        } catch (e: EOFException) {
            return Loc.t("el otro equipo cerró la conexión.")
        } catch (e: Exception) {
            return e.message ?: "error desconocido."
        }
    }
}

/** Protege las claves de favoritos con el Keystore de Android (la clave maestra no sale del chip de seguridad). */
object KeyBox {
    private const val ALIAS = "transferdood_secret_key"
    private const val PREFIX = "ks:"

    private fun b64(b: ByteArray) = Base64.encodeToString(b, Base64.NO_WRAP)

    private fun key(): javax.crypto.SecretKey? = try {
        val ks = java.security.KeyStore.getInstance("AndroidKeyStore").apply { load(null) }
        (ks.getKey(ALIAS, null) as? javax.crypto.SecretKey) ?: run {
            val g = javax.crypto.KeyGenerator.getInstance(android.security.keystore.KeyProperties.KEY_ALGORITHM_AES, "AndroidKeyStore")
            g.init(
                android.security.keystore.KeyGenParameterSpec.Builder(
                    ALIAS,
                    android.security.keystore.KeyProperties.PURPOSE_ENCRYPT or android.security.keystore.KeyProperties.PURPOSE_DECRYPT
                )
                    .setBlockModes(android.security.keystore.KeyProperties.BLOCK_MODE_GCM)
                    .setEncryptionPaddings(android.security.keystore.KeyProperties.ENCRYPTION_PADDING_NONE)
                    .setKeySize(256)
                    .build()
            )
            g.generateKey()
        }
    } catch (_: Throwable) {
        null
    }

    fun isWrapped(s: String) = s.startsWith(PREFIX)

    /** Texto listo para guardar. Si el Keystore falla en este teléfono, se guarda como antes (base64). */
    fun wrap(data: ByteArray): String {
        val k = key() ?: return b64(data)
        return try {
            val c = javax.crypto.Cipher.getInstance("AES/GCM/NoPadding")
            c.init(javax.crypto.Cipher.ENCRYPT_MODE, k)
            PREFIX + b64(c.iv + c.doFinal(data))
        } catch (_: Throwable) {
            b64(data)
        }
    }

    fun unwrap(s: String): ByteArray? = try {
        if (!isWrapped(s)) {
            Base64.decode(s, Base64.NO_WRAP)
        } else {
            val all = Base64.decode(s.substring(PREFIX.length), Base64.NO_WRAP)
            val k = key()
            if (k == null) null else {
                val c = javax.crypto.Cipher.getInstance("AES/GCM/NoPadding")
                c.init(javax.crypto.Cipher.DECRYPT_MODE, k, javax.crypto.spec.GCMParameterSpec(128, all.copyOfRange(0, 12)))
                c.doFinal(all, 12, all.size - 12)
            }
        }
    } catch (_: Throwable) {
        null
    }
}
