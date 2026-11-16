package com.kyaaadood.transferdood

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.graphics.drawable.Icon
import android.os.Build
import java.util.concurrent.atomic.AtomicInteger

object Notifier {
    private const val CH_SERVICE = "service"
    // Canales nuevos ("2"): Android no deja subir la importancia de un canal ya creado, y el anterior se quedó sin avisos flotantes.
    private const val CH_PROMPT = "prompt2"
    private const val CH_DONE = "done2"

    const val FG_ID = 1
    private const val PROMPT_BASE = 1000
    private val doneId = AtomicInteger(5000)

    fun init(ctx: Context) {
        if (Build.VERSION.SDK_INT < 26) return
        val nm = ctx.getSystemService(NotificationManager::class.java)
        nm.createNotificationChannel(
            NotificationChannel(CH_SERVICE, Loc.t("Servicio de recepción"), NotificationManager.IMPORTANCE_LOW).apply {
                setShowBadge(false)
            }
        )
        try {
            nm.deleteNotificationChannel("prompt")
            nm.deleteNotificationChannel("done")
        } catch (_: Exception) {
        }
        val attrs = android.media.AudioAttributes.Builder()
            .setUsage(android.media.AudioAttributes.USAGE_NOTIFICATION)
            .setContentType(android.media.AudioAttributes.CONTENT_TYPE_SONIFICATION)
            .build()
        val sound = android.media.RingtoneManager.getDefaultUri(android.media.RingtoneManager.TYPE_NOTIFICATION)
        // Importancia ALTA + sonido/vibración = aviso flotante en pantalla (heads-up).
        for ((id, name) in listOf(CH_PROMPT to Loc.t("Archivos entrantes"), CH_DONE to Loc.t("Transferencias completadas"))) {
            nm.createNotificationChannel(
                NotificationChannel(id, name, NotificationManager.IMPORTANCE_HIGH).apply {
                    enableVibration(true)
                    vibrationPattern = longArrayOf(0, 180, 120, 180)
                    setSound(sound, attrs)
                    lockscreenVisibility = Notification.VISIBILITY_PRIVATE
                }
            )
        }
    }

    @Suppress("DEPRECATION")
    private fun builder(ctx: Context, channel: String): Notification.Builder =
        if (Build.VERSION.SDK_INT >= 26) Notification.Builder(ctx, channel) else Notification.Builder(ctx)

    private fun openApp(ctx: Context): PendingIntent {
        val i = Intent(ctx, MainActivity::class.java).addFlags(Intent.FLAG_ACTIVITY_SINGLE_TOP or Intent.FLAG_ACTIVITY_CLEAR_TOP)
        return PendingIntent.getActivity(ctx, 0, i, PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT)
    }

    private fun broadcast(ctx: Context, code: Int, action: String, id: Int = -1): PendingIntent {
        val i = Intent(ctx, ActionReceiver::class.java).setAction(action).putExtra("id", id)
        return PendingIntent.getBroadcast(ctx, code, i, PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT)
    }

    private fun action(ctx: Context, label: String, pi: PendingIntent): Notification.Action =
        Notification.Action.Builder(Icon.createWithResource(ctx, R.drawable.ic_notify), label, pi).build()

    fun foreground(ctx: Context): Notification {
        val active = Core.jobs.filter { it.state == JobState.ACTIVE }
        val b = builder(ctx, CH_SERVICE)
            .setSmallIcon(R.drawable.ic_notify)
            .setContentTitle("TransferDood")
            .setContentIntent(openApp(ctx))
            .setOngoing(true)
            .setOnlyAlertOnce(true)
            .addAction(action(ctx, Loc.t("Detener"), broadcast(ctx, 1, ActionReceiver.ACTION_STOP)))

        if (active.isEmpty()) {
            b.setContentText(Loc.f("Listo para recibir como {0}", Core.prefs.effectiveName))
        } else {
            val done = active.sumOf { it.done.get() }
            val total = active.sumOf { it.totalBytes }
            val pct = if (total > 0) ((done * 100) / total).toInt().coerceIn(0, 100) else 0
            val text = if (active.size == 1) {
                val j = active[0]
                "${if (j.incoming) Loc.t("Recibiendo") else Loc.t("Enviando")} ${j.title} · $pct%"
            } else {
                Loc.f("{0} transferencias en curso · {1}%", active.size, pct)
            }
            b.setContentText(text).setProgress(100, pct, false)
        }
        return b.build()
    }

