package com.kyaaadood.transferdood

import android.app.Activity
import android.content.Context
import android.content.res.ColorStateList
import android.content.res.Configuration
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Paint
import android.graphics.Path
import android.graphics.Typeface
import android.graphics.drawable.ColorDrawable
import android.graphics.drawable.GradientDrawable
import android.graphics.drawable.RippleDrawable
import android.graphics.RectF
import android.os.Build
import android.text.TextPaint
import android.text.TextUtils
import android.util.TypedValue
import android.view.Gravity
import android.view.MotionEvent
import android.view.View
import android.widget.TextView

class Pal(
    val bg: Int, val surface: Int, val surfaceAlt: Int, val border: Int, val text: Int, val sub: Int,
    val accent: Int, val accentHover: Int, val accentSoft: Int, val danger: Int, val success: Int
)

object Theme {
    private fun c(rgb: Int) = rgb or (0xFF shl 24)

    private val light = Pal(
        c(0xF4F6FA), c(0xFFFFFF), c(0xE9EDF4), c(0xDCE1EA), c(0x141824), c(0x667084),
        c(0x2F6BFF), c(0x1F58E6), c(0xE6EEFF), c(0xE5484D), c(0x24A867)
    )
    private val dark = Pal(
        c(0x12141A), c(0x1A1D26), c(0x262A38), c(0x2E3342), c(0xE8EBF2), c(0x98A1B5),
        c(0x5B8CFF), c(0x7BA0FF), c(0x1F2A4A), c(0xFF6B70), c(0x46C985)
    )

    fun isDark(ctx: Context): Boolean = when (Core.prefs.theme) {
        "dark" -> true
        "light" -> false
        else -> (ctx.resources.configuration.uiMode and Configuration.UI_MODE_NIGHT_MASK) == Configuration.UI_MODE_NIGHT_YES
    }

    fun pal(ctx: Context): Pal = if (isDark(ctx)) dark else light

    fun dialogTheme(ctx: Context): Int =
        if (isDark(ctx)) android.R.style.Theme_Material_Dialog_Alert
        else android.R.style.Theme_Material_Light_Dialog_Alert

    /** Colores de ventana, barra de estado y navegación según el tema elegido. */
    @Suppress("DEPRECATION")
    fun apply(a: Activity) {
        val p = pal(a)
        val d = isDark(a)
        a.window.setBackgroundDrawable(ColorDrawable(p.bg))
        a.window.statusBarColor = p.bg
        a.window.navigationBarColor = p.bg
        var flags = a.window.decorView.systemUiVisibility
        flags = if (!d) {
            var f = flags or View.SYSTEM_UI_FLAG_LIGHT_STATUS_BAR
            if (Build.VERSION.SDK_INT >= 26) f = f or View.SYSTEM_UI_FLAG_LIGHT_NAVIGATION_BAR
            f
        } else {
            var f = flags and View.SYSTEM_UI_FLAG_LIGHT_STATUS_BAR.inv()
            if (Build.VERSION.SDK_INT >= 26) f = f and View.SYSTEM_UI_FLAG_LIGHT_NAVIGATION_BAR.inv()
            f
        }
        a.window.decorView.systemUiVisibility = flags
    }
}

fun Context.dp(v: Int): Int = (v * resources.displayMetrics.density + 0.5f).toInt()
fun Context.dpf(v: Float): Float = v * resources.displayMetrics.density

object Ui {
    val medium: Typeface = Typeface.create("sans-serif-medium", Typeface.NORMAL)

    fun alpha(color: Int, a: Int): Int = (color and 0x00FFFFFF) or (a shl 24)

    fun textPaint(ctx: Context, sp: Float, color: Int, bold: Boolean = false): TextPaint {
        val p = TextPaint(Paint.ANTI_ALIAS_FLAG)
        p.textSize = TypedValue.applyDimension(TypedValue.COMPLEX_UNIT_SP, sp, ctx.resources.displayMetrics)
        p.color = color
        if (bold) p.typeface = medium
        return p
    }

    /** Dibuja texto de una línea con "…" si no cabe. [top] es el borde superior de la línea. */
    fun drawText(c: Canvas, s: String, x: Float, top: Float, p: TextPaint, maxW: Float, align: Paint.Align = Paint.Align.LEFT) {
        val t = TextUtils.ellipsize(s, p, maxW.coerceAtLeast(1f), TextUtils.TruncateAt.END).toString()
        p.textAlign = align
        val y = top - p.fontMetrics.ascent
        val xx = when (align) {
            Paint.Align.RIGHT -> x + maxW
            Paint.Align.CENTER -> x + maxW / 2
            else -> x
        }
        c.drawText(t, xx, y, p)
    }

