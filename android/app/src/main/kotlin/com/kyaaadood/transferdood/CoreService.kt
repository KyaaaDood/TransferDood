package com.kyaaadood.transferdood

import android.app.Service
import android.content.Intent
import android.content.pm.ServiceInfo
import android.os.Build
import android.os.IBinder
import kotlin.concurrent.thread

/**
 * Servicio en primer plano que escucha (UDP + TCP) para poder recibir archivos.
 * Se enciende al abrir la app y se apaga solo al cerrarla, salvo que el usuario active
 * "Mantener disponible en segundo plano".
 */
class CoreService : Service() {
    private var disc: Discovery? = null
    private var server: TransferServer? = null
    @Volatile private var starting = false
    @Volatile private var destroyed = false

    override fun onBind(intent: Intent?): IBinder? = null

    override fun onCreate() {
        super.onCreate()
        Core.service = this
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        val n = Notifier.foreground(this)
        if (Build.VERSION.SDK_INT >= 29) {
            startForeground(Notifier.FG_ID, n, ServiceInfo.FOREGROUND_SERVICE_TYPE_CONNECTED_DEVICE)
        } else {
            startForeground(Notifier.FG_ID, n)
        }
        if (disc == null && server == null && !starting) {
            starting = true
            thread(name = "sd-start", isDaemon = true) {
                startNetwork()
                starting = false
            }
        }
        return START_NOT_STICKY
    }

    private fun startNetwork() {
        Core.networkError = null

        val d = Discovery(applicationContext, Core.prefs)
        d.onChanged = { Core.notifyChanged() }
        try {
            d.start()
            disc = d
            Core.discovery = d
        } catch (e: Exception) {
            Core.networkError = Loc.f("No se pudo abrir el puerto UDP {0}: {1}", Protocol.PORT, e.message)
        }

        val s = TransferServer()
        try {
            s.start()
            server = s
        } catch (e: Exception) {
            Core.networkError = Loc.f("No se pudo abrir el puerto TCP {0}: {1}", Protocol.PORT, e.message)
        }
        if (destroyed) {
            disc?.stop()
            server?.stop()
            return
        }
        Core.notifyChanged()
    }

    override fun onDestroy() {
        destroyed = true
        for (j in Core.jobs) if (!j.isFinished) j.cancel()
        disc?.stop()
        server?.stop()
        disc = null
        server = null
        Core.discovery = null
        if (Core.service === this) Core.service = null
        Core.notifyChanged()
        super.onDestroy()
    }
}
