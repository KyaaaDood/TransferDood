package com.kyaaadood.transferdood

import android.net.Uri
import android.os.SystemClock
import org.json.JSONArray
import org.json.JSONObject
import java.io.EOFException
import java.io.File
import java.io.IOException
import java.net.InetSocketAddress
import java.net.ServerSocket
import java.net.Socket
import java.net.SocketException
import java.net.SocketTimeoutException
import java.nio.ByteBuffer
import java.util.concurrent.atomic.AtomicLong
import kotlin.concurrent.thread

enum class JobState { WAITING, ACTIVE, DONE, FAILED, DECLINED, CANCELLED }

class TransferJob(val incoming: Boolean, val peerName: String) {
    @Volatile var kind: String = "files" // files | text
    @Volatile var text: String? = null
    @Volatile var timeMs: Long = System.currentTimeMillis()
    @Volatile var paused = false
    @Volatile var title: String = ""
    @Volatile var fileCount: Int = 0
    @Volatile var totalBytes: Long = 0
    @Volatile var error: String? = null
    @Volatile var openUri: Uri? = null
    @Volatile var cancelled = false
    @Volatile var socket: Socket? = null
    @Volatile private var activeSince = 0L
    @Volatile var state = JobState.WAITING
        private set

    val done = AtomicLong(0)

    val isFinished: Boolean
        get() = state == JobState.DONE || state == JobState.FAILED ||
                state == JobState.DECLINED || state == JobState.CANCELLED

    /** Bytes por segundo promedio desde que empezó. */
    val speed: Double
        get() {
            if (state != JobState.ACTIVE || activeSince == 0L) return 0.0
            val secs = (SystemClock.elapsedRealtime() - activeSince) / 1000.0
            return if (secs < 0.5) 0.0 else done.get() / secs
        }

    internal fun restoreState(s: JobState) {
        state = s
    }

    fun set(s: JobState, err: String? = null) {
        if (err != null) error = err
        if (s == JobState.ACTIVE) activeSince = SystemClock.elapsedRealtime()
        state = s
        Core.jobChanged(this)
    }

    fun togglePause() {
        if (state != JobState.ACTIVE || kind != "files") return
        paused = !paused
        Core.jobChanged(this)
    }

    /** Espera mientras la transferencia está en pausa (se reanuda sola tras MAX_PAUSE_MS). */
    fun waitPause() {
        if (!paused) return
        val start = SystemClock.elapsedRealtime()
        while (paused && !cancelled) {
            if (SystemClock.elapsedRealtime() - start >= Protocol.MAX_PAUSE_MS) {
                paused = false
                Core.jobChanged(this)
                break
            }
            Thread.sleep(150)
        }
    }

    fun toJson(): JSONObject = JSONObject()
        .put("in", incoming).put("peer", peerName).put("title", title).put("count", fileCount)
        .put("total", totalBytes).put("state", state.name).put("err", error ?: JSONObject.NULL)
        .put("uri", openUri?.toString() ?: JSONObject.NULL).put("kind", kind)
        .put("text", text?.take(4000) ?: JSONObject.NULL).put("time", timeMs)

    fun cancel() {
        cancelled = true
        try {
            socket?.close()
        } catch (_: Exception) {
        }
    }
}

/** Recupera una transferencia terminada guardada en el historial. */
fun restoreJob(o: JSONObject): TransferJob? = try {
    val j = TransferJob(o.optBoolean("in"), o.optString("peer", ""))
    j.title = o.optString("title", "")
    j.fileCount = o.optInt("count")
    j.totalBytes = o.optLong("total")
    j.error = if (o.isNull("err")) null else o.optString("err")
    j.openUri = if (o.isNull("uri")) null else Uri.parse(o.optString("uri"))
    j.kind = o.optString("kind", "files")
    j.text = if (o.isNull("text")) null else o.optString("text")
    j.timeMs = o.optLong("time")
    j.restoreState(JobState.valueOf(o.optString("state", "FAILED")))
    if (j.state == JobState.DONE) j.done.set(j.totalBytes)
    j
} catch (_: Exception) {
    null
}

internal fun friendly(e: Throwable): String = when (e) {
    is SocketTimeoutException -> Loc.t("El otro equipo no respondió.")
    is EOFException -> Loc.t("La conexión se interrumpió.")
    is SocketException -> Loc.t("La conexión se interrumpió.")
    is SecurityException -> Loc.t("No hay permiso para acceder al archivo.")
    else -> e.message ?: Loc.t("Falló la transferencia.")
}

// ───────────────────────────── Envío ─────────────────────────────

object TransferClient {

    fun start(peer: Peer, sources: List<PendingItem>) {
        val job = TransferJob(false, peer.name)
        job.title = Loc.t("Preparando…")
        Core.addJob(job)
        thread(name = "sd-send", isDaemon = true) { run(job, peer, sources, null) }
    }

