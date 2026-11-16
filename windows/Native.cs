using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace TransferDood;

static class Native
{
    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    [DllImport("kernel32.dll")]
    static extern bool SetProcessWorkingSetSize(IntPtr process, IntPtr min, IntPtr max);

    /// <summary>Barra de título oscura/clara según el tema (Windows 10 20H1+ y Windows 11).</summary>
    public static void DarkTitleBar(IntPtr handle, bool dark)
    {
        try
        {
            int v = dark ? 1 : 0;
            if (DwmSetWindowAttribute(handle, 20, ref v, sizeof(int)) != 0)
                DwmSetWindowAttribute(handle, 19, ref v, sizeof(int));
        }
        catch { }
    }

    /// <summary>Esquinas redondeadas y borde de color en ventanas sin marco (Windows 11; en Windows 10 se ignora).</summary>
    public static void RoundCorners(IntPtr handle, Color border)
    {
        try
        {
            int round = 2; // DWMWCP_ROUND
            DwmSetWindowAttribute(handle, 33, ref round, sizeof(int));
            int bgr = border.R | (border.G << 8) | (border.B << 16);
            DwmSetWindowAttribute(handle, 34, ref bgr, sizeof(int));
        }
        catch { }
    }

    [DllImport("shell32.dll")]
    static extern int SHQueryUserNotificationState(out int state);

    /// <summary>True si hay una app a pantalla completa (juego, video, presentación) en primer plano.</summary>
    public static bool FullscreenBusy()
    {
        try { return SHQueryUserNotificationState(out var s) == 0 && (s == 2 || s == 3 || s == 4); }
        catch { return false; }
    }

    /// <summary>Libera memoria: recolecta basura y reduce el working set del proceso.</summary>
    public static void TrimMemory()
    {
        try
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            using var p = Process.GetCurrentProcess();
            SetProcessWorkingSetSize(p.Handle, (IntPtr)(-1), (IntPtr)(-1));
        }
        catch { }
    }

    /// <summary>Abre el Explorador mostrando un archivo (seleccionado) o una carpeta.</summary>
    public static void Reveal(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            if (File.Exists(path))
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            else if (Directory.Exists(path))
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        catch { }
    }
}

/// <summary>Protege secretos con DPAPI (ligado al usuario de Windows): no quedan en claro en settings.json.</summary>
static class Dpapi
{
    const string Prefix = "dpapi:";

    [StructLayout(LayoutKind.Sequential)]
    struct DataBlob { public int cbData; public IntPtr pbData; }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool CryptProtectData(ref DataBlob pIn, string? desc, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out DataBlob pOut);

    [DllImport("crypt32.dll", SetLastError = true)]
    static extern bool CryptUnprotectData(ref DataBlob pIn, IntPtr desc, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out DataBlob pOut);

    [DllImport("kernel32.dll")]
    static extern IntPtr LocalFree(IntPtr h);

    static byte[]? Run(byte[] data, bool protect)
    {
        var input = new DataBlob { cbData = data.Length, pbData = Marshal.AllocHGlobal(data.Length) };
        try
        {
            Marshal.Copy(data, 0, input.pbData, data.Length);
            DataBlob output;
            bool ok = protect
                ? CryptProtectData(ref input, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output)
                : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
            if (!ok) return null;
            try
            {
                var res = new byte[output.cbData];
                Marshal.Copy(output.pbData, res, 0, output.cbData);
                return res;
            }
            finally { LocalFree(output.pbData); }
        }
        catch { return null; }
        finally { Marshal.FreeHGlobal(input.pbData); }
    }

    /// <summary>Texto listo para guardar: "dpapi:" + base64. Si DPAPI falla, se guarda como antes (base64).</summary>
    public static string Wrap(byte[] secret)
    {
        var p = Run(secret, true);
        return p != null ? Prefix + Convert.ToBase64String(p) : Convert.ToBase64String(secret);
    }

    public static byte[]? Unwrap(string stored)
    {
        try
        {
            if (!stored.StartsWith(Prefix)) return Convert.FromBase64String(stored);
            return Run(Convert.FromBase64String(stored[Prefix.Length..]), false);
        }
        catch { return null; }
    }

    public static bool IsWrapped(string stored) => stored.StartsWith(Prefix);
}
