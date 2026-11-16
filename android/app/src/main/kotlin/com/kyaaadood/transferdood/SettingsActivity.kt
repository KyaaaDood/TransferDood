package com.kyaaadood.transferdood

import android.app.Activity
import android.content.Intent
import android.content.res.ColorStateList
import android.graphics.drawable.GradientDrawable
import android.net.Uri
import android.os.Build
import android.os.Bundle
import android.provider.Settings
import android.text.InputType
import android.util.TypedValue
import android.view.Gravity
import android.view.ViewGroup
import android.widget.EditText
import android.widget.LinearLayout
import android.widget.RadioButton
import android.widget.RadioGroup
import android.widget.ScrollView
import android.widget.Switch
import android.widget.TextView

class SettingsActivity : Activity() {
    private lateinit var pal: Pal
    private lateinit var nameEdit: EditText
    private lateinit var folderText: TextView
    private lateinit var favBox: LinearLayout
    private lateinit var blockedBox: LinearLayout
    private var shown = false
    private var darkApplied = false

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        pal = Theme.pal(this)
        darkApplied = Theme.isDark(this)
        Theme.apply(this)
        title = Loc.t("Ajustes")
        buildUi()
    }

    override fun onStart() {
        super.onStart()
        shown = true
        Core.onUiShown()
    }

    override fun onPause() {
        // El nombre se guarda al salir de la pantalla.
        val newName = nameEdit.text.toString().trim()
        if (newName != Core.prefs.deviceName) {
            Core.prefs.deviceName = newName
            Core.discovery?.announce()
            Core.notifyChanged()
        }
        super.onPause()
    }

    override fun onStop() {
        if (shown) {
            shown = false
            Core.onUiHidden()
        }
        super.onStop()
    }

    private fun text(s: String, sp: Float, color: Int, bold: Boolean = false) = TextView(this).apply {
        text = s
        setTextSize(TypedValue.COMPLEX_UNIT_SP, sp)
        setTextColor(color)
        if (bold) typeface = Ui.medium
    }

    private fun lp(top: Int = 0) = LinearLayout.LayoutParams(
        ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT
    ).apply { topMargin = dp(top) }

    private fun section(col: LinearLayout, title: String, top: Int) {
        col.addView(text(title, 15f, pal.text, true), lp(top))
    }

    private fun hint(col: LinearLayout, s: String, top: Int = 4) {
        col.addView(text(s, 12f, pal.sub), lp(top))
    }

    private fun tinted(sw: Switch): Switch {
        val states = arrayOf(intArrayOf(android.R.attr.state_checked), intArrayOf())
        sw.thumbTintList = ColorStateList(states, intArrayOf(pal.accent, pal.sub))
        sw.trackTintList = ColorStateList(states, intArrayOf(Ui.alpha(pal.accent, 110), pal.surfaceAlt))
        sw.setTextColor(pal.text)
        sw.setTextSize(TypedValue.COMPLEX_UNIT_SP, 14f)
        return sw
    }

    private fun buildUi() {
        val scroll = ScrollView(this).apply { fitsSystemWindows = true }
        val col = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(dp(20), dp(12), dp(20), dp(28))
        }
        scroll.addView(col, ViewGroup.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT))

        // Encabezado con botón de volver
        val header = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
        }
        header.addView(Ui.button(this, "←", Ui.GHOST) { finish() }.apply {
            minWidth = dp(44)
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 20f)
        })
        header.addView(text(Loc.t("Ajustes"), 22f, pal.text, true))
        col.addView(header, lp())

        // Nombre
        section(col, Loc.t("Nombre de este dispositivo"), 18)
        nameEdit = EditText(this).apply {
            inputType = InputType.TYPE_CLASS_TEXT
            setSingleLine(true)
            hint = Core.prefs.defaultDeviceName
            setText(Core.prefs.deviceName)
            setTextColor(pal.text)
            setHintTextColor(pal.sub)
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 15f)
            setPadding(dp(12), dp(10), dp(12), dp(10))
            background = GradientDrawable().apply {
                cornerRadius = dpf(10f)
                setColor(pal.surface)
                setStroke(dp(1), pal.border)
            }
            filters = arrayOf(android.text.InputFilter.LengthFilter(40))
        }
        col.addView(nameEdit, lp(8))
        hint(col, Loc.f("Vacío = usar el nombre del teléfono ({0}).", Core.prefs.defaultDeviceName))

        // Carpeta de recibidos
        section(col, Loc.t("Carpeta para archivos recibidos"), 22)
        folderText = text(Storage.describeFolder(), 14f, pal.sub)
        col.addView(folderText, lp(6))
        val folderRow = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL }
        folderRow.addView(Ui.button(this, Loc.t("Cambiar…"), Ui.SECONDARY) { chooseFolder() })
        folderRow.addView(
            Ui.button(this, Loc.t("Predeterminada"), Ui.GHOST) {
                releaseOldTree()
                Core.prefs.receiveTree = null
                folderText.text = Storage.describeFolder()
            },
            LinearLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { leftMargin = dp(8) }
        )
        col.addView(folderRow, lp(8))

        // Tema
        section(col, Loc.t("Tema"), 22)
        val group = RadioGroup(this).apply { orientation = RadioGroup.HORIZONTAL }
        val options = listOf("system" to Loc.t("Automático"), "light" to Loc.t("Claro"), "dark" to Loc.t("Oscuro"))
        val ids = HashMap<String, Int>()
        for ((key, label) in options) {
            val rb = RadioButton(this).apply {
                id = android.view.View.generateViewId()
                text = label
                setTextColor(pal.text)
                setTextSize(TypedValue.COMPLEX_UNIT_SP, 14f)
                buttonTintList = ColorStateList.valueOf(pal.accent)
                setPadding(dp(4), dp(8), dp(16), dp(8))
            }
            ids[key] = rb.id
            group.addView(rb)
        }
        group.check(ids[Core.prefs.theme] ?: ids.getValue("system"))
        group.setOnCheckedChangeListener { _, checkedId ->
            val key = ids.entries.firstOrNull { it.value == checkedId }?.key ?: "system"
            if (key != Core.prefs.theme) {
                Core.prefs.theme = key
                recreate()
            }
        }
        col.addView(group, lp(4))

        // Idioma
        section(col, Loc.t("Idioma"), 22)
        val langGroup = RadioGroup(this).apply { orientation = RadioGroup.HORIZONTAL }
        val langOptions = listOf("auto" to Loc.t("Automático"), "es" to "Español", "en" to "English")
        val langIds = HashMap<String, Int>()
        for ((key, label) in langOptions) {
            val rb = RadioButton(this).apply {
                id = android.view.View.generateViewId()
                text = label
                setTextColor(pal.text)
                setTextSize(TypedValue.COMPLEX_UNIT_SP, 14f)
                buttonTintList = ColorStateList.valueOf(pal.accent)
                setPadding(dp(4), dp(8), dp(16), dp(8))
            }
            langIds[key] = rb.id
            langGroup.addView(rb)
        }
        langGroup.check(langIds[Core.prefs.language] ?: langIds.getValue("auto"))
        langGroup.setOnCheckedChangeListener { _, checkedId ->
            val key = langIds.entries.firstOrNull { it.value == checkedId }?.key ?: "auto"
            if (key != Core.prefs.language) {
                Core.prefs.language = key
                Loc.setting = key
                recreate()
            }
        }
        col.addView(langGroup, lp(4))

        // Comportamiento
        section(col, Loc.t("Comportamiento"), 20)

        val bg = tinted(Switch(this)).apply {
            text = Loc.t("Mantener disponible en segundo plano")
            isChecked = Core.prefs.background
            setPadding(0, dp(10), 0, dp(10))
            setOnCheckedChangeListener { _, checked -> Core.prefs.background = checked }
        }
        col.addView(bg, lp(4))
        hint(col, Loc.t("Así otros equipos pueden enviarte archivos con la app cerrada. Deja una notificación fija y gasta un poco más de batería."), 0)

        var guard = false
        val auto = tinted(Switch(this)).apply {
            text = Loc.t("Aceptar archivos sin preguntar")
            isChecked = Core.prefs.autoAccept
            setPadding(0, dp(10), 0, dp(10))
        }
        auto.setOnCheckedChangeListener { btn, checked ->
            if (guard) return@setOnCheckedChangeListener
            if (checked) {
                Sheets.confirm(
                    this,
                    Loc.t("¿Aceptar sin preguntar?"),
                    Loc.t("Cualquier equipo de tu red podrá guardar archivos en tu teléfono sin que lo apruebes. Es más seguro marcar como favoritos solo a los equipos de confianza."),
                    Loc.t("Activar"), Loc.t("Cancelar"),
                    onPositive = { Core.prefs.autoAccept = true },
                    onNegative = {
                        guard = true
                        btn.isChecked = false
                        guard = false
                    }
                ).show()
            } else {
                Core.prefs.autoAccept = false
            }
        }
        col.addView(auto, lp(10))

        // Favoritos
        section(col, Loc.t("Dispositivos favoritos"), 24)
        hint(col, Loc.t("Emparejados con código: sus envíos llegan sin preguntar. Toca la estrella de un dispositivo para emparejarlo."), 4)
        favBox = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL }
        col.addView(favBox, lp(6))
        section(col, Loc.t("Dispositivos bloqueados"), 18)
        hint(col, Loc.t("No pueden enviarte nada y no aparecen en la lista. Mantén pulsada una tarjeta para bloquear."), 4)
        blockedBox = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL }
        col.addView(blockedBox, lp(6))
        rebuildFavorites()

        // Notificaciones
        section(col, Loc.t("Notificaciones"), 22)
        hint(col, Loc.t("Para ver los avisos de envío entrante y cuando termina una transferencia."), 4)
        col.addView(
            Ui.button(this, Loc.t("Ajustes de notificaciones"), Ui.SECONDARY) { openNotificationSettings() },
            LinearLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { topMargin = dp(8) }
        )

        // Acerca de
        val version = try {
            packageManager.getPackageInfo(packageName, 0).versionName ?: ""
        } catch (_: Exception) {
            ""
        }
        col.addView(text("TransferDood $version · By KyaaaDood with Claude AI", 12f, pal.sub), lp(26))
        col.addView(
            text(Loc.t("Apoyar en Patreon"), 13f, pal.accent, true).apply {
                setPadding(0, dp(8), 0, dp(8))
                setOnClickListener {
                    try {
                        startActivity(Intent(Intent.ACTION_VIEW, Uri.parse("https://www.patreon.com/cw/KyaaaDood")))
                    } catch (_: Exception) {
                    }
                }
            },
            lp(2)
        )

        setContentView(scroll)
    }

    private fun rebuildBlocked() {
        blockedBox.removeAllViews()
        val list = Core.prefs.blocked()
        if (list.isEmpty()) {
            blockedBox.addView(text(Loc.t("No has bloqueado a nadie."), 13f, pal.sub).apply { setPadding(0, dp(4), 0, dp(4)) })
            return
        }
        for ((id, name) in list) {
            val row = LinearLayout(this).apply {
                orientation = LinearLayout.HORIZONTAL
                gravity = Gravity.CENTER_VERTICAL
            }
            row.addView(
                text(name.ifEmpty { Loc.t("Dispositivo") }, 14f, pal.text),
                LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f)
            )
            row.addView(Ui.button(this, Loc.t("Desbloquear"), Ui.GHOST) {
                Core.prefs.setBlocked(id, "", false)
                rebuildBlocked()
            }.apply { minHeight = dp(40) })
            blockedBox.addView(row, lp())
        }
    }

    private fun rebuildFavorites() {
        rebuildBlocked()
        favBox.removeAllViews()
        val favs = Core.prefs.favorites()
        if (favs.isEmpty()) {
            favBox.addView(text(Loc.t("Aún no hay favoritos."), 13f, pal.sub).apply { setPadding(0, dp(4), 0, dp(4)) })
            return
        }
        for ((id, name) in favs) {
            val row = LinearLayout(this).apply {
                orientation = LinearLayout.HORIZONTAL
                gravity = Gravity.CENTER_VERTICAL
            }
            row.addView(
                text("★  ${name.ifEmpty { Loc.t("Dispositivo") }}", 14f, pal.text),
                LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f)
            )
            row.addView(Ui.button(this, Loc.t("Quitar"), Ui.GHOST) {
                Core.prefs.removeFavorite(id)
                rebuildFavorites()
            }.apply { minHeight = dp(40) })
            favBox.addView(row, lp())
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

    private fun chooseFolder() {
        val i = Intent(Intent.ACTION_OPEN_DOCUMENT_TREE).addFlags(
            Intent.FLAG_GRANT_READ_URI_PERMISSION or
                    Intent.FLAG_GRANT_WRITE_URI_PERMISSION or
                    Intent.FLAG_GRANT_PERSISTABLE_URI_PERMISSION or
                    Intent.FLAG_GRANT_PREFIX_URI_PERMISSION
        )
        try {
            startActivityForResult(i, REQ_FOLDER)
        } catch (_: Exception) {
        }
    }

    private fun releaseOldTree() {
        val old = Core.prefs.receiveTree ?: return
        try {
            contentResolver.releasePersistableUriPermission(
                Uri.parse(old),
                Intent.FLAG_GRANT_READ_URI_PERMISSION or Intent.FLAG_GRANT_WRITE_URI_PERMISSION
            )
        } catch (_: Exception) {
        }
    }

    @Deprecated("Deprecated in Java")
    override fun onActivityResult(requestCode: Int, resultCode: Int, data: Intent?) {
        super.onActivityResult(requestCode, resultCode, data)
        if (requestCode != REQ_FOLDER || resultCode != RESULT_OK) return
        val uri = data?.data ?: return
        try {
            contentResolver.takePersistableUriPermission(
                uri,
                Intent.FLAG_GRANT_READ_URI_PERMISSION or Intent.FLAG_GRANT_WRITE_URI_PERMISSION
            )
        } catch (_: Exception) {
        }
        if (Core.prefs.receiveTree != uri.toString()) releaseOldTree()
        Core.prefs.receiveTree = uri.toString()
        folderText.text = Storage.describeFolder()
    }

    companion object {
        private const val REQ_FOLDER = 10
    }
}