    fun startText(peer: Peer, text: String) {
        val t = text.take(Protocol.MAX_TEXT)
        val job = TransferJob(false, peer.name)
        job.kind = "text"
        job.text = t
        job.title = preview(t)
        Core.addJob(job)
        thread(name = "sd-send", isDaemon = true) { run(job, peer, emptyList(), t) }
    }

    fun preview(t: String): String {
        val c = t.map { if (it.isISOControl()) ' ' else it }.joinToString("").trim()
        return if (c.length > 80) c.take(80) + "…" else c
    }

    private fun run(job: TransferJob, peer: Peer, sources: List<PendingItem>, text: String?) {
        val staged = ArrayList<File>()
        try {
            val ctx = Core.app
            val items = ArrayList<SendItem>()
            if (text == null) {
                for (s in sources) {
                    if (job.cancelled) throw InterruptedException()
                    if (s.isTree) {
                        items.addAll(FilesUtil.walkTree(ctx, s.uri))
                    } else {
                        val (name, size) = FilesUtil.describe(ctx, s.uri)
                        items.add(SendItem(s.uri, name, size))
                    }
                }
                if (items.isEmpty()) {
                    job.set(JobState.FAILED, Loc.t("No hay archivos para enviar."))
                    return
                }
                FilesUtil.resolveSizes(ctx, items, staged) { job.cancelled }

                job.fileCount = items.size
                job.totalBytes = items.sumOf { it.size }
                job.title = when {
                    items.size == 1 -> items[0].relName.substringAfterLast('/')
                    sources.size == 1 && sources[0].isTree -> Loc.f("{0} ({1} archivos)", items[0].relName.substringBefore('/'), items.size)
                    else -> Loc.f("{0} archivos", items.size)
                }
                Core.jobChanged(job)
            }

            val s = Socket()
            job.socket = s
            s.tcpNoDelay = true
            try {
                s.connect(InetSocketAddress(peer.address, peer.port), 6000)
            } catch (e: IOException) {
                if (job.cancelled) throw e
                throw IOException(Loc.t("No se pudo conectar con el equipo. Revisa que esté abierto y el firewall."))
            }

            s.use {
                val ch = SecureChannel.client(s, peer.id)
                val out = ch.out
                val inp = ch.inp

                // Cabecera
                val files = JSONArray()
                for (i in items) files.put(JSONObject().put("name", i.relName).put("size", i.size))
                val hj = JSONObject()
                    .put("from", Core.prefs.effectiveName)
                    .put("id", Core.prefs.deviceId)
                    .put("total", job.totalBytes)
                    .put("files", files)
                if (text != null) hj.put("kind", "text").put("text", text)
                Crypto.sign(hj, peer.id)
                val header = hj.toString().toByteArray(Charsets.UTF_8)
                out.write(Protocol.MAGIC)
                out.write(ByteBuffer.allocate(4).putInt(header.size).array())
                out.write(header)
                out.flush()

                // Esperar a que el otro equipo acepte (hasta 75 s)
                s.soTimeout = 75_000
                val answer = try {
                    inp.read()
                } catch (e: SocketTimeoutException) {
                    throw IOException(Loc.t("El otro equipo no respondió."))
                }
                if (answer < 0) throw IOException(Loc.t("El otro equipo cerró la conexión."))
                if (answer != 1) {
                    job.set(JobState.DECLINED)
                    return
                }

                job.set(JobState.ACTIVE)
                s.soTimeout = 30_000

                val buf = ByteArray(Protocol.BUFFER)
                for (item in items) {
                    val input = ctx.contentResolver.openInputStream(item.uri)
                        ?: throw IOException(Loc.f("No se pudo leer: {0}", item.relName))
                    input.use {
                        var remaining = item.size
                        while (remaining > 0) {
                            if (job.cancelled) throw InterruptedException()
                            job.waitPause()
                            val want = minOf(buf.size.toLong(), remaining).toInt()
                            val n = input.read(buf, 0, want)
                            if (n < 0) throw IOException(Loc.t("El archivo cambió mientras se enviaba: ") + item.relName)
                            out.write(buf, 0, n)
                            remaining -= n
                            job.done.addAndGet(n.toLong())
                        }
                    }
                }
                out.flush()

                // Confirmación final del receptor
                val ack = try {
                    inp.read()
                } catch (e: SocketTimeoutException) {
                    throw IOException(Loc.t("El otro equipo no confirmó la recepción."))
                }
                if (ack == 1) job.set(JobState.DONE)
                else job.set(JobState.FAILED, Loc.t("El otro equipo no confirmó la recepción."))
            }
        } catch (e: Exception) {
            if (job.cancelled || e is InterruptedException) job.set(JobState.CANCELLED)
            else job.set(JobState.FAILED, friendly(e))
        } finally {
            for (f in staged) f.delete()
        }
    }
}

