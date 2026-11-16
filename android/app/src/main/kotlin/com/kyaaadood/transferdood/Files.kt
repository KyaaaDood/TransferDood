package com.kyaaadood.transferdood

import android.content.Context
import android.net.Uri
import android.provider.DocumentsContract
import android.provider.OpenableColumns
import java.io.File

/** Utilidades para leer nombres/tamaños de lo que se va a enviar. */
object FilesUtil {

    /** Nombre y tamaño (-1 si el proveedor no lo dice) de un archivo elegido. */
    fun describe(ctx: Context, uri: Uri): Pair<String, Long> {
        if (uri.scheme == "file") {
            val f = File(uri.path ?: "")
            return safeName(f.name) to f.length()
        }
        var name: String? = null
        var size = -1L
        try {
            ctx.contentResolver.query(
                uri, arrayOf(OpenableColumns.DISPLAY_NAME, OpenableColumns.SIZE), null, null, null
            )?.use { c ->
                if (c.moveToFirst()) {
                    val ni = c.getColumnIndex(OpenableColumns.DISPLAY_NAME)
                    val si = c.getColumnIndex(OpenableColumns.SIZE)
                    if (ni >= 0 && !c.isNull(ni)) name = c.getString(ni)
                    if (si >= 0 && !c.isNull(si)) size = c.getLong(si)
                }
            }
        } catch (_: Exception) {
        }
        return safeName(name ?: uri.lastPathSegment ?: "archivo") to size
    }

    /** Recorre una carpeta elegida con el selector de carpetas y devuelve todos sus archivos. */
    fun walkTree(ctx: Context, tree: Uri): List<SendItem> {
        val out = ArrayList<SendItem>()
        val rootId = DocumentsContract.getTreeDocumentId(tree)
        val rootUri = DocumentsContract.buildDocumentUriUsingTree(tree, rootId)

        var rootName = Loc.t("Carpeta")
        try {
            ctx.contentResolver.query(
                rootUri, arrayOf(DocumentsContract.Document.COLUMN_DISPLAY_NAME), null, null, null
            )?.use { c ->
                if (c.moveToFirst() && !c.isNull(0)) rootName = safeName(c.getString(0))
            }
        } catch (_: Exception) {
        }

        walk(ctx, tree, rootId, rootName, out, 0)
        return out
    }

    private fun walk(ctx: Context, tree: Uri, parentId: String, prefix: String, out: MutableList<SendItem>, depth: Int) {
        if (depth > 40) return
        val children = DocumentsContract.buildChildDocumentsUriUsingTree(tree, parentId)
        val cols = arrayOf(
            DocumentsContract.Document.COLUMN_DOCUMENT_ID,
            DocumentsContract.Document.COLUMN_DISPLAY_NAME,
            DocumentsContract.Document.COLUMN_MIME_TYPE,
            DocumentsContract.Document.COLUMN_SIZE
        )
        val dirs = ArrayList<Pair<String, String>>()
        try {
            ctx.contentResolver.query(children, cols, null, null, null)?.use { c ->
                while (c.moveToNext()) {
                    val id = c.getString(0) ?: continue
                    val name = safeName(c.getString(1) ?: "archivo")
                    val mime = c.getString(2) ?: ""
                    val size = if (c.isNull(3)) -1L else c.getLong(3)
                    if (mime == DocumentsContract.Document.MIME_TYPE_DIR) {
                        dirs.add(id to name)
                    } else {
                        out.add(SendItem(DocumentsContract.buildDocumentUriUsingTree(tree, id), "$prefix/$name", size))
                    }
                }
            }
        } catch (_: Exception) {
        }
        for ((id, name) in dirs) walk(ctx, tree, id, "$prefix/$name", out, depth + 1)
    }

    /**
     * Si un proveedor no informa el tamaño, se intenta por el descriptor y, como último recurso,
     * se copia a la caché para conocerlo (el protocolo necesita el tamaño antes de enviar).
     */
    fun resolveSizes(ctx: Context, items: List<SendItem>, staged: MutableList<File>, cancelled: () -> Boolean) {
        for (it in items) {
            if (it.size >= 0) continue
            try {
                ctx.contentResolver.openAssetFileDescriptor(it.uri, "r")?.use { afd ->
                    if (afd.length >= 0) it.size = afd.length
                }
            } catch (_: Exception) {
            }
            if (it.size >= 0) continue

            val tmp = File.createTempFile("sd", ".tmp", ctx.cacheDir)
            staged.add(tmp)
            ctx.contentResolver.openInputStream(it.uri)?.use { input ->
                tmp.outputStream().use { o ->
                    val buf = ByteArray(64 * 1024)
                    while (true) {
                        if (cancelled()) throw InterruptedException()
                        val n = input.read(buf)
                        if (n < 0) break
                        o.write(buf, 0, n)
                    }
                }
            } ?: throw java.io.IOException(Loc.f("No se pudo leer: {0}", it.relName))
            it.uri = Uri.fromFile(tmp)
            it.size = tmp.length()
        }
    }

    fun safeName(raw: String): String {
        val t = raw.replace('/', '_').replace('\\', '_').filter { !it.isISOControl() }.trim()
        return t.ifEmpty { "archivo" }
    }
}
