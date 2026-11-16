package com.kyaaadood.transferdood

import android.content.Context
import android.net.ConnectivityManager
import android.net.wifi.WifiManager
import android.os.SystemClock
import org.json.JSONObject
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.Inet4Address
import java.net.InetAddress
import java.net.InetSocketAddress
import java.net.NetworkInterface
import java.net.SocketAddress
import java.net.SocketException
import kotlin.concurrent.thread

/**
 * Descubrimiento por broadcast UDP en la red local (mismo formato que la versión de Windows).
 * Cada equipo anuncia "hello" cada pocos segundos y responde a "query".
 */
class Discovery(private val ctx: Context, private val prefs: Prefs) {
    private val lock = Any()
    private val peers = HashMap<String, Peer>()

    @Volatile private var socket: DatagramSocket? = null
    @Volatile private var running = false
    private var mcast: WifiManager.MulticastLock? = null

    var onChanged: (() -> Unit)? = null

    // Ahorro de batería: con la app a la vista se anuncia cada 4 s; en segundo plano, cada 30 s
    // (el otro equipo nos recuerda más tiempo gracias al campo "ttl"). Al abrir la app se pregunta de inmediato.
    @Volatile private var intervalMs = 30_000L
    @Volatile private var ttlSec = 75
    private val wake = Object()

    fun setActive(active: Boolean) {
        val newInterval = if (active) 4_000L else 30_000L
        if (newInterval == intervalMs) return
        intervalMs = newInterval
        ttlSec = if (active) 15 else 75
        synchronized(wake) { wake.notifyAll() }
        if (active) refresh()
    }

    fun start() {
        if (running) return

        val s = DatagramSocket(null as SocketAddress?)
        s.reuseAddress = true
        s.broadcast = true
        s.bind(InetSocketAddress(Protocol.PORT))
        socket = s
        running = true

        // Sin este "lock" muchos teléfonos descartan los paquetes broadcast para ahorrar batería.
        try {
            val wm = ctx.applicationContext.getSystemService(Context.WIFI_SERVICE) as WifiManager
            mcast = wm.createMulticastLock("transferdood").apply {
                setReferenceCounted(false)
                acquire()
            }
        } catch (_: Exception) {
        }

        thread(name = "sd-udp-rx", isDaemon = true) { receiveLoop(s) }
        thread(name = "sd-udp-tx", isDaemon = true) { announceLoop() }
    }

    fun snapshot(): List<Peer> = synchronized(lock) {
        peers.values.map { it.copy() }.sortedBy { it.name.lowercase() }
    }

    /** Pregunta a todos quién está y se anuncia. */
    fun refresh() {
        thread(isDaemon = true) {
            send("query")
            send("hello")
            queryManual()
        }
    }

    /** Busca un equipo por IP (para redes donde el broadcast no llega). */
    fun addManual(ip: InetAddress) {
        prefs.addManual(ip.hostAddress ?: return)
        thread(isDaemon = true) { send("query", unicast = InetSocketAddress(ip, Protocol.PORT)) }
    }

    private fun queryManual() {
        for (t in prefs.manualPeers()) {
            try {
                send("query", unicast = InetSocketAddress(InetAddress.getByName(t), Protocol.PORT))
            } catch (_: Exception) {
            }
        }
    }

    fun announce() {
        thread(isDaemon = true) { send("hello") }
    }

    fun stop() {
        if (!running) return
        running = false
        val s = socket
        thread(isDaemon = true) {
            try {
                send("bye", s)
            } catch (_: Exception) {
            }
            try {
                s?.close()
            } catch (_: Exception) {
            }
        }
        try {
            mcast?.release()
        } catch (_: Exception) {
        }
        mcast = null
        synchronized(lock) { peers.clear() }
    }

    private fun announceLoop() {
        send("query")
        while (running) {
            try {
                send("hello")
                queryManual()
                expire()
                synchronized(wake) { wake.wait(intervalMs) }
            } catch (_: InterruptedException) {
                break
            } catch (_: Exception) {
            }
        }
    }

    private fun receiveLoop(s: DatagramSocket) {
        val buf = ByteArray(4096)
        val pkt = DatagramPacket(buf, buf.size)
        while (running) {
            try {
                pkt.length = buf.size
                s.receive(pkt)
                handle(String(pkt.data, 0, pkt.length, Charsets.UTF_8), pkt.address)
            } catch (_: SocketException) {
                if (!running) break
                try {
                    Thread.sleep(200)
                } catch (_: InterruptedException) {
                    break
                }
            } catch (_: Exception) {
            }
        }
    }