// ───────────────────────────── Recepción ─────────────────────────────

class TransferServer {
    // Límites contra abuso en la red local: pocas conexiones a la vez y pocos avisos pendientes por equipo.
    private val conns = java.util.concurrent.atomic.AtomicInteger(0)
    private val pendingByIp = HashMap<String, Int>()
    private val MAX_CONNS = 16
    private val MAX_PENDING_PER_IP = 3

    private fun enterPrompt(ip: String): Boolean = synchronized(pendingByIp) {
        val n = pendingByIp[ip] ?: 0
        if (n >= MAX_PENDING_PER_IP) false else { pendingByIp[ip] = n + 1; true }
    }

    private fun leavePrompt(ip: String) = synchronized(pendingByIp) {
        val n = pendingByIp[ip] ?: 0
        if (n <= 1) pendingByIp.remove(ip) else pendingByIp[ip] = n - 1
    }

    @Volatile private var server: ServerSocket? = null
    @Volatile private var running = false

    fun start() {
        val ss = ServerSocket()
        ss.reuseAddress = true
        ss.bind(InetSocketAddress(Protocol.PORT), 32)
        server = ss
        running = true
        thread(name = "sd-accept", isDaemon = true) {
            while (running) {
                try {
                    val c = ss.accept()
                    if (conns.incrementAndGet() > MAX_CONNS) {
                        conns.decrementAndGet()
                        try { c.close() } catch (_: Exception) {}
                        continue
                    }
                    thread(name = "sd-recv", isDaemon = true) {
                        try { handle(c) } finally { conns.decrementAndGet() }
                    }
                } catch (_: Exception) {
                    if (!running) break
                    try {
                        Thread.sleep(100)
                    } catch (_: InterruptedException) {
                        break
                    }
                }
            }
        }
    }

    fun stop() {
        running = false
        try {
            server?.close()
        } catch (_: Exception) {
        }
        server = null
    }

    private fun handle(c: Socket) {
        var job: TransferJob? = null
        var cur: Output? = null
        try {
            c.use { sock ->
                sock.tcpNoDelay = true
                sock.soTimeout = 10_000
                val ch = SecureChannel.server(sock) ?: return
                val inp = ch.inp
                val out = ch.out
                val ip = sock.inetAddress?.hostAddress ?: ""

                // 1) Cabecera
                val pre = ByteArray(8)
                inp.readFully(pre, 8)
                for (i in 0 until 4) if (pre[i] != Protocol.MAGIC[i]) return
                val len = ByteBuffer.wrap(pre, 4, 4).int
                if (len <= 0 || len > Protocol.MAX_HEADER) return
                val hb = ByteArray(len)
                inp.readFully(hb, len)

                val h = JSONObject(String(hb, Charsets.UTF_8))
                val fromId = h.optString("id", "").take(64)

                // Equipos bloqueados: se rechazan sin mostrar nada.
                if (Core.prefs.isBlocked(fromId)) {
                    out.write(0)
                    out.flush()
                    return
                }

                val kind = h.optString("kind", "files")
                if (kind == "pair") {
                    handlePair(sock, inp, out, h, fromId, ip)
                    return
                }
                val isText = kind == "text"
                val text = if (isText) h.optString("text", "") else ""
                if (isText && (text.isEmpty() || text.length > Protocol.MAX_TEXT)) return

                val files = if (isText) JSONArray() else h.getJSONArray("files")
                val n = files.length()
                if (!isText && (n == 0 || n > 500_000)) return

                val sizes = LongArray(n)
                val parts = ArrayList<List<String>>(n)
                var total = 0L
                for (i in 0 until n) {
                    val f = files.getJSONObject(i)
                    val sz = f.getLong("size")
                    if (sz < 0) return
                    total = Math.addExact(total, sz)
                    sizes[i] = sz
                    parts.add(Storage.cleanParts(f.optString("name", "")))
                }

                val fromName = h.optString("from", "").filter { !it.isISOControl() }.trim().take(48)
                    .ifEmpty { Loc.t("Otro equipo") }
                val allInFolder = parts.all { it.size > 1 } && parts.map { it[0].lowercase() }.distinct().size == 1
                val title = when {
                    isText -> TransferClient.preview(text)
                    n == 1 -> parts[0].last()
                    allInFolder -> Loc.f("{0} ({1} archivos)", parts[0][0], n)
                    else -> Loc.f("{0} archivos", n)
                }

                val j = TransferJob(true, fromName)
                j.title = title
                j.fileCount = n
                j.totalBytes = total
                j.socket = sock
                if (isText) {
                    j.kind = "text"
                    j.text = text
                }
                job = j
                Core.addJob(j)

                // 2) Espacio libre
                if (!isText && Storage.freeSpace() < total + 16L * 1024 * 1024) {
                    out.write(0)
                    out.flush()
                    j.set(JobState.FAILED, Loc.t("No hay espacio suficiente en el teléfono."))
                    return
                }

                // 3) Preguntar al usuario
                var accepted = Core.prefs.autoAccept || Crypto.verifyTrusted(h)
                if (!accepted) {
                    if (!enterPrompt(ip)) {
                        out.write(0)
                        out.flush()
                        j.set(JobState.DECLINED)
                        return
                    }
                    try {
                        accepted = Core.ask(
                            IncomingRequest(
                                Core.newRequestId(), fromId, fromName, n, if (isText) text.length.toLong() else total,
                                if (isText) "" else parts[0].last(),
                                kind = if (isText) "text" else "files",
                                preview = if (isText) TransferClient.preview(text) else "",
                                ip = ip
                            )
                        )
                    } finally {
                        leavePrompt(ip)
                    }
                }
                out.write(if (accepted) 1 else 0)
                out.flush()
                if (!accepted) {
                    j.set(JobState.DECLINED)
                    return
                }

                if (isText) {
                    Core.onTextReceived(fromName, text)
                    out.write(1)
                    out.flush()
                    j.set(JobState.DONE)
                    return
                }

                j.set(JobState.ACTIVE)
                sock.soTimeout = Protocol.DATA_TIMEOUT_MS

                // 4) Datos
                val buf = ByteArray(Protocol.BUFFER)
                var last: Uri? = null
                for (i in 0 until n) {
                    val p = parts[i]
                    val o = Storage.create(Core.app, p.dropLast(1), p.last())
                    cur = o
                    var remaining = sizes[i]
                    while (remaining > 0) {
                        if (j.cancelled) throw InterruptedException()
                        j.waitPause()
                        val want = minOf(buf.size.toLong(), remaining).toInt()
                        val r = try {
                            inp.read(buf, 0, want)
                        } catch (e: SocketTimeoutException) {
                            throw IOException(Loc.t("El otro equipo dejó de enviar datos."))
                        }
                        if (r < 0) throw EOFException(Loc.t("La conexión se cerró antes de terminar."))
                        o.stream.write(buf, 0, r)
                        remaining -= r
                        j.done.addAndGet(r.toLong())
                    }
                    last = o.commit()
                    cur = null
                }

                j.openUri = if (n == 1) last else null
                out.write(1)
                out.flush()
                j.set(JobState.DONE)
            }
        } catch (e: Exception) {
            try {
                cur?.abort()
            } catch (_: Exception) {
            }
            val j = job
            if (j != null) {
                if (j.cancelled) j.set(JobState.CANCELLED) else j.set(JobState.FAILED, friendly(e))
            }
        }
    }

