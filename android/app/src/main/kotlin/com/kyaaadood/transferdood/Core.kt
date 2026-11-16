package com.kyaaadood.transferdood

import android.app.Application
import android.app.Dialog
import android.content.Context
import android.content.Intent
import android.net.Uri
import android.net.wifi.WifiManager
import android.os.Build
import android.os.Handler
import android.os.Looper
import android.os.PowerManager
import java.util.concurrent.CompletableFuture
import java.util.concurrent.CopyOnWriteArrayList
import java.util.concurrent.CopyOnWriteArraySet
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicInteger

class App : Application() {
    override fun onCreate() {
        super.onCreate()
        Core.init(this)
    }
}

/** Un envío entrante esperando que el usuario lo acepte o lo rechace. */
class IncomingRequest(
    val id: Int,
    val fromId: String,
    val from: String,
    val count: Int,
    val total: Long,
    val firstName: String,
    val kind: String = "files",   // files | text | pair
    val preview: String = "",     // vista previa del texto recibido
    val code: String = "",        // código de emparejamiento
    val initiator: Boolean = false,
    val ip: String = ""
) {
    val answer = CompletableFuture<Boolean>()

    @Volatile
    var dialog: Dialog? = null
}

/** Estado global de la app (una sola instancia por proceso). */
object Core {
    lateinit var app: Application
        private set
    lateinit var prefs: Prefs
        private set

    val main = Handler(Looper.getMainLooper())

    @Volatile var discovery: Discovery? = null
    @Volatile var service: CoreService? = null
    @Volatile var networkError: String? = null

    val pending = CopyOnWriteArrayList<PendingItem>()
    @Volatile var pendingText: String? = null
    val jobs = CopyOnWriteArrayList<TransferJob>()
    val incoming = CopyOnWriteArrayList<IncomingRequest>()

    private val listeners = CopyOnWriteArraySet<Runnable>()
    private val ids = AtomicInteger(1)

    @Volatile private var visibleCount = 0
    val uiVisible: Boolean get() = visibleCount > 0

    /** La pantalla principal (la que muestra los avisos de envío) está a la vista. */
    @Volatile var mainVisible = false
        private set

    /** Mientras hay transferencias, refresca el progreso de la notificación fija cada segundo. */
    private val progressTick = object : Runnable {
        override fun run() {
            Notifier.updateForeground()
            if (locksHeld) main.postDelayed(this, 1000)
        }
    }

    fun notificationsEnabled(ctx: Context): Boolean = try {
        ctx.getSystemService(android.app.NotificationManager::class.java).areNotificationsEnabled()
    } catch (_: Exception) {
        true
    }

    private var wake: PowerManager.WakeLock? = null
    private var wifi: WifiManager.WifiLock? = null
    private var locksHeld = false

    fun init(a: Application) {
        app = a
        prefs = Prefs(a)
        Loc.setting = prefs.language
        for (o in prefs.loadHistory()) restoreJob(o)?.let { jobs.add(it) }
        Notifier.init(a)
    }

    // ── Escuchas de la interfaz ──

    fun addListener(r: Runnable) {
        listeners.add(r)
    }

    fun removeListener(r: Runnable) {
        listeners.remove(r)
    }

    fun notifyChanged() {
        main.post { for (l in listeners) l.run() }
    }

    // ── Visibilidad de la interfaz ──

    /** Cualquier pantalla de la app (mantiene vivo el servicio). */
    fun onUiShown() {
        visibleCount++
        discovery?.setActive(true)
    }

    fun onUiHidden() {
        if (visibleCount > 0) visibleCount--
        if (visibleCount == 0) {
            discovery?.setActive(false)
            maybeStopService()
        }
    }

    /** La pantalla principal muestra los avisos como diálogo; si no está a la vista, van como notificación. */
    fun onMainShown() {
        mainVisible = true
        for (r in incoming) Notifier.cancelPrompt(r)
        notifyChanged()
    }

    fun onMainHidden() {
        mainVisible = false
        for (r in incoming) if (!r.answer.isDone) Notifier.showPrompt(r)
    }

    // ── Servicio ──

    fun startService(ctx: Context) {
        val i = Intent(ctx, CoreService::class.java)
        try {
            if (Build.VERSION.SDK_INT >= 26) ctx.startForegroundService(i) else ctx.startService(i)
        } catch (e: Exception) {
            networkError = Loc.f("No se pudo iniciar el servicio: {0}", e.message)
            notifyChanged()
        }
    }

