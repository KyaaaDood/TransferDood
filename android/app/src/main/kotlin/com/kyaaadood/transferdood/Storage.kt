package com.kyaaadood.transferdood

import android.content.ContentValues
import android.content.Context
import android.net.Uri
import android.os.Build
import android.os.Environment
import android.provider.DocumentsContract
import android.provider.MediaStore
import android.webkit.MimeTypeMap
import java.io.File
import java.io.IOException
import java.io.OutputStream

/** Un archivo que se está escribiendo en la carpeta de recibidos. */
interface Output {
    val stream: OutputStream

    /** Cierra y deja el archivo visible. Devuelve su Uri si se conoce. */
    fun commit(): Uri?

    /** Cierra y borra lo que se haya escrito. */
    fun abort()
}

/**
 * Dónde se guardan los archivos recibidos:
 *  - Si el usuario eligió una carpeta (selector de carpetas de Android) -> esa.
 *  - Android 10+  -> Descargas/TransferDood (MediaStore, sin permisos).
 *  - Android 7-9  -> Descargas/TransferDood (archivo normal).
 */
object Storage {

    fun create(ctx: Context, dirParts: List<String>, name: String): Output {
        val tree = Core.prefs.receiveTree
        return try {
            when {
                tree != null -> SafOutput(ctx, Uri.parse(tree), dirParts, name)
                Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q -> MediaStoreOutput(ctx, dirParts, name)
                else -> LegacyOutput(dirParts, name)
            }
        } catch (e: SecurityException) {
            throw IOException(Loc.t("No hay acceso a la carpeta de recibidos. Elígela de nuevo en Ajustes."))
        }
    }

    fun freeSpace(): Long = try {
        android.os.StatFs(Environment.getExternalStorageDirectory().path).availableBytes
    } catch (_: Exception) {
        Long.MAX_VALUE
    }

    fun mimeOf(name: String): String {
        val ext = name.substringAfterLast('.', "").lowercase()
        if (ext.isEmpty()) return "application/octet-stream"
        return MimeTypeMap.getSingleton().getMimeTypeFromExtension(ext) ?: "application/octet-stream"
    }

    /** Texto para mostrar la carpeta de recibidos en Ajustes. */
    fun describeFolder(): String {
        val t = Core.prefs.receiveTree ?: return "Descargas/TransferDood"
        return try {
            val id = DocumentsContract.getTreeDocumentId(Uri.parse(t))
            val shown = id.substringAfter(':', id).ifEmpty { Loc.t("Almacenamiento interno") }
            if (id.startsWith("primary:")) shown else id
        } catch (_: Exception) {
            Loc.t("Carpeta elegida")
        }
    }

    private fun sanitize(part: String): String {
        val bad = "\\/:*?\"<>|"
        var p = part.filter { it.code >= 32 && it !in bad }.trim().trimEnd('.', ' ')
        if (p == "." || p == "..") p = ""
        return p.take(150)
    }

    fun cleanParts(name: String): List<String> {
        val parts = name.split('/', '\\').map { sanitize(it) }.filter { it.isNotEmpty() }
        return if (parts.isEmpty()) listOf("archivo") else parts
    }

    // ── Android 10+: MediaStore ──

    @android.annotation.TargetApi(Build.VERSION_CODES.Q)
    private class MediaStoreOutput(ctx: Context, dirParts: List<String>, name: String) : Output {
        private val resolver = ctx.contentResolver
        private val uri: Uri

        override val stream: OutputStream

        init {
            val rel = "Download/TransferDood" + dirParts.joinToString("") { "/$it" }
            val v = ContentValues().apply {
                put(MediaStore.MediaColumns.DISPLAY_NAME, name)
                put(MediaStore.MediaColumns.MIME_TYPE, mimeOf(name))
                put(MediaStore.MediaColumns.RELATIVE_PATH, rel)
                put(MediaStore.MediaColumns.IS_PENDING, 1)
            }
            uri = resolver.insert(MediaStore.Downloads.EXTERNAL_CONTENT_URI, v)
                ?: throw IOException(Loc.t("No se pudo crear el archivo en Descargas."))
            stream = try {
                resolver.openOutputStream(uri) ?: throw IOException(Loc.t("No se pudo abrir el archivo para escribir."))
            } catch (e: Exception) {
                try {
                    resolver.delete(uri, null, null)
                } catch (_: Exception) {
                }
                throw e
            }
        }

