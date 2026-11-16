package com.kyaaadood.transferdood

import java.io.EOFException
import java.io.IOException
import java.io.InputStream
import java.io.OutputStream
import java.net.Socket
import java.security.GeneralSecurityException
import java.security.PrivateKey
import javax.crypto.Cipher
import javax.crypto.Mac
import javax.crypto.spec.GCMParameterSpec
import javax.crypto.spec.SecretKeySpec

/**
 * Cifrado de toda la conexión (AES-256-GCM). Mismo esquema que en Windows:
 * ECDH P-256 efímero por conexión; si ambos son favoritos, el secreto del emparejamiento se mezcla en la clave.
 *
 *   Cliente → servidor: "SDE1" | flags(1) | idLen(1) | id | pubLen(1) | pub (91 bytes)
 *   Servidor → cliente: flags(1) | pubLen(1) | pub        (flags bit0 = se mezcló el secreto de favoritos)
 *   Luego, en cada sentido: tramos [len(4, BE)] [cifrado(len)] [etiqueta(16)], nonce = contador de 64 bits.
 */
object SecureChannel {
    private const val PUB_LEN = 91
    private val HELLO = "SDE1".toByteArray(Charsets.US_ASCII)

    class Streams(val inp: InputStream, val out: OutputStream)

    private fun hmac(key: ByteArray, data: ByteArray): ByteArray {
        val m = Mac.getInstance("HmacSHA256")
        m.init(SecretKeySpec(key, "HmacSHA256"))
        return m.doFinal(data)
    }

    fun client(sock: Socket, peerId: String): Streams {
        val rin = sock.getInputStream()
        val rout = sock.getOutputStream()
        val kp = Crypto.newKeyPair()
        val pub = kp.public.encoded
        if (pub.size != PUB_LEN) throw IOException("clave no válida")
        val secret = if (peerId.isEmpty()) null else Core.prefs.secret(peerId)
        var id = Core.prefs.deviceId.toByteArray(Charsets.UTF_8)
        if (id.size > 64) id = id.copyOf(64)

        val msg = ByteArray(4 + 1 + 1 + id.size + 1 + PUB_LEN)
        HELLO.copyInto(msg, 0)
        msg[4] = (if (secret != null) 1 else 0).toByte()
        msg[5] = id.size.toByte()
        id.copyInto(msg, 6)
        msg[6 + id.size] = PUB_LEN.toByte()
        pub.copyInto(msg, 7 + id.size)

        sock.soTimeout = 10_000
        rout.write(msg)
        rout.flush()
        val head = ByteArray(2)
        rin.readFully(head, 2)
        if (head[1].toInt() != PUB_LEN) throw IOException(Loc.t("Respuesta no válida."))
        val spub = ByteArray(PUB_LEN)
        rin.readFully(spub, PUB_LEN)

        val mixed = (head[0].toInt() and 1) != 0 && secret != null
        return build(rin, rout, kp.private, pub, spub, if (mixed) secret else null, true)
    }

    /** Devuelve null si el otro lado no habla este protocolo. */
    fun server(sock: Socket): Streams? {
        val rin = sock.getInputStream()
        val rout = sock.getOutputStream()
        val pre = ByteArray(6)
        rin.readFully(pre, 6)
        for (i in 0 until 4) if (pre[i] != HELLO[i]) return null
        val idLen = pre[5].toInt() and 0xFF
        if (idLen > 64) return null
        val idb = ByteArray(idLen)
        if (idLen > 0) rin.readFully(idb, idLen)
        if (rin.read() != PUB_LEN) return null
        val cpub = ByteArray(PUB_LEN)
        rin.readFully(cpub, PUB_LEN)

        val secret = if ((pre[4].toInt() and 1) != 0) Core.prefs.secret(String(idb, Charsets.UTF_8)) else null
        val kp = Crypto.newKeyPair()
        val pub = kp.public.encoded
        if (pub.size != PUB_LEN) return null
        val reply = ByteArray(2 + pub.size)
        reply[0] = (if (secret != null) 1 else 0).toByte()
        reply[1] = pub.size.toByte()
        pub.copyInto(reply, 2)
        rout.write(reply)
        rout.flush()

        return build(rin, rout, kp.private, pub, cpub, secret, false)
    }

    /** Claves de cada sentido (cliente→servidor, servidor→cliente). */
    fun deriveKeys(shared: ByteArray, epkC: ByteArray, epkS: ByteArray, secret: ByteArray?): Pair<ByteArray, ByteArray> {
        val data = "tdood-e2e".toByteArray(Charsets.US_ASCII) + shared + epkC + epkS
        val prk = hmac(secret ?: ByteArray(32), data)
        return Pair(
            hmac(prk, "c2s\u0001".toByteArray(Charsets.US_ASCII)),
            hmac(prk, "s2c\u0001".toByteArray(Charsets.US_ASCII))
        )
    }

