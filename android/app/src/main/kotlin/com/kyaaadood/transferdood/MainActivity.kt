package com.kyaaadood.transferdood

import android.Manifest
import android.app.Activity
import android.app.DownloadManager
import android.content.Intent
import android.content.pm.PackageManager
import android.graphics.Color
import android.net.Uri
import android.os.Build
import android.os.Bundle
import android.provider.Settings
import android.util.TypedValue
import android.view.Gravity
import android.view.View
import android.view.ViewGroup
import android.widget.LinearLayout
import android.widget.ScrollView
import android.widget.TextView
import android.widget.Toast

class MainActivity : Activity() {
    private lateinit var pal: Pal
    private var darkApplied = false
    private var builtLang = ""
    private var shown = false

    private lateinit var subtitle: TextView
    private lateinit var errorText: TextView
    private lateinit var pendingBar: PendingBarView
    private lateinit var notifBar: PendingBarView
    private lateinit var devices: LinearLayout
    private lateinit var transfers: LinearLayout

    private var pickTarget: Peer? = null

    private val listener = Runnable { refresh() }
    private val ticker = object : Runnable {
        override fun run() {
            tick()
            Core.main.postDelayed(this, 300)
        }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        pal = Theme.pal(this)
        darkApplied = Theme.isDark(this)
        builtLang = Loc.setting
        Theme.apply(this)
        buildUi()
        handleIntent(intent)
        askPermissions()
    }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        setIntent(intent)
        handleIntent(intent)
    }

    override fun onStart() {
        super.onStart()
        if (Theme.isDark(this) != darkApplied || builtLang != Loc.setting) {
            recreate()
            return
        }
        shown = true
        Core.onUiShown()
        Core.addListener(listener)
        Core.onMainShown()
        Core.startService(this)
        Core.discovery?.refresh()
        refresh()
        Core.main.postDelayed(ticker, 300)
    }

    override fun onStop() {
        if (shown) {
            shown = false
            Core.main.removeCallbacks(ticker)
            Core.removeListener(listener)
            for (r in Core.incoming) r.dialog?.dismiss()
            Core.onMainHidden()
            Core.onUiHidden()
        }
        super.onStop()
    }

    // ── Interfaz ──

    private fun lp(w: Int, h: Int) = LinearLayout.LayoutParams(w, h)

    private fun label(text: String, sp: Float, color: Int, bold: Boolean = false) = TextView(this).apply {
        this.text = text
        setTextSize(TypedValue.COMPLEX_UNIT_SP, sp)
        setTextColor(color)
        if (bold) typeface = Ui.medium
    }

    private fun sectionRow(title: String, actionText: String, action: () -> Unit): LinearLayout {
        val row = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
        }
        row.addView(label(title, 16f, pal.text, true), LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f))
        row.addView(Ui.button(this, actionText, Ui.GHOST, action).apply { minHeight = dp(36) })
        return row
    }

    private fun buildUi() {
        val scroll = ScrollView(this).apply {
            isFillViewport = true
            fitsSystemWindows = true
        }
        val col = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(dp(20), dp(12), dp(20), dp(24))
        }
        scroll.addView(col, ViewGroup.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT))

        // Encabezado
        val header = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
        }
        val titles = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL }
        titles.addView(label("TransferDood", 28f, pal.text, true))
        subtitle = label("", 14f, pal.sub)
        titles.addView(subtitle)
        header.addView(titles, LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f))
        header.addView(Ui.button(this, Loc.t("Ajustes"), Ui.SECONDARY) {
            startActivity(Intent(this, SettingsActivity::class.java))
        })
        col.addView(header, lp(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT))

        errorText = label("", 13f, pal.danger).apply {
            visibility = View.GONE
            setPadding(0, dp(8), 0, 0)
        }
        col.addView(errorText)

        notifBar = PendingBarView(this, Loc.t("Activar")) { enableNotifications() }
        notifBar.info = Loc.t("Activa las notificaciones para enterarte de los envíos")
        notifBar.visibility = View.GONE
        col.addView(notifBar, lp(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { topMargin = dp(12) })

        // Botones de envío
        val buttons = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL }
        buttons.addView(
            Ui.button(this, Loc.t("Enviar archivos"), Ui.PRIMARY) { pickFiles(null) },
            LinearLayout.LayoutParams(0, dp(48), 1f).apply { rightMargin = dp(6) }
        )
        buttons.addView(
            Ui.button(this, Loc.t("Enviar carpeta"), Ui.SECONDARY) { pickFolder() },
            LinearLayout.LayoutParams(0, dp(48), 1f).apply { leftMargin = dp(6) }
        )
        col.addView(buttons, lp(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { topMargin = dp(20) })

        val buttons2 = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL }
        buttons2.addView(
            Ui.button(this, Loc.t("Enviar texto"), Ui.SECONDARY) { pickText() },
            LinearLayout.LayoutParams(0, dp(48), 1f).apply { rightMargin = dp(6) }
        )
        buttons2.addView(
            Ui.button(this, Loc.t("Conectar por IP"), Ui.SECONDARY) { connectByIp() },
            LinearLayout.LayoutParams(0, dp(48), 1f).apply { leftMargin = dp(6) }
        )
        col.addView(buttons2, lp(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { topMargin = dp(10) })

        pendingBar = PendingBarView(this, Loc.t("Quitar")) { Core.clearPending() }
        pendingBar.visibility = View.GONE
        col.addView(pendingBar, lp(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { topMargin = dp(14) })

        col.addView(
            Ui.button(this, Loc.t("Abrir carpeta de recibidos"), Ui.GHOST) { openReceivedFolder() },
            lp(ViewGroup.LayoutParams.MATCH_PARENT, dp(48)).apply { topMargin = dp(6) }
        )

        // Dispositivos
        col.addView(
            sectionRow(Loc.t("Dispositivos cercanos"), Loc.t("Buscar")) { Core.discovery?.refresh() },
            lp(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { topMargin = dp(18) }
        )
        devices = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL }
        col.addView(devices, lp(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { topMargin = dp(6) })

        // Transferencias
        col.addView(
            sectionRow(Loc.t("Transferencias"), Loc.t("Limpiar")) { Core.clearFinished() },
            lp(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { topMargin = dp(10) }
        )
        transfers = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL }
        col.addView(transfers, lp(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { topMargin = dp(6) })

        setContentView(scroll)
    }

    private fun emptyText(text: String) = label(text, 14f, pal.sub).apply {
        gravity = Gravity.CENTER
        setPadding(dp(8), dp(24), dp(8), dp(24))
    }

    private fun refresh() {
        subtitle.text = Loc.f("Visible como “{0}”", Core.prefs.effectiveName)

        val err = Core.networkError
        errorText.text = err ?: ""
        errorText.visibility = if (err == null) View.GONE else View.VISIBLE

        notifBar.visibility = if (Core.notificationsEnabled(this)) View.GONE else View.VISIBLE

        rebuildDevices()
        rebuildTransfers()
        updatePending()
        showIncoming()
    }

    private fun rebuildDevices() {
        devices.removeAllViews()
        val peers = (Core.discovery?.snapshot() ?: emptyList()).filter { !Core.prefs.isBlocked(it.id) }
        if (peers.isEmpty()) {
            devices.addView(emptyText(Loc.t("Buscando dispositivos en tu red…\nAbre TransferDood en el otro equipo y conéctalo a la misma red Wi‑Fi.")))
            return
        }
        for (pair in peers.chunked(2)) {
            val row = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL }
            pair.forEachIndexed { i, peer ->
                val card = DeviceCardView(this, peer, Core.prefs.isFavorite(peer.id)) { toggleFavorite(peer) }
                card.setOnClickListener { onDeviceTap(peer) }
                card.setOnLongClickListener {
                    deviceMenu(peer)
                    true
                }
                row.addView(card, LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f).apply {
                    if (i == 0) rightMargin = dp(6) else leftMargin = dp(6)
                    bottomMargin = dp(12)
                })
            }
            if (pair.size == 1) {
                row.addView(View(this), LinearLayout.LayoutParams(0, 1, 1f).apply { leftMargin = dp(6) })
            }
            devices.addView(row, lp(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT))
        }
    }

    private fun rebuildTransfers() {
        transfers.removeAllViews()
        val list = Core.jobs.toList().asReversed()
        if (list.isEmpty()) {
            transfers.addView(emptyText(Loc.t("Aquí verás lo que envíes y recibas.")))
            return
        }
        for (job in list) {
            val row = TransferRowView(this, job) { j, action -> onRowAction(j, action) }
            transfers.addView(row, lp(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply {
                bottomMargin = dp(8)
            })
        }
    }

    private fun updatePending() {
        val items = Core.pending.toList()
        val text = Core.pendingText
        if (text != null) {
            pendingBar.info = Loc.f("Texto listo: {0} · toca un dispositivo", TransferClient.preview(text))
            pendingBar.visibility = View.VISIBLE
            return
        }
        if (items.isEmpty()) {
            pendingBar.visibility = View.GONE
            return
        }
        val what = if (items.size == 1) {
            val it = items[0]
            if (it.isTree) Loc.t("una carpeta") else FilesUtil.describe(this, it.uri).first
        } else {
            Loc.f("{0} elementos", items.size)
        }
        pendingBar.info = Loc.f("Listo para enviar: {0} · toca un dispositivo", what)
        pendingBar.visibility = View.VISIBLE
    }

    private fun tick() {
        for (i in 0 until transfers.childCount) {
            val v = transfers.getChildAt(i)
            if (v is TransferRowView && !v.job.isFinished) v.invalidate()
        }
    }

    // ── Acciones ──

    /** Estrella: si ya está emparejado lo quita; si no, inicia el emparejamiento (código en ambos equipos). */
    private fun toggleFavorite(peer: Peer) {
        if (Core.prefs.isFavorite(peer.id)) {
            Core.prefs.removeFavorite(peer.id)
            rebuildDevices()
        } else {
            Toast.makeText(this, Loc.t("Confirma el código en los dos equipos…"), Toast.LENGTH_SHORT).show()
            PairClient.start(peer)
        }
    }

    private fun deviceMenu(peer: Peer) {
        val fav = Core.prefs.isFavorite(peer.id)
        val items = listOf<Pair<String, () -> Unit>>(
            (if (fav) Loc.t("Quitar de favoritos") else Loc.t("Emparejar como favorito")) to { toggleFavorite(peer) },
            Loc.t("Bloquear este equipo") to {
                Core.prefs.setBlocked(peer.id, peer.name, true)
                rebuildDevices()
            }
        )
        Sheets.actions(this, peer.name, items).show()
    }

    private fun pickText() {
        var initial = ""
        try {
            val cm = getSystemService(CLIPBOARD_SERVICE) as android.content.ClipboardManager
            initial = cm.primaryClip?.getItemAt(0)?.coerceToText(this)?.toString() ?: ""
        } catch (_: Exception) {
        }
        Sheets.textInput(this, initial) { Core.queueText(it) }.show()
    }

    private fun localIps(): String {
        val l = ArrayList<String>()
        try {
            for (ni in java.net.NetworkInterface.getNetworkInterfaces()) {
                if (!ni.isUp || ni.isLoopback) continue
                for (a in ni.inetAddresses) if (a is java.net.Inet4Address) l.add(a.hostAddress ?: continue)
            }
        } catch (_: Exception) {
        }
        return if (l.isEmpty()) "—" else l.joinToString(", ")
    }

    private fun connectByIp() {
        val saved = Core.prefs.manualPeers()
        Sheets.ipInput(
            this, localIps(),
            if (saved.isEmpty()) Loc.t("Sin IP guardadas.") else Loc.t("Guardadas (se buscan siempre): ") + saved.joinToString(", "),
            { text ->
                val ip = try {
                    if (Regex("""\d{1,3}(\.\d{1,3}){3}""").matches(text)) java.net.InetAddress.getByName(text) else null
                } catch (_: Exception) {
                    null
                }
                if (ip == null) {
                    Toast.makeText(this, Loc.t("Escribe una IP válida, por ejemplo 192.168.1.25."), Toast.LENGTH_LONG).show()
                } else {
                    Core.discovery?.addManual(ip)
                    Toast.makeText(this, Loc.f("Buscando {0}… si responde aparecerá en la lista.", text), Toast.LENGTH_LONG).show()
                }
            },
            { Core.prefs.clearManual() }
        ).show()
    }

    private fun onDeviceTap(peer: Peer) {
        if (Core.hasPending) Core.sendPending(peer) else pickFiles(peer)
    }

    private fun onRowAction(job: TransferJob, action: String) {
        when (action) {
            Loc.t("Cancelar") -> job.cancel()
            Loc.t("Abrir"), Loc.t("Instalar") -> openReceived(job)
            Loc.t("Ver") -> job.openUri?.let {
                startActivity(Intent(this, PreviewActivity::class.java).putExtra("uri", it.toString()).putExtra("name", job.title))
            } ?: openReceived(job)
            Loc.t("Compartir") -> job.openUri?.let { Viewer.share(this, it, job.title) }
            Loc.t("Pausar"), Loc.t("Reanudar") -> job.togglePause()
            Loc.t("Copiar") -> try {
                val cm = getSystemService(CLIPBOARD_SERVICE) as android.content.ClipboardManager
                cm.setPrimaryClip(android.content.ClipData.newPlainText("TransferDood", job.text ?: ""))
                Toast.makeText(this, Loc.t("Texto copiado"), Toast.LENGTH_SHORT).show()
            } catch (_: Exception) {
            }
        }
    }

    private fun openReceived(job: TransferJob) {
        val uri = job.openUri
        if (uri != null && job.title.lowercase().endsWith(".apk")) {
            installApk(uri)
            return
        }
        if (uri != null) {
            try {
                startActivity(
                    Intent(Intent.ACTION_VIEW)
                        .setDataAndType(uri, Storage.mimeOf(job.title))
                        .addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
                )
                return
            } catch (_: Exception) {
            }
        }
        openReceivedFolder()
    }

    /** Abre el instalador de paquetes. Si falta el permiso de "instalar apps desconocidas", lleva a ese ajuste. */
    private fun installApk(uri: Uri) {
        if (Build.VERSION.SDK_INT >= 26 && !packageManager.canRequestPackageInstalls()) {
            Toast.makeText(this, Loc.t("Permite instalar apps desde TransferDood y vuelve a tocar «Instalar»."), Toast.LENGTH_LONG).show()
            try {
                startActivity(Intent(Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES, Uri.parse("package:$packageName")))
            } catch (_: Exception) {
                try {
                    startActivity(Intent(Settings.ACTION_SECURITY_SETTINGS))
                } catch (_: Exception) {
                }
            }
            return
        }
        // Android 7-9 con archivo normal: se permite compartir el archivo con el instalador.
        if (uri.scheme == "file" && Build.VERSION.SDK_INT in 24..28) {
            try {
                android.os.StrictMode.setVmPolicy(android.os.StrictMode.VmPolicy.Builder().build())
            } catch (_: Exception) {
            }
        }
        val mime = "application/vnd.android.package-archive"
        val attempts = listOf(
            Intent(Intent.ACTION_VIEW).setDataAndType(uri, mime),
            @Suppress("DEPRECATION")
            Intent(Intent.ACTION_INSTALL_PACKAGE).setDataAndType(uri, mime)
        )
        for (i in attempts) {
            try {
                startActivity(i.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION or Intent.FLAG_ACTIVITY_NEW_TASK))
                return
            } catch (_: Exception) {
            }
        }
        Toast.makeText(this, Loc.t("No se encontró una app para abrir este archivo."), Toast.LENGTH_LONG).show()
        openReceivedFolder()
    }

    /** Abre la carpeta de recibidos en el explorador de archivos (lo más compatible posible). */
    private fun openReceivedFolder() {
        val tries = ArrayList<Intent>()
        val tree = Core.prefs.receiveTree
        try {
            if (tree != null) {
                val t = Uri.parse(tree)
                val doc = android.provider.DocumentsContract.buildDocumentUriUsingTree(t, android.provider.DocumentsContract.getTreeDocumentId(t))
                tries.add(
                    Intent(Intent.ACTION_VIEW)
                        .setDataAndType(doc, android.provider.DocumentsContract.Document.MIME_TYPE_DIR)
                        .addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
                )
            } else {
                val u = Uri.parse("content://com.android.externalstorage.documents/document/primary%3ADownload%2FTransferDood")
                tries.add(Intent(Intent.ACTION_VIEW).setDataAndType(u, "vnd.android.document/directory"))
                tries.add(Intent(Intent.ACTION_VIEW).setDataAndType(u, "resource/folder"))
            }
        } catch (_: Exception) {
        }
        tries.add(Intent(DownloadManager.ACTION_VIEW_DOWNLOADS))
        for (i in tries) {
            try {
                startActivity(i)
                return
            } catch (_: Exception) {
            }
        }
        Toast.makeText(this, Loc.t("No se pudo abrir la carpeta."), Toast.LENGTH_LONG).show()
    }

    private fun pickFiles(target: Peer?) {
        pickTarget = target
        val i = Intent(Intent.ACTION_OPEN_DOCUMENT)
            .addCategory(Intent.CATEGORY_OPENABLE)
            .setType("*/*")
            .putExtra(Intent.EXTRA_ALLOW_MULTIPLE, true)
        try {
            startActivityForResult(i, if (target != null) REQ_PICK_SEND else REQ_PICK)
        } catch (_: Exception) {
        }
    }

    private fun pickFolder() {
        try {
            startActivityForResult(Intent(Intent.ACTION_OPEN_DOCUMENT_TREE), REQ_TREE)
        } catch (_: Exception) {
        }
    }

    @Deprecated("Deprecated in Java")
    override fun onActivityResult(requestCode: Int, resultCode: Int, data: Intent?) {
        super.onActivityResult(requestCode, resultCode, data)
        if (resultCode != RESULT_OK || data == null) return
        when (requestCode) {
            REQ_PICK -> Core.queue(urisFrom(data), false)
            REQ_PICK_SEND -> {
                val peer = pickTarget
                val uris = urisFrom(data)
                if (peer != null && uris.isNotEmpty()) TransferClient.start(peer, uris.map { PendingItem(it, false) })
            }
            REQ_TREE -> data.data?.let { Core.queue(listOf(it), true) }
        }
    }

    private fun urisFrom(data: Intent): List<Uri> {
        val list = ArrayList<Uri>()
        val cd = data.clipData
        if (cd != null) {
            for (i in 0 until cd.itemCount) cd.getItemAt(i).uri?.let { list.add(it) }
        }
        if (list.isEmpty()) data.data?.let { list.add(it) }
        return list
    }

    /** Archivos que llegan desde el menú "Compartir" de otras apps. */
    @Suppress("DEPRECATION")
    private fun handleIntent(i: Intent?) {
        if (i == null) return
        if (i.action != Intent.ACTION_SEND && i.action != Intent.ACTION_SEND_MULTIPLE) return

        var uris = urisFrom(i)
        if (uris.isEmpty()) {
            val extra = ArrayList<Uri>()
            if (i.action == Intent.ACTION_SEND_MULTIPLE) {
                i.getParcelableArrayListExtra<Uri>(Intent.EXTRA_STREAM)?.let { extra.addAll(it) }
            } else {
                i.getParcelableExtra<Uri>(Intent.EXTRA_STREAM)?.let { extra.add(it) }
            }
            uris = extra
        }
        if (uris.isEmpty()) {
            val text = i.getCharSequenceExtra(Intent.EXTRA_TEXT)?.toString()
            if (!text.isNullOrBlank()) {
                Core.queueText(text)
                setIntent(Intent())
                return
            }
        }
        if (uris.isNotEmpty()) Core.queue(uris, false)
        setIntent(Intent())
    }

    // ── Avisos de envíos entrantes (cuando la app está abierta) ──

    private fun showIncoming() {
        for (req in Core.incoming) {
            if (req.dialog != null || req.answer.isDone) continue
            val dlg = Sheets.incoming(this, req)
            req.dialog = dlg
            dlg.setOnDismissListener { req.dialog = null }
            try {
                dlg.show()
            } catch (_: Exception) {
                req.dialog = null
            }
        }
    }

    // ── Permisos ──

    private fun askPermissions() {
        val need = ArrayList<String>()
        if (Build.VERSION.SDK_INT >= 33 && !Core.prefs.permissionsAsked &&
            checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED
        ) {
            need.add(Manifest.permission.POST_NOTIFICATIONS)
        }
        if (Build.VERSION.SDK_INT <= 28 && Core.prefs.receiveTree == null &&
            checkSelfPermission(Manifest.permission.WRITE_EXTERNAL_STORAGE) != PackageManager.PERMISSION_GRANTED
        ) {
            need.add(Manifest.permission.WRITE_EXTERNAL_STORAGE)
        }
        Core.prefs.permissionsAsked = true
        if (need.isNotEmpty()) requestPermissions(need.toTypedArray(), 1)
    }

    private fun enableNotifications() {
        if (Build.VERSION.SDK_INT >= 33 &&
            checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED
        ) {
            requestPermissions(arrayOf(Manifest.permission.POST_NOTIFICATIONS), REQ_NOTIF)
        } else {
            openNotificationSettings()
        }
    }

    private fun openNotificationSettings() {
        try {
            val i = if (Build.VERSION.SDK_INT >= 26) {
                Intent(Settings.ACTION_APP_NOTIFICATION_SETTINGS).putExtra(Settings.EXTRA_APP_PACKAGE, packageName)
            } else {
                Intent(Settings.ACTION_APPLICATION_DETAILS_SETTINGS, Uri.parse("package:$packageName"))
            }
            startActivity(i)
        } catch (_: Exception) {
        }
    }

    override fun onRequestPermissionsResult(requestCode: Int, permissions: Array<out String>, grantResults: IntArray) {
        super.onRequestPermissionsResult(requestCode, permissions, grantResults)
        if (requestCode == REQ_NOTIF && grantResults.firstOrNull() != PackageManager.PERMISSION_GRANTED) {
            // Si el sistema ya no muestra el aviso, se llega a los ajustes de la app.
            openNotificationSettings()
        }
        refresh()
    }

    companion object {
        private const val REQ_NOTIF = 2
        private const val REQ_PICK = 1
        private const val REQ_PICK_SEND = 2
        private const val REQ_TREE = 3
    }
}
