package com.kyaaadood.transferdood

import android.content.Context
import android.os.Build
import org.json.JSONArray
import org.json.JSONObject
import java.util.UUID
import android.provider.Settings as AndroidSettings

class Prefs(ctx: Context) {
    private val sp = ctx.getSharedPreferences("transferdood", Context.MODE_PRIVATE)

    val deviceId: String = sp.getString("id", null) ?: UUID.randomUUID().toString().replace("-", "").also {
        sp.edit().putString("id", it).apply()
    }

    private val defaultName: String = try {
        val n = AndroidSettings.Global.getString(ctx.contentResolver, "device_name")
        if (n.isNullOrBlank()) Build.MODEL else n
    } catch (e: Exception) {
        Build.MODEL
    }.take(40)

    /** Nombre elegido por el usuario; vacío = usar el del dispositivo. */
    var deviceName: String
        get() = sp.getString("name", "") ?: ""
        set(v) = sp.edit().putString("name", v.trim().take(40)).apply()

    val effectiveName: String
        get() = deviceName.ifBlank { defaultName }

    val defaultDeviceName: String get() = defaultName

    /** Árbol SAF elegido para guardar; null = carpeta Descargas/TransferDood. */
    var receiveTree: String?
        get() = sp.getString("tree", null)
        set(v) = sp.edit().putString("tree", v).apply()

    /** system | light | dark */
    var language: String
        get() = sp.getString("lang", "auto") ?: "auto"
        set(v) = sp.edit().putString("lang", v).apply()

    var theme: String
        get() = sp.getString("theme", "system") ?: "system"
        set(v) = sp.edit().putString("theme", v).apply()

    var autoAccept: Boolean
        get() = sp.getBoolean("autoAccept", false)
        set(v) = sp.edit().putBoolean("autoAccept", v).apply()

    /** Mantener el servicio de recepción activo aunque la app esté cerrada. */
    var background: Boolean
        get() = sp.getBoolean("background", true)
        set(v) = sp.edit().putBoolean("background", v).apply()

    // ── Dispositivos emparejados (favoritos: reciben sin preguntar, con firma) ──

    private fun obj(key: String): JSONObject = try {
        JSONObject(sp.getString(key, "{}") ?: "{}")
    } catch (_: Exception) {
        JSONObject()
    }

    private fun favMap(): JSONObject = obj("favs2")

    /** Lista de (id, nombre) ordenada por nombre. */
    @Synchronized
    fun favorites(): List<Pair<String, String>> {
        val j = favMap()
        return j.keys().asSequence().map { it to (j.optJSONObject(it)?.optString("n", "") ?: "") }
            .sortedBy { it.second.lowercase() }.toList()
    }

    @Synchronized
    fun isFavorite(id: String): Boolean = id.isNotEmpty() && favMap().has(id)

    @Synchronized
    fun favoriteName(id: String): String? = favMap().optJSONObject(id)?.optString("n", "")

    /** Clave compartida obtenida al emparejar (null si no hay emparejamiento). */
    @Synchronized
    fun secret(id: String): ByteArray? {
        val s = favMap().optJSONObject(id)?.optString("s", "") ?: return null
        if (s.isEmpty()) return null
        val raw = KeyBox.unwrap(s) ?: return null
        if (!KeyBox.isWrapped(s)) {
            // Clave guardada en claro por una versión anterior: se protege con el Keystore de Android.
            try {
                val j = favMap()
                j.optJSONObject(id)?.put("s", KeyBox.wrap(raw))
                sp.edit().putString("favs2", j.toString()).apply()
            } catch (_: Exception) {
            }
        }
        return raw
    }

    @Synchronized
    fun pair(id: String, name: String, secret: ByteArray) {
        if (id.isEmpty()) return
        val j = favMap()
        j.put(id, JSONObject().put("n", name).put("s", KeyBox.wrap(secret)))
        val b = obj("blocked")
        b.remove(id)
        sp.edit().putString("favs2", j.toString()).putString("blocked", b.toString()).apply()
    }

    @Synchronized
    fun updateFavoriteName(id: String, name: String) {
        val j = favMap()
        val f = j.optJSONObject(id) ?: return
        if (f.optString("n", "") == name) return
        f.put("n", name)
        sp.edit().putString("favs2", j.toString()).apply()
    }

    @Synchronized
    fun removeFavorite(id: String) {
        val j = favMap()
        j.remove(id)
        sp.edit().putString("favs2", j.toString()).apply()
    }

    // ── Bloqueados ──

    @Synchronized
    fun isBlocked(id: String): Boolean = id.isNotEmpty() && obj("blocked").has(id)

    @Synchronized
    fun blocked(): List<Pair<String, String>> {
        val j = obj("blocked")
        return j.keys().asSequence().map { it to j.optString(it, "") }.sortedBy { it.second.lowercase() }.toList()
    }

    @Synchronized
    fun setBlocked(id: String, name: String, on: Boolean) {
        if (id.isEmpty()) return
        val b = obj("blocked")
        val f = favMap()
        if (on) {
            b.put(id, name)
            f.remove(id)
        } else {
            b.remove(id)
        }
        sp.edit().putString("blocked", b.toString()).putString("favs2", f.toString()).apply()
    }

    // ── IP añadidas a mano ──

    @Synchronized
    fun manualPeers(): List<String> {
        val a = try {
            JSONArray(sp.getString("manual", "[]") ?: "[]")
        } catch (_: Exception) {
            JSONArray()
        }
        return (0 until a.length()).map { a.optString(it) }.filter { it.isNotEmpty() }
    }

    @Synchronized
    fun addManual(ip: String) {
        val l = manualPeers().toMutableList()
        if (!l.contains(ip)) l.add(ip)
        sp.edit().putString("manual", JSONArray(l).toString()).apply()
    }

    @Synchronized
    fun clearManual() {
        sp.edit().putString("manual", "[]").apply()
    }

    // ── Historial de transferencias ──

    @Synchronized
    fun loadHistory(): List<JSONObject> {
        val a = try {
            JSONArray(sp.getString("history", "[]") ?: "[]")
        } catch (_: Exception) {
            JSONArray()
        }
        return (0 until a.length()).mapNotNull { a.optJSONObject(it) }
    }

    @Synchronized
    fun saveHistory(items: List<JSONObject>) {
        val a = JSONArray()
        items.takeLast(60).forEach { a.put(it) }
        sp.edit().putString("history", a.toString()).apply()
    }

    var permissionsAsked: Boolean
        get() = sp.getBoolean("permAsked", false)
        set(v) = sp.edit().putBoolean("permAsked", v).apply()
}