    private fun handle(text: String, addr: InetAddress) {
        val j = try {
            JSONObject(text)
        } catch (_: Exception) {
            return
        }
        if (j.optInt("v", 0) != Protocol.VERSION) return
        val id = j.optString("id", "")
        if (id.isEmpty() || id.length > 64 || id == prefs.deviceId) return
        val type = j.optString("t", "")

        if (type == "bye") {
            val removed = synchronized(lock) { peers.remove(id) != null }
            if (removed) onChanged?.invoke()
            return
        }
        if (type != "hello" && type != "query") return

        val rawPort = j.optInt("port", Protocol.PORT)
        val port = if (rawPort in 1..65535) rawPort else Protocol.PORT
        val name = clean(j.optString("name", ""), Loc.t("Dispositivo"))
        val os = clean(j.optString("os", ""), "")
        val ttlMs = j.optInt("ttl", 15).coerceIn(10, 180) * 1000L
        val now = SystemClock.elapsedRealtime()
        var changed = false
        prefs.updateFavoriteName(id, name)
        if (prefs.isBlocked(id)) return

        synchronized(lock) {
            val p = peers[id]
            if (p == null) {
                peers[id] = Peer(id, name, os, addr, port, now, ttlMs)
                changed = true
            } else {
                if (p.name != name) {
                    p.name = name
                    changed = true
                }
                if (p.port != port) {
                    p.port = port
                    changed = true
                }
                // Con varias tarjetas de red llegan paquetes desde varias IP: no saltar de una a otra.
                if (p.address != addr && now - p.lastSeen > 10_000) {
                    p.address = addr
                    changed = true
                }
                p.os = os
                p.ttlMs = ttlMs
                p.lastSeen = now
            }
        }

        if (changed) onChanged?.invoke()
        if (type == "query") send("hello", unicast = InetSocketAddress(addr, port))
    }

    private fun expire() {
        val now = SystemClock.elapsedRealtime()
        val changed = synchronized(lock) {
            val old = peers.filterValues { now - it.lastSeen > it.ttlMs }.keys.toList()
            old.forEach { peers.remove(it) }
            old.isNotEmpty()
        }
        if (changed) onChanged?.invoke()
    }

    private fun send(type: String, sock: DatagramSocket? = socket, unicast: InetSocketAddress? = null) {
        val s = sock ?: return
        try {
            val data = JSONObject()
                .put("v", Protocol.VERSION)
                .put("t", type)
                .put("id", prefs.deviceId)
                .put("name", prefs.effectiveName)
                .put("os", "android")
                .put("port", Protocol.PORT)
                .put("ttl", ttlSec)
                .toString()
                .toByteArray(Charsets.UTF_8)

            if (unicast != null) {
                s.send(DatagramPacket(data, data.size, unicast))
                return
            }
            for (target in broadcastTargets()) {
                try {
                    s.send(DatagramPacket(data, data.size, InetSocketAddress(target, Protocol.PORT)))
                } catch (_: Exception) {
                }
            }
        } catch (_: Exception) {
        }
    }

    private fun broadcastTargets(): Set<InetAddress> {
        val set = LinkedHashSet<InetAddress>()
        try {
            set.add(InetAddress.getByName("255.255.255.255"))
        } catch (_: Exception) {
        }

        // 1) Redes activas según Android (funciona aunque getNetworkInterfaces esté limitado).
        try {
            val cm = ctx.getSystemService(Context.CONNECTIVITY_SERVICE) as ConnectivityManager
            for (net in cm.allNetworks) {
                val lp = cm.getLinkProperties(net) ?: continue
                for (la in lp.linkAddresses) {
                    val a = la.address
                    if (a !is Inet4Address || a.isLoopbackAddress) continue
                    val prefix = la.prefixLength
                    if (prefix !in 8..30) continue
                    val ip = a.address
                    val mask = (-1 shl (32 - prefix))
                    val b = ByteArray(4)
                    for (i in 0 until 4) {
                        val m = (mask ushr (24 - 8 * i)) and 0xFF
                        b[i] = (ip[i].toInt() or m.inv()).toByte()
                    }
                    set.add(InetAddress.getByAddress(b))
                }
            }
        } catch (_: Exception) {
        }

        // 2) Interfaces de red (incluye el hotspot del propio teléfono).
        try {
            val nis = NetworkInterface.getNetworkInterfaces()
            while (nis != null && nis.hasMoreElements()) {
                val ni = nis.nextElement()
                if (!ni.isUp || ni.isLoopback) continue
                for (ia in ni.interfaceAddresses) ia.broadcast?.let { set.add(it) }
            }
        } catch (_: Exception) {
        }
        return set
    }

    private fun clean(s: String, fallback: String): String {
        val t = s.filter { !it.isISOControl() }.trim().take(48)
        return t.ifEmpty { fallback }
    }
}