    const val PRIMARY = 0
    const val SECONDARY = 1
    const val GHOST = 2

    fun button(ctx: Context, text: String, kind: Int, onClick: () -> Unit): TextView {
        val p = Theme.pal(ctx)
        val bg: Int
        val fg: Int
        var stroke = 0
        when (kind) {
            PRIMARY -> { bg = p.accent; fg = Color.WHITE }
            GHOST -> { bg = Color.TRANSPARENT; fg = p.accent }
            else -> { bg = p.surface; fg = p.text; stroke = p.border }
        }
        val shape = GradientDrawable().apply {
            cornerRadius = ctx.dpf(10f)
            setColor(bg)
            if (stroke != 0) setStroke(ctx.dp(1), stroke)
        }
        return TextView(ctx).apply {
            this.text = text
            gravity = Gravity.CENTER
            typeface = medium
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 14f)
            setTextColor(fg)
            minHeight = ctx.dp(44)
            setPadding(ctx.dp(14), 0, ctx.dp(14), 0)
            background = RippleDrawable(ColorStateList.valueOf(alpha(fg, 50)), shape, null)
            isClickable = true
            isFocusable = true
            setOnClickListener { onClick() }
        }
    }
}

// ───────────────────────────── Vistas ─────────────────────────────

class DeviceCardView(
    ctx: Context,
    val peer: Peer,
    private val favorite: Boolean,
    private val onStar: () -> Unit
) : View(ctx) {
    private val paint = Paint(Paint.ANTI_ALIAS_FLAG)
    private val rect = RectF()
    private val path = Path()
    private val starRect = RectF()
    private var starDown = false

    init {
        isClickable = true
        isFocusable = true
    }

    override fun onSizeChanged(w: Int, h: Int, oldw: Int, oldh: Int) {
        super.onSizeChanged(w, h, oldw, oldh)
        val k = resources.displayMetrics.density
        starRect.set(w - 54 * k, 0f, w.toFloat(), 54 * k)
    }

    override fun onTouchEvent(e: MotionEvent): Boolean {
        val inStar = starRect.contains(e.x, e.y)
        when (e.actionMasked) {
            MotionEvent.ACTION_DOWN -> if (inStar) {
                starDown = true
                return true
            }
            MotionEvent.ACTION_UP -> if (starDown) {
                starDown = false
                if (inStar) onStar()
                return true
            }
            MotionEvent.ACTION_CANCEL -> if (starDown) {
                starDown = false
                return true
            }
        }
        if (starDown) return true
        return super.onTouchEvent(e)
    }

    private fun drawStar(c: Canvas, cx: Float, cy: Float, r: Float, filled: Boolean, color: Int) {
        path.reset()
        for (i in 0 until 10) {
            val rad = if (i % 2 == 0) r else r * 0.45f
            val ang = Math.PI / 2 + i * Math.PI / 5
            val x = cx + (rad * Math.cos(ang)).toFloat()
            val y = cy - (rad * Math.sin(ang)).toFloat()
            if (i == 0) path.moveTo(x, y) else path.lineTo(x, y)
        }
        path.close()
        paint.color = color
        paint.strokeJoin = Paint.Join.ROUND
        if (filled) {
            paint.style = Paint.Style.FILL
        } else {
            paint.style = Paint.Style.STROKE
            paint.strokeWidth = 1.8f * resources.displayMetrics.density
        }
        c.drawPath(path, paint)
    }

    override fun onMeasure(widthMeasureSpec: Int, heightMeasureSpec: Int) {
        setMeasuredDimension(MeasureSpec.getSize(widthMeasureSpec), context.dp(118))
    }

    override fun drawableStateChanged() {
        super.drawableStateChanged()
        invalidate()
    }

    override fun onDraw(c: Canvas) {
        val p = Theme.pal(context)
        val k = resources.displayMetrics.density
        val w = width.toFloat()
        val h = height.toFloat()
        val hot = isPressed || isFocused

        rect.set(k, k, w - k, h - k)
        paint.style = Paint.Style.FILL
        paint.color = p.surface
        c.drawRoundRect(rect, 14 * k, 14 * k, paint)
        paint.style = Paint.Style.STROKE
        paint.strokeWidth = if (hot) 2 * k else k
        paint.color = if (hot) p.accent else p.border
        c.drawRoundRect(rect, 14 * k, 14 * k, paint)

        // Icono
        val cx = 16 * k + 23 * k
        val cy = 16 * k + 23 * k
        paint.style = Paint.Style.FILL
        paint.color = p.accentSoft
        c.drawCircle(cx, cy, 23 * k, paint)
        drawGlyph(c, cx, cy, k, p.accent, peer.os.equals("android", ignoreCase = true))

        // Textos
        val nameP = Ui.textPaint(context, 15f, p.text, true)
        val subP = Ui.textPaint(context, 12f, p.sub)
        Ui.drawText(c, peer.name, 16 * k, 70 * k, nameP, w - 32 * k)
        val os = if (peer.os.equals("android", ignoreCase = true)) "Android" else "Windows"
        Ui.drawText(c, "$os · ${peer.address.hostAddress}", 16 * k, 93 * k, subP, w - 32 * k)

        // Estrella de favorito
        if (favorite) drawStar(c, w - 28 * k, 28 * k, 11 * k, true, Color.rgb(0xF5, 0xB3, 0x01))
        else drawStar(c, w - 28 * k, 28 * k, 11 * k, false, p.sub)
    }

    private fun drawGlyph(c: Canvas, cx: Float, cy: Float, k: Float, color: Int, phone: Boolean) {
        paint.style = Paint.Style.STROKE
        paint.strokeWidth = 2.2f * k
        paint.strokeCap = Paint.Cap.ROUND
        paint.strokeJoin = Paint.Join.ROUND
        paint.color = color
        if (phone) {
            rect.set(cx - 7 * k, cy - 12 * k, cx + 7 * k, cy + 12 * k)
            c.drawRoundRect(rect, 3 * k, 3 * k, paint)
            c.drawLine(cx - 2.5f * k, cy + 8 * k, cx + 2.5f * k, cy + 8 * k, paint)
        } else {
            rect.set(cx - 12 * k, cy - 10 * k, cx + 12 * k, cy + 6 * k)
            c.drawRoundRect(rect, 2.5f * k, 2.5f * k, paint)
            c.drawLine(cx, cy + 6 * k, cx, cy + 10 * k, paint)
            c.drawLine(cx - 6 * k, cy + 11 * k, cx + 6 * k, cy + 11 * k, paint)
        }
        paint.strokeCap = Paint.Cap.BUTT
    }
}