    /** Actualiza la notificación fija (progreso). No hace nada si el servicio no está activo. */
    fun updateForeground() {
        if (Core.service == null) return
        try {
            Core.app.getSystemService(NotificationManager::class.java).notify(FG_ID, foreground(Core.app))
        } catch (_: Exception) {
        }
    }

    private fun describe(req: IncomingRequest): String =
        if (req.kind == "text") req.preview
        else if (req.kind == "pair") Loc.f("Código {0} · debe verse igual en el otro equipo", req.code)
        else if (req.count == 1) "${req.firstName} · ${formatBytes(req.total)}"
        else "${Loc.f("{0} archivos", req.count)} · ${formatBytes(req.total)}"

    @Suppress("DEPRECATION")
    fun showPrompt(req: IncomingRequest) {
        val ctx = Core.app
        val b = builder(ctx, CH_PROMPT)
            .setSmallIcon(R.drawable.ic_notify)
            .setContentTitle(
                when (req.kind) {
                    "text" -> Loc.f("{0} quiere enviarte un texto", req.from)
                    "pair" -> if (req.initiator) Loc.f("Emparejar con {0}", req.from) else Loc.f("{0} quiere emparejarse", req.from)
                    else -> Loc.f("{0} quiere enviarte", req.from)
                }
            )
            .setContentText(describe(req) + if (req.ip.isNotEmpty()) " · " + req.ip else "")
            .setContentIntent(openApp(ctx))
            .setAutoCancel(true)
            .setCategory(Notification.CATEGORY_MESSAGE)
            .addAction(action(ctx, if (req.kind == "pair") Loc.t("Coincide") else Loc.t("Aceptar"), broadcast(ctx, req.id * 2 + 10, ActionReceiver.ACTION_ACCEPT, req.id)))
            .addAction(action(ctx, if (req.kind == "pair") Loc.t("No coincide") else Loc.t("Rechazar"), broadcast(ctx, req.id * 2 + 11, ActionReceiver.ACTION_DECLINE, req.id)))
        if (Build.VERSION.SDK_INT >= 26) b.setTimeoutAfter(65_000)
        else b.setPriority(Notification.PRIORITY_HIGH).setDefaults(Notification.DEFAULT_ALL)
        try {
            ctx.getSystemService(NotificationManager::class.java).notify(PROMPT_BASE + req.id, b.build())
        } catch (_: Exception) {
        }
    }

    fun cancelPrompt(req: IncomingRequest) {
        try {
            Core.app.getSystemService(NotificationManager::class.java).cancel(PROMPT_BASE + req.id)
        } catch (_: Exception) {
        }
    }

    fun showDone(job: TransferJob) {
        val ctx = Core.app
        val peer = job.peerName
        val b = builder(ctx, CH_DONE)
            .setSmallIcon(R.drawable.ic_notify)
            .setContentTitle(
                if (job.kind == "text") (if (job.incoming) Loc.t("Texto copiado") else Loc.t("Texto enviado"))
                else if (job.incoming) Loc.t("Archivos recibidos") else Loc.t("Envío completado")
            )
            .setContentText(
                if (job.kind == "text" && job.incoming) Loc.f("de {0} · ya puedes pegarlo", peer)
                else if (job.incoming) Loc.f("{0} · de {1}", job.title, peer) else Loc.f("{0} · para {1}", job.title, peer)
            )
            .setContentIntent(openApp(ctx))
            .setAutoCancel(true)
            .setCategory(Notification.CATEGORY_MESSAGE)
        if (Build.VERSION.SDK_INT < 26) b.setPriority(Notification.PRIORITY_HIGH).setDefaults(Notification.DEFAULT_ALL)
        try {
            ctx.getSystemService(NotificationManager::class.java).notify(doneId.getAndIncrement(), b.build())
        } catch (_: Exception) {
        }
    }
}

/** Botones de las notificaciones (aceptar / rechazar / detener). */
class ActionReceiver : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent) {
        when (intent.action) {
            ACTION_ACCEPT, ACTION_DECLINE -> {
                val id = intent.getIntExtra("id", -1)
                Core.incoming.firstOrNull { it.id == id }?.answer?.complete(intent.action == ACTION_ACCEPT)
            }
            ACTION_STOP -> {
                Core.prefs.background = false
                if (!Core.uiVisible) Core.service?.stopSelf()
            }
        }
    }

    companion object {
        const val ACTION_ACCEPT = "com.kyaaadood.transferdood.ACCEPT"
        const val ACTION_DECLINE = "com.kyaaadood.transferdood.DECLINE"
        const val ACTION_STOP = "com.kyaaadood.transferdood.STOP"
    }
}
