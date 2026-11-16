package com.kyaaadood.transferdood

import android.app.Activity
import android.content.Context
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Matrix
import android.graphics.RectF
import android.graphics.Typeface
import android.graphics.pdf.PdfRenderer
import android.media.ExifInterface
import android.media.MediaPlayer
import android.net.Uri
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.os.ParcelFileDescriptor
import android.util.LruCache
import android.util.TypedValue
import android.view.Gravity
import android.view.MotionEvent
import android.view.ScaleGestureDetector
import android.view.View
import android.view.ViewGroup
import android.widget.AbsListView
import android.widget.BaseAdapter
import android.widget.FrameLayout
import android.widget.ImageView
import android.widget.LinearLayout
import android.widget.ListView
import android.widget.MediaController
import android.widget.ScrollView
import android.widget.SeekBar
import android.widget.TextView
import android.widget.VideoView
import kotlin.concurrent.thread

/** Vista previa ligera dentro de la app: imágenes, PDF, audio, video y texto (solo con lo que trae Android). */
class PreviewActivity : Activity() {
    private lateinit var pal: Pal
    private lateinit var uri: Uri
    private lateinit var name: String
    private lateinit var holder: FrameLayout
    private var shown = false
    private var renderer: PdfRenderer? = null
    private var pfd: ParcelFileDescriptor? = null
    private var player: MediaPlayer? = null
    private var video: VideoView? = null
    private val ui = Handler(Looper.getMainLooper())
    private var ticker: Runnable? = null

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        pal = Theme.pal(this)
        Theme.apply(this)
        val u = intent.getStringExtra("uri")
        if (u == null) {
            finish()
            return
        }
        uri = Uri.parse(u)
        name = intent.getStringExtra("name") ?: "archivo"
        title = name