class TransferRowView(
    ctx: Context,
    val job: TransferJob,
    private val onAction: (TransferJob, String) -> Unit
) : View(ctx) {
    private val paint = Paint(Paint.ANTI_ALIAS_FLAG)
    private val rect = RectF()
    private val path = Path()
    private var actionRect = RectF()
    private var actionRect2 = RectF()

    override fun onMeasure(widthMeasureSpec: Int, heightMeasureSpec: Int) {
        setMeasuredDimension(MeasureSpec.getSize(widthMeasureSpec), context.dp(70))
    }

    private fun actionText(): String? {
        val s = job.state
        if (s == JobState.ACTIVE || (s == JobState.WAITING && !job.incoming)) return Loc.t("Cancelar")
        if (s == JobState.DONE && job.kind == "text" && !job.text.isNullOrEmpty()) return Loc.t("Copiar")
        if (s == JobState.DONE && job.incoming && job.kind == "files")
        {
            if (job.fileCount == 1 && job.title.lowercase().endsWith(".apk")) return Loc.t("Instalar")
            if (job.fileCount == 1 && job.openUri != null && Viewer.kindOf(job.title) != null) return Loc.t("Ver")
            return Loc.t("Abrir")
        }
        return null
    }

    private fun action2Text(): String? {
        if (job.state == JobState.ACTIVE && job.kind == "files") return if (job.paused) Loc.t("Reanudar") else Loc.t("Pausar")
        if (job.state == JobState.DONE && job.incoming && job.kind == "files" && job.fileCount == 1 && job.openUri != null) return Loc.t("Compartir")
        return null
    }

    override fun onTouchEvent(e: MotionEvent): Boolean {
        val a1 = actionText()
        val a2 = action2Text()
        val in1 = a1 != null && actionRect.contains(e.x, e.y)
        val in2 = a2 != null && actionRect2.contains(e.x, e.y)
        when (e.actionMasked) {
            MotionEvent.ACTION_DOWN -> if (in1 || in2) return true
            MotionEvent.ACTION_UP -> {
                if (in2) {
                    onAction(job, a2!!)
                    return true
                }
                if (in1) {
                    onAction(job, a1!!)
                    return true
                }
            }
        }
        return super.onTouchEvent(e)
    }

    override fun onDraw(c: Canvas) {
        val p = Theme.pal(context)
        val k = resources.displayMetrics.density
        val w = width.toFloat()
        val h = height.toFloat()

        rect.set(k, k, w - k, h - k)
        paint.style = Paint.Style.FILL
        paint.color = p.surface
        c.drawRoundRect(rect, 12 * k, 12 * k, paint)
        paint.style = Paint.Style.STROKE
        paint.strokeWidth = k
        paint.color = p.border
        c.drawRoundRect(rect, 12 * k, 12 * k, paint)

        val state = job.state
        val tone = when (state) {
            JobState.DONE -> p.success
            JobState.FAILED -> p.danger
            JobState.DECLINED, JobState.CANCELLED -> p.sub
            else -> p.accent
        }

        // Icono circular con flecha
        val d = 38 * k
        val cx = 14 * k + d / 2
        val cy = h / 2
        paint.style = Paint.Style.FILL
        paint.color = Ui.alpha(tone, if (Theme.isDark(context)) 50 else 36)
        c.drawCircle(cx, cy, d / 2, paint)
        drawArrow(c, cx, cy, k, tone, job.incoming)

        // Acción a la derecha
        var right = w - 16 * k
        val act = actionText()
        if (act != null) {
            val aw = 78 * k
            actionRect.set(right - aw, 0f, right + 16 * k, h)
            Ui.drawText(c, act, right - aw, h / 2 - 9 * k, Ui.textPaint(context, 13f, p.accent, true), aw, Paint.Align.RIGHT)
            right -= aw + 6 * k
        } else {
            actionRect.setEmpty()
        }
        val act2 = action2Text()
        if (act2 != null) {
            val aw = 84 * k
            actionRect2.set(right - aw, 0f, right + 6 * k, h)
            Ui.drawText(c, act2, right - aw, h / 2 - 9 * k, Ui.textPaint(context, 13f, p.sub, true), aw, Paint.Align.RIGHT)
            right -= aw + 6 * k
        } else {
            actionRect2.setEmpty()
        }

        val x = 64 * k
        val tw = (right - x).coerceAtLeast(10f)
        Ui.drawText(c, job.title, x, 9 * k, Ui.textPaint(context, 14.5f, p.text, true), tw)

        val peer = if (job.incoming) Loc.f("de {0}", job.peerName) else Loc.f("para {0}", job.peerName)
        val sub = when (state) {
            JobState.WAITING -> if (job.incoming) Loc.f("Esperando tu respuesta · {0}", peer) else Loc.f("Esperando que {0} acepte…", job.peerName)
            JobState.ACTIVE -> if (job.paused) Loc.f("En pausa · {0} de {1} · {2}", formatBytes(job.done.get()), formatBytes(job.totalBytes), peer)
            else if (job.kind == "text") Loc.f("Enviando texto · {0}", peer)
            else Loc.f("{0} de {1} · {2}/s · {3}", formatBytes(job.done.get()), formatBytes(job.totalBytes), formatBytes(job.speed.toLong()), peer)
            JobState.DONE -> if (job.kind == "text") Loc.f("Texto · {0}", peer) else Loc.f("{0} · {1} · completado", formatBytes(job.totalBytes), peer)
            JobState.DECLINED -> Loc.f("Rechazado · {0}", peer)
            JobState.CANCELLED -> Loc.f("Cancelado · {0}", peer)
            JobState.FAILED -> Loc.f("Error: {0}", job.error ?: Loc.t("falló la transferencia"))
        }
        Ui.drawText(c, sub, x, 31 * k, Ui.textPaint(context, 11.5f, if (state == JobState.FAILED) p.danger else p.sub), tw)

        // Barra de progreso
        val by = 54 * k
        val bh = 6 * k
        rect.set(x, by, x + tw, by + bh)
        paint.color = p.surfaceAlt
        c.drawRoundRect(rect, bh / 2, bh / 2, paint)

        var frac = if (job.totalBytes > 0) job.done.get().toDouble() / job.totalBytes else 0.0
        if (state == JobState.DONE) frac = 1.0
        frac = frac.coerceIn(0.0, 1.0)
        if (frac > 0) {
            val fw = (tw * frac).toFloat().coerceAtLeast(bh)
            rect.set(x, by, x + fw, by + bh)
            paint.color = tone
            c.drawRoundRect(rect, bh / 2, bh / 2, paint)
        }
    }

    private fun drawArrow(c: Canvas, cx: Float, cy: Float, k: Float, color: Int, down: Boolean) {
        paint.style = Paint.Style.STROKE
        paint.strokeWidth = 2.4f * k
        paint.strokeCap = Paint.Cap.ROUND
        paint.strokeJoin = Paint.Join.ROUND
        paint.color = color
        val s = if (down) 1f else -1f
        c.drawLine(cx, cy - 7 * k * s, cx, cy + 7 * k * s, paint)
        path.reset()
        path.moveTo(cx - 5 * k, cy + 2 * k * s)
        path.lineTo(cx, cy + 7 * k * s)
        path.lineTo(cx + 5 * k, cy + 2 * k * s)
        c.drawPath(path, paint)
        paint.strokeCap = Paint.Cap.BUTT
    }
}