        override fun commit(): Uri? {
            stream.flush()
            stream.close()
            val v = ContentValues().apply { put(MediaStore.MediaColumns.IS_PENDING, 0) }
            resolver.update(uri, v, null, null)
            return uri
        }

        override fun abort() {
            try {
                stream.close()
            } catch (_: Exception) {
            }
            try {
                resolver.delete(uri, null, null)
            } catch (_: Exception) {
            }
        }
    }

    // ── Android 7-9: archivo normal ──

    private class LegacyOutput(dirParts: List<String>, name: String) : Output {
        private val target: File
        private val part: File
        override val stream: OutputStream

        init {
            var dir = File(Environment.getExternalStoragePublicDirectory(Environment.DIRECTORY_DOWNLOADS), "TransferDood")
            for (p in dirParts) dir = File(dir, p)
            if (!dir.isDirectory && !dir.mkdirs()) throw IOException(Loc.t("No se pudo crear la carpeta de recibidos."))

            var f = File(dir, name)
            var i = 1
            val stem = name.substringBeforeLast('.', name)
            val ext = if (name.contains('.')) "." + name.substringAfterLast('.') else ""
            while (f.exists() || File(dir, f.name + ".sdpart").exists()) {
                f = File(dir, "$stem ($i)$ext")
                i++
            }
            target = f
            part = File(dir, f.name + ".sdpart")
            stream = part.outputStream().buffered(64 * 1024)
        }

        override fun commit(): Uri? {
            stream.flush()
            stream.close()
            if (!part.renameTo(target)) throw IOException(Loc.t("No se pudo terminar de guardar el archivo."))
            return null
        }

        override fun abort() {
            try {
                stream.close()
            } catch (_: Exception) {
            }
            part.delete()
        }
    }

    // ── Carpeta elegida por el usuario (SAF) ──

    private class SafOutput(ctx: Context, private val tree: Uri, dirParts: List<String>, name: String) : Output {
        private val resolver = ctx.contentResolver
        private val doc: Uri
        override val stream: OutputStream

        init {
            var parent = DocumentsContract.buildDocumentUriUsingTree(tree, DocumentsContract.getTreeDocumentId(tree))
            for (p in dirParts) {
                parent = findChild(parent, p)
                    ?: DocumentsContract.createDocument(resolver, parent, DocumentsContract.Document.MIME_TYPE_DIR, p)
                    ?: throw IOException(Loc.f("No se pudo crear la carpeta \"{0}\".", p))
            }

            var finalName = name
            var i = 1
            val stem = name.substringBeforeLast('.', name)
            val ext = if (name.contains('.')) "." + name.substringAfterLast('.') else ""
            while (findChild(parent, finalName) != null) {
                finalName = "$stem ($i)$ext"
                i++
            }

            doc = DocumentsContract.createDocument(resolver, parent, mimeOf(finalName), finalName)
                ?: throw IOException(Loc.t("No se pudo crear el archivo en la carpeta elegida."))
            stream = try {
                resolver.openOutputStream(doc) ?: throw IOException(Loc.t("No se pudo abrir el archivo para escribir."))
            } catch (e: Exception) {
                try {
                    DocumentsContract.deleteDocument(resolver, doc)
                } catch (_: Exception) {
                }
                throw e
            }
        }

        private fun findChild(parent: Uri, name: String): Uri? {
            val parentId = DocumentsContract.getDocumentId(parent)
            val children = DocumentsContract.buildChildDocumentsUriUsingTree(tree, parentId)
            try {
                resolver.query(
                    children,
                    arrayOf(
                        DocumentsContract.Document.COLUMN_DOCUMENT_ID,
                        DocumentsContract.Document.COLUMN_DISPLAY_NAME
                    ),
                    null, null, null
                )?.use { c ->
                    while (c.moveToNext()) {
                        if (c.getString(1) == name) {
                            return DocumentsContract.buildDocumentUriUsingTree(tree, c.getString(0))
                        }
                    }
                }
            } catch (_: Exception) {
            }
            return null
        }

        override fun commit(): Uri? {
            stream.flush()
            stream.close()
            return doc
        }

        override fun abort() {
            try {
                stream.close()
            } catch (_: Exception) {
            }
            try {
                DocumentsContract.deleteDocument(resolver, doc)
            } catch (_: Exception) {
            }
        }
    }
}
