package com.kyaaadood.transferdood

import android.app.Activity
import android.app.Dialog
import android.graphics.Color
import android.graphics.drawable.ColorDrawable
import android.graphics.drawable.GradientDrawable
import android.util.TypedValue
import android.view.Gravity
import android.view.View
import android.view.ViewGroup
import android.view.Window
import android.widget.FrameLayout
import android.widget.LinearLayout
import android.widget.TextView

/** Hojas flotantes (tarjeta redondeada que sube desde abajo) en lugar de los diálogos clásicos de Android. */
object Sheets {

    private fun label(a: Activity, s: String, sp: Float, color: Int, bold: Boolean = false) = TextView(a).apply {
        text = s
        setTextSize(TypedValue.COMPLEX_UNIT_SP, sp)
        setTextColor(color)
        if (bold) typeface = Ui.medium
    }

    private fun card(a: Activity): LinearLayout {
        val p = Theme.pal(a)
        return LinearLayout(a).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(a.dp(22), a.dp(22), a.dp(22), a.dp(16))
            background = GradientDrawable().apply {
                cornerRadius = a.dpf(24f)
                setColor(p.surface)
                setStroke(a.dp(1), p.border)
            }
        }
    }

    private fun host(a: Activity, card: View, cancelable: Boolean): Dialog {
        val d = Dialog(a)
        d.requestWindowFeature(Window.FEATURE_NO_TITLE)
        val frame = FrameLayout(a)
        frame.addView(
            card,
            FrameLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT, Gravity.BOTTOM).apply {
                setMargins(a.dp(12), a.dp(12), a.dp(12), a.dp(12))
            }
        )
        d.setContentView(frame)
        d.setCancelable(cancelable)
        d.window?.let { w ->
            w.setBackgroundDrawable(ColorDrawable(Color.TRANSPARENT))
            w.setLayout(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT)
            w.setGravity(Gravity.BOTTOM)
            w.setDimAmount(0.45f)
            val attrs = w.attributes
            attrs.windowAnimations = android.R.style.Animation_InputMethod
            w.attributes = attrs
        }
        return d
    }

    /** Aviso de envío entrante (archivos, texto o emparejamiento): Aceptar / Rechazar. */
    fun incoming(a: Activity, req: IncomingRequest): Dialog {
        val p = Theme.pal(a)
        val card = card(a)

        val top = LinearLayout(a).apply {
            orientation = LinearLayout.HORIZONTAL
            gravity = Gravity.CENTER_VERTICAL
        }
        top.addView(GlyphCircleView(a, true))
        val titles = LinearLayout(a).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(a.dp(14), 0, 0, 0)
        }
        titles.addView(label(a, req.from, 18f, p.text, true).apply {
            maxLines = 1
            ellipsize = android.text.TextUtils.TruncateAt.END
        })
        val sub = when (req.kind) {
            "text" -> Loc.t("quiere enviarte un texto")
            "pair" -> if (req.initiator) Loc.t("emparejar equipos") else Loc.t("quiere emparejarse contigo")
            else -> Loc.t("quiere enviarte")
        }
        titles.addView(label(a, if (req.ip.isNotEmpty()) sub + " · " + req.ip else sub, 13f, p.sub))
        top.addView(titles, LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f))
        card.addView(top)

        val what = when (req.kind) {
            "text" -> req.preview
            "pair" -> req.code
            else -> if (req.count == 1) req.firstName else Loc.f("{0} archivos", req.count)
        }
        card.addView(label(a, what, if (req.kind == "pair") 32f else 15f, p.text, true).apply {
            maxLines = 2
            ellipsize = android.text.TextUtils.TruncateAt.END
            if (req.kind == "pair") letterSpacing = 0.12f
        }, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { topMargin = a.dp(16) })
        val note = when (req.kind) {
            "text" -> "${req.total} caracteres"
            "pair" -> Loc.t("Debe verse igual en el otro equipo")
            else -> formatBytes(req.total)
        }
        card.addView(label(a, note, 13f, p.sub))

        lateinit var dialog: Dialog

        val buttons = LinearLayout(a).apply { orientation = LinearLayout.HORIZONTAL }
        buttons.addView(
            Ui.button(a, if (req.kind == "pair") Loc.t("No coincide") else Loc.t("Rechazar"), Ui.SECONDARY) {
                req.answer.complete(false)
                dialog.dismiss()
            },
            LinearLayout.LayoutParams(0, a.dp(48), 1f).apply { rightMargin = a.dp(6) }
        )
        buttons.addView(
            Ui.button(a, if (req.kind == "pair") Loc.t("Coincide") else Loc.t("Aceptar"), Ui.PRIMARY) {
                req.answer.complete(true)
                dialog.dismiss()
            },
            LinearLayout.LayoutParams(0, a.dp(48), 1f).apply { leftMargin = a.dp(6) }
        )
        card.addView(buttons, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { topMargin = a.dp(18) })

        dialog = host(a, card, false)
        return dialog
    }

    private fun input(a: Activity, hint: String, multiline: Boolean, initial: String): android.widget.EditText {
        val p = Theme.pal(a)
        return android.widget.EditText(a).apply {
            this.hint = hint
            setText(initial)
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 15f)
            setTextColor(p.text)
            setHintTextColor(p.sub)
            setPadding(a.dp(14), a.dp(12), a.dp(14), a.dp(12))
            background = GradientDrawable().apply {
                cornerRadius = a.dpf(12f)
                setColor(p.surfaceAlt)
            }
            if (multiline) {
                inputType = android.text.InputType.TYPE_CLASS_TEXT or android.text.InputType.TYPE_TEXT_FLAG_MULTI_LINE
                minLines = 4
                maxLines = 8
                gravity = Gravity.TOP
            } else {
                inputType = android.text.InputType.TYPE_CLASS_TEXT or android.text.InputType.TYPE_TEXT_VARIATION_URI
                setSingleLine()
            }
        }
    }

    /** Escribir o pegar un texto para enviarlo. */
    fun textInput(a: Activity, initial: String, onOk: (String) -> Unit): Dialog {
        val p = Theme.pal(a)
        val card = card(a)
        card.addView(label(a, Loc.t("Enviar texto"), 18f, p.text, true))
        card.addView(label(a, Loc.t("Escribe o pega un texto o enlace. Después elige el equipo."), 13f, p.sub))
        val box = input(a, Loc.t("Texto"), true, initial)
        card.addView(box, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { topMargin = a.dp(14) })

        lateinit var dialog: Dialog
        val buttons = LinearLayout(a).apply { orientation = LinearLayout.HORIZONTAL }
        buttons.addView(
            Ui.button(a, Loc.t("Cancelar"), Ui.SECONDARY) { dialog.dismiss() },
            LinearLayout.LayoutParams(0, a.dp(48), 1f).apply { rightMargin = a.dp(6) }
        )
        buttons.addView(
            Ui.button(a, Loc.t("Continuar"), Ui.PRIMARY) {
                val t = box.text.toString().trim()
                if (t.isNotEmpty()) {
                    dialog.dismiss()
                    onOk(t)
                }
            },
            LinearLayout.LayoutParams(0, a.dp(48), 1f).apply { leftMargin = a.dp(6) }
        )
        card.addView(buttons, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { topMargin = a.dp(16) })
        dialog = host(a, card, true)
        dialog.window?.setSoftInputMode(android.view.WindowManager.LayoutParams.SOFT_INPUT_ADJUST_RESIZE or android.view.WindowManager.LayoutParams.SOFT_INPUT_STATE_VISIBLE)
        return dialog
    }

    /** Conectar por IP. [myIps] se muestra para dictárselo al otro equipo. */
    fun ipInput(a: Activity, myIps: String, saved: String, onOk: (String) -> Unit, onForget: () -> Unit): Dialog {
        val p = Theme.pal(a)
        val card = card(a)
        card.addView(label(a, Loc.t("Conectar por IP"), 18f, p.text, true))
        card.addView(label(a, Loc.t("Úsalo si el otro equipo no aparece solo (algunas redes bloquean la búsqueda)."), 13f, p.sub))
        card.addView(label(a, Loc.t("Tu IP: ") + myIps, 14f, p.text, true),
            LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { topMargin = a.dp(12) })
        val box = input(a, "192.168.1.25", false, "")
        card.addView(box, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { topMargin = a.dp(12) })
        card.addView(label(a, saved, 12f, p.sub),
            LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { topMargin = a.dp(8) })

        lateinit var dialog: Dialog
        val buttons = LinearLayout(a).apply { orientation = LinearLayout.HORIZONTAL }
        buttons.addView(
            Ui.button(a, Loc.t("Olvidar guardadas"), Ui.SECONDARY) {
                dialog.dismiss()
                onForget()
            },
            LinearLayout.LayoutParams(0, a.dp(48), 1f).apply { rightMargin = a.dp(6) }
        )
        buttons.addView(
            Ui.button(a, Loc.t("Conectar"), Ui.PRIMARY) {
                val t = box.text.toString().trim()
                if (t.isNotEmpty()) {
                    dialog.dismiss()
                    onOk(t)
                }
            },
            LinearLayout.LayoutParams(0, a.dp(48), 1f).apply { leftMargin = a.dp(6) }
        )
        card.addView(buttons, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { topMargin = a.dp(16) })
        dialog = host(a, card, true)
        dialog.window?.setSoftInputMode(android.view.WindowManager.LayoutParams.SOFT_INPUT_ADJUST_RESIZE or android.view.WindowManager.LayoutParams.SOFT_INPUT_STATE_VISIBLE)
        return dialog
    }

    /** Menú de un dispositivo: lista de acciones como botones en una hoja. */
    fun actions(a: Activity, title: String, items: List<Pair<String, () -> Unit>>): Dialog {
        val p = Theme.pal(a)
        val card = card(a)
        card.addView(label(a, title, 18f, p.text, true))
        lateinit var dialog: Dialog
        for ((text, act) in items) {
            card.addView(
                Ui.button(a, text, Ui.GHOST) {
                    dialog.dismiss()
                    act()
                },
                LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, a.dp(48)).apply { topMargin = a.dp(6) }
            )
        }
        dialog = host(a, card, true)
        return dialog
    }

    /** Confirmación sencilla con dos botones. */
    fun confirm(
        a: Activity,
        title: String,
        message: String,
        positive: String,
        negative: String,
        onPositive: () -> Unit,
        onNegative: () -> Unit
    ): Dialog {
        val p = Theme.pal(a)
        val card = card(a)
        card.addView(label(a, title, 18f, p.text, true))
        card.addView(label(a, message, 14f, p.sub), LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT
        ).apply { topMargin = a.dp(8) })

        lateinit var dialog: Dialog
        val buttons = LinearLayout(a).apply { orientation = LinearLayout.HORIZONTAL }
        buttons.addView(
            Ui.button(a, negative, Ui.SECONDARY) {
                dialog.dismiss()
                onNegative()
            },
            LinearLayout.LayoutParams(0, a.dp(48), 1f).apply { rightMargin = a.dp(6) }
        )
        buttons.addView(
            Ui.button(a, positive, Ui.PRIMARY) {
                dialog.dismiss()
                onPositive()
            },
            LinearLayout.LayoutParams(0, a.dp(48), 1f).apply { leftMargin = a.dp(6) }
        )
        card.addView(buttons, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { topMargin = a.dp(18) })

        dialog = host(a, card, false)
        return dialog
    }
}