class PendingBarView(ctx: Context, private val actionLabel: String, private val onClear: () -> Unit) : View(ctx) {
    private val paint = Paint(Paint.ANTI_ALIAS_FLAG)
    private val rect = RectF()
    private var clearRect = RectF()

    var info: String = ""
        set(v) {
            field = v
            invalidate()
        }

    override fun onMeasure(widthMeasureSpec: Int, heightMeasureSpec: Int) {
        setMeasuredDimension(MeasureSpec.getSize(widthMeasureSpec), context.dp(54))
    }

    override fun onTouchEvent(e: MotionEvent): Boolean {
        val inside = clearRect.contains(e.x, e.y)
        when (e.actionMasked) {
            MotionEvent.ACTION_DOWN -> return inside
            MotionEvent.ACTION_UP -> if (inside) {
                onClear()
                return true
            }
        }
        return super.onTouchEvent(e)
    }

    override fun onDraw(c: Canvas) {
        val p = Theme.pal(context)
        val k = resources.displayMetrics.density
        val w = width.toFloat()
        val h = height.toFloat()

        rect.set(k, k, w - k, h - k)
        paint.style = Paint.Style.FILL
        paint.color = p.accentSoft
        c.drawRoundRect(rect, 12 * k, 12 * k, paint)
        paint.style = Paint.Style.STROKE
        paint.strokeWidth = k
        paint.color = Ui.alpha(p.accent, 140)
        c.drawRoundRect(rect, 12 * k, 12 * k, paint)

        val aw = 64 * k
        clearRect.set(w - 16 * k - aw - 8 * k, 0f, w, h)
        Ui.drawText(c, actionLabel, w - 16 * k - aw, h / 2 - 9 * k, Ui.textPaint(context, 13f, p.accent, true), aw, Paint.Align.RIGHT)
        Ui.drawText(c, info, 16 * k, h / 2 - 9 * k, Ui.textPaint(context, 13f, p.text), w - 32 * k - aw - 8 * k)
    }
}

