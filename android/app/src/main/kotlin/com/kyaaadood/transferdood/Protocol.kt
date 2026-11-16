package com.kyaaadood.transferdood

import android.net.Uri
import java.io.EOFException
import java.io.InputStream
import java.net.InetAddress

/** Constantes del protocolo. Ver PROTOCOL.md (es el mismo que usa la versión de Windows). */
object Protocol {
    const val PORT = 47615
    const val VERSION = 1
    const val BUFFER = 256 * 1024
    const val MAX_HEADER = 16 * 1024 * 1024
    const val DATA_TIMEOUT_MS = 120_000   // sin datos: se considera caída (permite pausas cortas)
    const val MAX_PAUSE_MS = 90_000L      // una pausa se reanuda sola pasado este tiempo
    const val MAX_TEXT = 60_000
    val MAGIC = byteArrayOf('S'.code.toByte(), 'D'.code.toByte(), 'D'.code.toByte(), '1'.code.toByte())
}

class Peer(
    val id: String,
    var name: String,
    var os: String,
    var address: InetAddress,
    var port: Int,
    var lastSeen: Long,
    var ttlMs: Long = 15_000
) {
    fun copy() = Peer(id, name, os, address, port, lastSeen, ttlMs)
}

/** Algo que el usuario eligió para enviar: un archivo o una carpeta completa (árbol SAF). */
class PendingItem(val uri: Uri, val isTree: Boolean)

/** Un archivo concreto listo para enviarse. [relName] puede incluir subcarpetas con '/'. */
class SendItem(var uri: Uri, val relName: String, var size: Long)

class FileEntry(val name: String, val size: Long)

fun InputStream.readFully(buf: ByteArray, count: Int) {
    var off = 0
    while (off < count) {
        val n = read(buf, off, count - off)
        if (n < 0) throw EOFException(Loc.t("La conexión se cerró."))
        off += n
    }
}

fun formatBytes(b: Long): String {
    val units = arrayOf("B", "KB", "MB", "GB", "TB")
    var v = b.toDouble()
    var i = 0
    while (v >= 1024 && i < units.size - 1) {
        v /= 1024
        i++
    }
    return if (i == 0) "$b B" else String.format(java.util.Locale.US, "%.1f %s", v, units[i]).replace(".0 ", " ")
}