    /** Apaga el servicio si nadie lo necesita (app cerrada, sin transferencias y sin modo segundo plano). */
    fun maybeStopService() {
        if (uiVisible || prefs.background) return
        if (jobs.any { !it.isFinished } || incoming.any { !it.answer.isDone }) return
        service?.stopSelf()
    }

    // ── Archivos pendientes ──

    fun queue(uris: List<Uri>, tree: Boolean) {
        var added = false
        for (u in uris) {
            if (pending.none { it.uri == u }) {
                pending.add(PendingItem(u, tree))
                added = true
            }
        }
        if (added) notifyChanged()
    }

    fun queueText(text: String) {
        if (text.isBlank()) return
        pendingText = text
        notifyChanged()
    }

    val hasPending: Boolean get() = pending.isNotEmpty() || pendingText != null

    fun clearPending() {
        pending.clear()
        pendingText = null
        notifyChanged()
    }

    fun sendPending(peer: Peer) {
        val t = pendingText
        if (t != null) {
            pendingText = null
            TransferClient.startText(peer, t)
            notifyChanged()
            return
        }
        if (pending.isEmpty()) return
        val list = pending.toList()
        pending.clear()
        TransferClient.start(peer, list)
        notifyChanged()
    }

    // ── Transferencias ──

    fun addJob(job: TransferJob) {
        jobs.add(job)
        while (jobs.size > 60) {
            val old = jobs.firstOrNull { it.isFinished } ?: break
            jobs.remove(old)
        }
        updateLocks()
        notifyChanged()
    }

    fun clearFinished() {
        jobs.removeAll { it.isFinished }
        saveHistory()
        notifyChanged()
    }

    private fun saveHistory() {
        try {
            prefs.saveHistory(jobs.filter { it.isFinished }.map { it.toJson() })
        } catch (_: Exception) {
        }
    }

    fun toast(msg: String) {
        main.post {
            try {
                android.widget.Toast.makeText(app, msg, android.widget.Toast.LENGTH_LONG).show()
            } catch (_: Exception) {
            }
        }
    }

    /** Llegó un texto: se copia al portapapeles (si el sistema lo permite) y queda en el historial. */
    fun onTextReceived(from: String, text: String) {
        main.post {
            try {
                val cm = app.getSystemService(Context.CLIPBOARD_SERVICE) as android.content.ClipboardManager
                cm.setPrimaryClip(android.content.ClipData.newPlainText("TransferDood", text))
            } catch (_: Exception) {
            }
        }
    }

    fun jobChanged(job: TransferJob) {
        updateLocks()
        if (job.state == JobState.DONE && !mainVisible) Notifier.showDone(job)
        notifyChanged()
        if (job.isFinished) {
            saveHistory()
            maybeStopService()
        }
    }

    @Synchronized
    private fun updateLocks() {
        val active = jobs.any { !it.isFinished }
        if (active == locksHeld) return
        locksHeld = active
        if (active) {
            main.removeCallbacks(progressTick)
            main.post(progressTick)
        } else {
            main.removeCallbacks(progressTick)
            main.post { Notifier.updateForeground() }
        }
        try {
            if (active) {
                val pm = app.getSystemService(Context.POWER_SERVICE) as PowerManager
                wake = pm.newWakeLock(PowerManager.PARTIAL_WAKE_LOCK, "transferdood:transfer").apply {
                    setReferenceCounted(false)
                    acquire(4 * 60 * 60 * 1000L)
                }
                val wm = app.applicationContext.getSystemService(Context.WIFI_SERVICE) as WifiManager
                @Suppress("DEPRECATION")
                wifi = wm.createWifiLock(WifiManager.WIFI_MODE_FULL_HIGH_PERF, "transferdood").apply {
                    setReferenceCounted(false)
                    acquire()
                }
            } else {
                wake?.let { if (it.isHeld) it.release() }
                wifi?.let { if (it.isHeld) it.release() }
                wake = null
                wifi = null
            }
        } catch (_: Exception) {
        }
    }

    // ── Envíos entrantes ──

    fun newRequestId(): Int = ids.getAndIncrement()

    /** Bloquea el hilo que lo llama (nunca el principal) hasta que el usuario responde o pasan 65 s. */
    fun ask(req: IncomingRequest): Boolean {
        incoming.add(req)
        main.post {
            if (mainVisible) notifyChanged() else Notifier.showPrompt(req)
        }
        val ok = try {
            req.answer.get(65, TimeUnit.SECONDS)
        } catch (_: Exception) {
            false
        }
        req.answer.complete(false)
        incoming.remove(req)
        main.post {
            Notifier.cancelPrompt(req)
            req.dialog?.dismiss()
            notifyChanged()
        }
        return ok
    }
}