        val root = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL }
        root.addView(TextView(this).apply {
            text = name
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 15f)
            setTextColor(pal.text)
            typeface = Ui.medium
            maxLines = 1
            ellipsize = android.text.TextUtils.TruncateAt.MIDDLE
            setPadding(dp(16), dp(12), dp(16), dp(10))
        }, LinearLayout.LayoutParams(-1, -2))
        holder = FrameLayout(this)
        root.addView(holder, LinearLayout.LayoutParams(-1, 0, 1f))
        val bar = LinearLayout(this).apply {
            orientation = LinearLayout.HORIZONTAL
            setPadding(dp(12), dp(8), dp(12), dp(8))
        }
        val m = dp(4)
        bar.addView(Ui.button(this, Loc.t("Compartir"), Ui.PRIMARY) { Viewer.share(this, uri, name) },
            LinearLayout.LayoutParams(0, -2, 1f).apply { setMargins(m, 0, m, 0) })
        bar.addView(Ui.button(this, Loc.t("Abrir con…"), Ui.SECONDARY) { Viewer.openWith(this, uri, name) },
            LinearLayout.LayoutParams(0, -2, 1f).apply { setMargins(m, 0, m, 0) })
        root.addView(bar, LinearLayout.LayoutParams(-1, -2))
        setContentView(root)

        try {
            when (Viewer.kindOf(name)) {
                "image" -> showImage()
                "pdf" -> showPdf()
                "audio" -> showAudio()
                "video" -> showVideo()
                "text" -> showText()
                else -> message(Loc.t("No se puede previsualizar este tipo de archivo."))
            }
        } catch (e: Throwable) {
            message(Loc.t("No se pudo mostrar la vista previa. Prueba «Abrir con…»."))
        }
    }

    override fun onStart() {
        super.onStart()
        shown = true
        Core.onUiShown()
    }

    override fun onStop() {
        try { player?.takeIf { it.isPlaying }?.pause() } catch (_: Exception) {}
        try { video?.pause() } catch (_: Exception) {}
        if (shown) {
            shown = false
            Core.onUiHidden()
        }
        super.onStop()
    }

    override fun onDestroy() {
        ticker?.let { ui.removeCallbacks(it) }
        try { player?.release() } catch (_: Exception) {}
        try { video?.stopPlayback() } catch (_: Exception) {}
        try { renderer?.close() } catch (_: Exception) {}
        try { pfd?.close() } catch (_: Exception) {}
        super.onDestroy()
    }

    private fun message(s: String) {
        holder.removeAllViews()
        holder.addView(TextView(this).apply {
            text = s
            gravity = Gravity.CENTER
            setTextColor(pal.sub)
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 14f)
            setPadding(dp(24), dp(24), dp(24), dp(24))
        }, FrameLayout.LayoutParams(-1, -1))
    }

    // ───────── Imagen ─────────
    private fun showImage() {
        message(Loc.t("Cargando…"))
        thread(isDaemon = true) {
            var bmp: Bitmap? = null
            try {
                val o = BitmapFactory.Options().apply { inJustDecodeBounds = true }
                contentResolver.openInputStream(uri)?.use { BitmapFactory.decodeStream(it, null, o) }
                var sample = 1
                while (o.outWidth / sample > 2600 || o.outHeight / sample > 2600) sample *= 2
                val o2 = BitmapFactory.Options().apply { inSampleSize = sample }
                bmp = contentResolver.openInputStream(uri)?.use { BitmapFactory.decodeStream(it, null, o2) }
                if (bmp != null) {
                    val orient = try {
                        contentResolver.openInputStream(uri)?.use {
                            ExifInterface(it).getAttributeInt(ExifInterface.TAG_ORIENTATION, ExifInterface.ORIENTATION_NORMAL)
                        } ?: 1
                    } catch (_: Exception) { 1 }
                    val mx = Matrix()
                    when (orient) {
                        ExifInterface.ORIENTATION_ROTATE_90 -> mx.postRotate(90f)
                        ExifInterface.ORIENTATION_ROTATE_180 -> mx.postRotate(180f)
                        ExifInterface.ORIENTATION_ROTATE_270 -> mx.postRotate(270f)
                        ExifInterface.ORIENTATION_FLIP_HORIZONTAL -> mx.postScale(-1f, 1f)
                        ExifInterface.ORIENTATION_FLIP_VERTICAL -> mx.postScale(1f, -1f)
                    }
                    if (!mx.isIdentity) bmp = Bitmap.createBitmap(bmp, 0, 0, bmp.width, bmp.height, mx, true)
                }
            } catch (_: Throwable) {
            }
            val result = bmp
            runOnUiThread {
                if (isFinishing) return@runOnUiThread
                if (result == null) {
                    message(Loc.t("No se pudo mostrar la vista previa. Prueba «Abrir con…»."))
                } else {
                    holder.removeAllViews()
                    holder.addView(ZoomImageView(this).also { it.setBitmap(result) }, FrameLayout.LayoutParams(-1, -1))
                }
            }
        }
    }

    // ───────── PDF ─────────
    private fun showPdf() {
        val fd = contentResolver.openFileDescriptor(uri, "r") ?: throw IllegalStateException()
        pfd = fd
        val r = try {
            PdfRenderer(fd)
        } catch (e: Throwable) {
            message(Loc.t("No se pudo mostrar la vista previa. Prueba «Abrir con…»."))
            return
        }
        renderer = r
        val screenW = resources.displayMetrics.widthPixels
        val cache = object : LruCache<Int, Bitmap>(6) {}
        val list = ListView(this).apply {
            divider = android.graphics.drawable.ColorDrawable(pal.border)
            dividerHeight = dp(6)
            setBackgroundColor(pal.surfaceAlt)
            isVerticalScrollBarEnabled = true
        }
        list.adapter = object : BaseAdapter() {
            override fun getCount() = r.pageCount
            override fun getItem(p: Int) = p
            override fun getItemId(p: Int) = p.toLong()
            override fun getView(p: Int, v: View?, parent: ViewGroup): View {
                val iv = (v as? ImageView) ?: ImageView(this@PreviewActivity).apply {
                    scaleType = ImageView.ScaleType.FIT_XY
                    setBackgroundColor(Color.WHITE)
                }
                var bmp = cache.get(p)
                if (bmp == null) {
                    try {
                        r.openPage(p).use { page ->
                            val h = (screenW.toFloat() * page.height / page.width).toInt().coerceAtLeast(1)
                            val b = Bitmap.createBitmap(screenW, h, Bitmap.Config.ARGB_8888)
                            b.eraseColor(Color.WHITE)
                            page.render(b, null, null, PdfRenderer.Page.RENDER_MODE_FOR_DISPLAY)
                            cache.put(p, b)
                            bmp = b
                        }
                    } catch (_: Throwable) {
                    }
                }
                val b = bmp
                iv.setImageBitmap(b)
                val h = if (b != null) b.height else screenW
                iv.layoutParams = AbsListView.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, h)
                return iv
            }
        }
        holder.removeAllViews()
        holder.addView(list, FrameLayout.LayoutParams(-1, -1))
    }

    // ───────── Audio ─────────
    private fun showAudio() {
        val mp = MediaPlayer()
        player = mp
        mp.setDataSource(this, uri)
        mp.prepare()
        val col = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            gravity = Gravity.CENTER
            setPadding(dp(24), dp(24), dp(24), dp(24))
        }
        col.addView(TextView(this).apply {
            text = "♪"
            gravity = Gravity.CENTER
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 64f)
            setTextColor(pal.accent)
        }, LinearLayout.LayoutParams(-1, -2))
        val time = TextView(this).apply {
            gravity = Gravity.CENTER
            setTextColor(pal.sub)
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 13f)
        }
        val seek = SeekBar(this).apply { max = mp.duration.coerceAtLeast(1) }
        col.addView(seek, LinearLayout.LayoutParams(-1, -2).apply { topMargin = dp(24) })
        col.addView(time, LinearLayout.LayoutParams(-1, -2))
        val playBtn = Ui.button(this, Loc.t("Reproducir"), Ui.PRIMARY) {}
        fun fmt(ms: Int): String {
            val s = ms / 1000
            return "%d:%02d".format(s / 60, s % 60)
        }
        fun refresh() {
            try {
                seek.progress = mp.currentPosition
                time.text = fmt(mp.currentPosition) + " / " + fmt(mp.duration)
                playBtn.text = if (mp.isPlaying) Loc.t("Pausar") else Loc.t("Reproducir")
            } catch (_: Exception) {
            }
        }
        playBtn.setOnClickListener {
            try {
                if (mp.isPlaying) mp.pause() else mp.start()
            } catch (_: Exception) {
            }
            refresh()
        }
        col.addView(playBtn, LinearLayout.LayoutParams(dp(180), -2).apply { topMargin = dp(20) })
        seek.setOnSeekBarChangeListener(object : SeekBar.OnSeekBarChangeListener {
            override fun onProgressChanged(s: SeekBar, p: Int, fromUser: Boolean) {
                if (fromUser) {
                    try { mp.seekTo(p) } catch (_: Exception) {}
                    refresh()
                }
            }
            override fun onStartTrackingTouch(s: SeekBar) {}
            override fun onStopTrackingTouch(s: SeekBar) {}
        })
        mp.setOnCompletionListener { refresh() }
        val t = object : Runnable {
            override fun run() {
                refresh()
                ui.postDelayed(this, 500)
            }
        }
        ticker = t
        ui.post(t)
        refresh()
        holder.addView(col, FrameLayout.LayoutParams(-1, -1))
    }

    // ───────── Video ─────────
    private fun showVideo() {
        val vv = VideoView(this)
        video = vv
        val mc = MediaController(this)
        mc.setAnchorView(holder)
        vv.setMediaController(mc)
        vv.setOnErrorListener { _, _, _ ->
            message(Loc.t("No se pudo mostrar la vista previa. Prueba «Abrir con…»."))
            true
        }
        vv.setVideoURI(uri)
        vv.setOnPreparedListener { it.isLooping = false; vv.start() }
        holder.addView(vv, FrameLayout.LayoutParams(-1, -1, Gravity.CENTER))
    }

    // ───────── Texto ─────────
    private fun showText() {
        val max = 256 * 1024
        val buf = ByteArray(max)
        var n = 0
        var truncated = false
        contentResolver.openInputStream(uri)?.use { s ->
            while (n < max) {
                val r = s.read(buf, n, max - n)
                if (r < 0) break
                n += r
            }
            if (n >= max && s.read() >= 0) truncated = true
        }
        var s = String(buf, 0, n, Charsets.UTF_8)
        if (truncated) s += "\n\n… " + Loc.t("(se muestra solo el inicio del archivo)")
        val tv = TextView(this).apply {
            text = s
            setTextIsSelectable(true)
            typeface = Typeface.MONOSPACE
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 12.5f)
            setTextColor(pal.text)
            setPadding(dp(14), dp(8), dp(14), dp(8))
        }
        holder.addView(ScrollView(this).apply { addView(tv) }, FrameLayout.LayoutParams(-1, -1))
    }
}

