package com.kyaaadood.transferdood

import android.app.Activity
import android.content.ClipData
import android.content.Context
import android.content.Intent
import android.net.Uri
import android.os.Build
import android.widget.Toast

/** Ayudas para ver, compartir y abrir con otra app un archivo recibido. */
object Viewer {
    private val image = setOf("jpg", "jpeg", "png", "gif", "webp", "bmp", "heic", "heif")
    private val audio = setOf("mp3", "m4a", "aac", "wav", "ogg", "oga", "opus", "flac", "amr", "mid", "midi")
    private val video = setOf("mp4", "m4v", "3gp", "webm", "mkv", "mov")
    private val text = setOf(
        "txt", "log", "md", "csv", "json", "xml", "ini", "conf", "cfg", "yml", "yaml", "html", "htm", "css", "js",
        "ts", "java", "kt", "py", "cs", "c", "h", "cpp", "bat", "sh", "ps1", "sql", "srt", "tsv", "properties"
    )

    /** Tipo de vista previa posible: image, pdf, audio, video, text; o null si no se puede previsualizar. */
    fun kindOf(name: String): String? {
        val ext = name.substringAfterLast('.', "").lowercase()
        return when {
            ext in image -> "image"
            ext == "pdf" -> "pdf"
            ext in audio -> "audio"
            ext in video -> "video"
            ext in text -> "text"
            else -> null
        }
    }

    private fun relaxFileUri(uri: Uri) {
        // Android 7-9 con archivo normal (file://): se permite entregarlo a otra app.
        if (uri.scheme == "file" && Build.VERSION.SDK_INT in 24..28) {
            try {
                android.os.StrictMode.setVmPolicy(android.os.StrictMode.VmPolicy.Builder().build())
            } catch (_: Exception) {
            }
        }
    }

    fun share(ctx: Context, uri: Uri, name: String) {
        try {
            relaxFileUri(uri)
            val i = Intent(Intent.ACTION_SEND)
                .setType(Storage.mimeOf(name))
                .putExtra(Intent.EXTRA_STREAM, uri)
                .putExtra(Intent.EXTRA_SUBJECT, name)
                .addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
            i.clipData = ClipData.newRawUri(name, uri)
            val chooser = Intent.createChooser(i, Loc.t("Compartir"))
            if (ctx !is Activity) chooser.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
            ctx.startActivity(chooser)
        } catch (_: Exception) {
            Toast.makeText(ctx, Loc.t("No se pudo compartir el archivo."), Toast.LENGTH_LONG).show()
        }
    }

    /** Abre el archivo con otra app (con selector, para poder elegir cuál). */
    fun openWith(ctx: Context, uri: Uri, name: String) {
        try {
            relaxFileUri(uri)
            val i = Intent(Intent.ACTION_VIEW)
                .setDataAndType(uri, Storage.mimeOf(name))
                .addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
            i.clipData = ClipData.newRawUri(name, uri)
            val chooser = Intent.createChooser(i, Loc.t("Abrir con…"))
            if (ctx !is Activity) chooser.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
            ctx.startActivity(chooser)
        } catch (_: Exception) {
            Toast.makeText(ctx, Loc.t("No se encontró una app para abrir este archivo."), Toast.LENGTH_LONG).show()
        }
    }
}