    /** Emparejamiento: intercambio de claves públicas; ambos usuarios confirman que el código coincide. */
    private fun handlePair(sock: Socket, inp: java.io.InputStream, out: java.io.OutputStream, h: JSONObject, fromId: String, ip: String) {
        val pubB64 = h.optString("pub", "")
        if (pubB64.isEmpty() || fromId.isEmpty()) return
        if (!enterPrompt(ip)) return
        try {
            handlePairCore(sock, inp, out, h, fromId, ip, pubB64)
        } finally {
            leavePrompt(ip)
        }
    }

    private fun handlePairCore(sock: Socket, inp: java.io.InputStream, out: java.io.OutputStream, h: JSONObject, fromId: String, ip: String, pubB64: String) {
        val peerPub = android.util.Base64.decode(pubB64, android.util.Base64.NO_WRAP)
        if (peerPub.size > 4096) return

        val kp = Crypto.newKeyPair()
        val myPub = kp.public.encoded
        val code = Crypto.sas(peerPub, myPub)
        out.write(3)
        out.write(ByteBuffer.allocate(4).putInt(myPub.size).array())
        out.write(myPub)
        out.flush()

        val name = h.optString("from", "").filter { !it.isISOControl() }.trim().take(48).ifEmpty { Loc.t("Otro equipo") }
        val req = IncomingRequest(Core.newRequestId(), fromId, name, 0, 0, "", kind = "pair", code = code, ip = ip)
        val userOk = java.util.concurrent.atomic.AtomicBoolean(false)
        val t = thread(name = "sd-pair-ask", isDaemon = true) { userOk.set(Core.ask(req)) }

        sock.soTimeout = 70_000
        val theirs = inp.read() == 1
        t.join(66_000)
        val ok = theirs && userOk.get()
        out.write(if (ok) 1 else 0)
        out.flush()
        if (ok) {
            Core.prefs.pair(fromId, name, Crypto.derive(kp.private, peerPub))
            Core.toast(Loc.f("Emparejado con {0}", name))
            Core.notifyChanged()
        }
    }
}