/** ImageView con zoom (pellizco) y desplazamiento, sin librerías. */
class ZoomImageView(ctx: Context) : ImageView(ctx) {
    private val m = Matrix()
    private val v = FloatArray(9)
    private var minScale = 1f
    private var lastX = 0f
    private var lastY = 0f
    private var dragging = false
    private val sd = ScaleGestureDetector(ctx, object : ScaleGestureDetector.SimpleOnScaleGestureListener() {
        override fun onScale(d: ScaleGestureDetector): Boolean {
            m.getValues(v)
            val cur = v[Matrix.MSCALE_X]
            val f = (cur * d.scaleFactor).coerceIn(minScale, minScale * 8f) / cur
            m.postScale(f, f, d.focusX, d.focusY)
            clamp()
            return true
        }
    })

    init {
        scaleType = ScaleType.MATRIX
        setBackgroundColor(Color.BLACK)
    }

    fun setBitmap(b: Bitmap) {
        setImageBitmap(b)
        fit()
    }

    override fun onSizeChanged(w: Int, h: Int, ow: Int, oh: Int) {
        super.onSizeChanged(w, h, ow, oh)
        fit()
    }

    private fun fit() {
        val d = drawable ?: return
        if (width == 0 || height == 0) return
        val dw = d.intrinsicWidth.toFloat()
        val dh = d.intrinsicHeight.toFloat()
        minScale = minOf(width / dw, height / dh)
        m.reset()
        m.postScale(minScale, minScale)
        m.postTranslate((width - dw * minScale) / 2, (height - dh * minScale) / 2)
        imageMatrix = m
    }