/** Círculo con una flecha (abajo = recibir), igual al de la lista de transferencias. */
class GlyphCircleView(ctx: Context, private val down: Boolean) : View(ctx) {
    private val paint = Paint(Paint.ANTI_ALIAS_FLAG)
    private val path = Path()

    override fun onMeasure(widthMeasureSpec: Int, heightMeasureSpec: Int) {
        val s = context.dp(46)
        setMeasuredDimension(s, s)
    }

    override fun onDraw(c: Canvas) {
        val p = Theme.pal(context)
        val k = resources.displayMetrics.density
        val cx = width / 2f
        val cy = height / 2f
        paint.style = Paint.Style.FILL
        paint.color = p.accentSoft
        c.drawCircle(cx, cy, width / 2f, paint)

        paint.style = Paint.Style.STROKE
        paint.strokeWidth = 2.4f * k
        paint.strokeCap = Paint.Cap.ROUND
        paint.strokeJoin = Paint.Join.ROUND
        paint.color = p.accent
        val s = if (down) 1f else -1f
        c.drawLine(cx, cy - 8 * k * s, cx, cy + 8 * k * s, paint)
        path.reset()
        path.moveTo(cx - 6 * k, cy + 2 * k * s)
        path.lineTo(cx, cy + 8 * k * s)
        path.lineTo(cx + 6 * k, cy + 2 * k * s)
        c.drawPath(path, paint)
    }
}