    private fun build(
        rin: InputStream, rout: OutputStream, mine: PrivateKey, myPub: ByteArray, peerPub: ByteArray,
        secret: ByteArray?, client: Boolean
    ): Streams {
        val shared = Crypto.ecdhRaw(mine, peerPub)
        val epkC = if (client) myPub else peerPub
        val epkS = if (client) peerPub else myPub
        val (kc, ks) = deriveKeys(shared, epkC, epkS, secret)
        return if (client) Streams(SecureIn(rin, ks), SecureOut(rout, kc)) else Streams(SecureIn(rin, kc), SecureOut(rout, ks))
    }
}

private const val MAX_FRAME = 64 * 1024

private fun putCounter(iv: ByteArray, v: Long) {
    for (i in 0 until 8) iv[4 + i] = (v ushr (56 - 8 * i)).toByte()
}

/** Escritura cifrada por tramos de hasta 64 KB. */
class SecureOut(private val inner: OutputStream, key: ByteArray) : OutputStream() {
    private val spec = SecretKeySpec(key, "AES")
    private val cipher = Cipher.getInstance("AES/GCM/NoPadding")
    private val frame = ByteArray(4 + MAX_FRAME + 16)
    private val iv = ByteArray(12)
    private var ctr = 0L

    override fun write(b: Int) {
        write(byteArrayOf(b.toByte()), 0, 1)
    }

    @Synchronized
    override fun write(b: ByteArray, off: Int, len: Int) {
        var o = off
        var left = len
        while (left > 0) {
            val n = minOf(left, MAX_FRAME)
            frame[0] = (n ushr 24).toByte()
            frame[1] = (n ushr 16).toByte()
            frame[2] = (n ushr 8).toByte()
            frame[3] = n.toByte()
            putCounter(iv, ctr++)
            cipher.init(Cipher.ENCRYPT_MODE, spec, GCMParameterSpec(128, iv))
            cipher.updateAAD(frame, 0, 4)
            cipher.doFinal(b, o, n, frame, 4)
            inner.write(frame, 0, 4 + n + 16)
            o += n
            left -= n
        }
    }

    override fun flush() = inner.flush()
    override fun close() = inner.close()
}

/** Lectura cifrada: devuelve los datos en claro tramo por tramo. */
class SecureIn(private val inner: InputStream, key: ByteArray) : InputStream() {
    private val spec = SecretKeySpec(key, "AES")
    private val cipher = Cipher.getInstance("AES/GCM/NoPadding")
    private val lenBuf = ByteArray(4)
    private val rbuf = ByteArray(MAX_FRAME + 16)
    private val plain = ByteArray(MAX_FRAME)
    private val iv = ByteArray(12)
    private val one = ByteArray(1)
    private var ctr = 0L
    private var rpos = 0
    private var rlen = 0

    override fun read(): Int {
        val n = read(one, 0, 1)
        return if (n < 0) -1 else one[0].toInt() and 0xFF
    }

    override fun read(b: ByteArray, off: Int, len: Int): Int {
        if (len == 0) return 0
        if (rpos >= rlen) {
            var got = 0
            while (got < 4) {
                val r = inner.read(lenBuf, got, 4 - got)
                if (r < 0) {
                    if (got == 0) return -1
                    throw EOFException(Loc.t("La conexión se cerró."))
                }
                got += r
            }
            val n = ((lenBuf[0].toInt() and 0xFF) shl 24) or ((lenBuf[1].toInt() and 0xFF) shl 16) or
                    ((lenBuf[2].toInt() and 0xFF) shl 8) or (lenBuf[3].toInt() and 0xFF)
            if (n <= 0 || n > MAX_FRAME) throw IOException(Loc.t("La conexión no es segura (datos alterados)."))
            inner.readFully(rbuf, n + 16)
            putCounter(iv, ctr++)
            try {
                cipher.init(Cipher.DECRYPT_MODE, spec, GCMParameterSpec(128, iv))
                cipher.updateAAD(lenBuf, 0, 4)
                cipher.doFinal(rbuf, 0, n + 16, plain, 0)
            } catch (e: GeneralSecurityException) {
                throw IOException(Loc.t("La conexión no es segura (datos alterados)."))
            }
            rpos = 0
            rlen = n
        }
        val c = minOf(len, rlen - rpos)
        System.arraycopy(plain, rpos, b, off, c)
        rpos += c
        return c
    }

    override fun available(): Int = rlen - rpos
    override fun close() = inner.close()
}