    private fun clamp() {
        val d = drawable ?: return
        val r = RectF(0f, 0f, d.intrinsicWidth.toFloat(), d.intrinsicHeight.toFloat())
        m.mapRect(r)
        var dx = 0f
        var dy = 0f
        if (r.width() <= width) dx = (width - r.width()) / 2 - r.left
        else if (r.left > 0) dx = -r.left
        else if (r.right < width) dx = width - r.right
        if (r.height() <= height) dy = (height - r.height()) / 2 - r.top
        else if (r.top > 0) dy = -r.top
        else if (r.bottom < height) dy = height - r.bottom
        m.postTranslate(dx, dy)
        imageMatrix = m
    }

    override fun onTouchEvent(e: MotionEvent): Boolean {
        sd.onTouchEvent(e)
        if (sd.isInProgress) {
            dragging = false
            return true
        }
        when (e.actionMasked) {
            MotionEvent.ACTION_DOWN -> {
                lastX = e.x; lastY = e.y; dragging = true
            }
            MotionEvent.ACTION_MOVE -> if (dragging && e.pointerCount == 1) {
                m.postTranslate(e.x - lastX, e.y - lastY)
                lastX = e.x; lastY = e.y
                clamp()
            }
            MotionEvent.ACTION_POINTER_UP -> {
                dragging = false
            }
            MotionEvent.ACTION_UP, MotionEvent.ACTION_CANCEL -> dragging = false
        }
        return true
    }
}
