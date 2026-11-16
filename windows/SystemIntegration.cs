using System.Diagnostics;
using Microsoft.Win32;

namespace TransferDood;

/// <summary>Inicio con Windows, menú contextual del Explorador y regla de firewall.</summary>
static class SystemIntegration
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string RunValue = "TransferDood";
    const string RuleName = "TransferDood";

    public static string ExePath => Environment.ProcessPath ?? "";

    // ── Inicio con Windows ──

    public static void SetAutostart(bool on)
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey, writable: true) ?? Registry.CurrentUser.CreateSubKey(RunKey);
            k.DeleteValue("SendDood", throwOnMissingValue: false);   // nombre anterior de la app
            if (on) k.SetValue(RunValue, $"\"{ExePath}\" --tray");
            else k.DeleteValue(RunValue, throwOnMissingValue: false);
        }
        catch { }
    }

    // ── Menú contextual (clic derecho en archivos y carpetas) ──

    static readonly string[] MenuRoots =
    {
        @"Software\Classes\*\shell\TransferDood",
        @"Software\Classes\Directory\shell\TransferDood"
    };

    public static void SetContextMenu(bool on)
    {
        // Nombre anterior de la app (SendDood): se limpia para no dejar entradas duplicadas.
        foreach (var old in new[] { @"Software\Classes\*\shell\SendDood", @"Software\Classes\Directory\shell\SendDood" })
        {
            try { Registry.CurrentUser.DeleteSubKeyTree(old, throwOnMissingSubKey: false); } catch { }
        }
        foreach (var root in MenuRoots)
        {
            try
            {
                if (on)
                {
                    using var k = Registry.CurrentUser.CreateSubKey(root);
                    k.SetValue("", Loc.T("Enviar con TransferDood"));
                    k.SetValue("Icon", $"\"{ExePath}\",0");
                    k.SetValue("MultiSelectModel", "Player");
                    using var c = k.CreateSubKey("command");
                    c.SetValue("", $"\"{ExePath}\" --send \"%1\"");
                }
                else
                {
                    Registry.CurrentUser.DeleteSubKeyTree(root, throwOnMissingSubKey: false);
                }
            }
            catch { }
        }
    }

    // ── Firewall ──

    static (int code, string output) RunNetsh(string args)
    {
        try
        {
            var psi = new ProcessStartInfo("netsh.exe", args)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var p = Process.Start(psi);
            if (p == null) return (-1, "");
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(10000);
            return (p.ExitCode, output);
        }
        catch { return (-1, ""); }
    }

    public static bool FirewallOk()
    {
        var (code, output) = RunNetsh($"advfirewall firewall show rule name=\"{RuleName}\" verbose");
        return code == 0 && output.Contains(ExePath, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Comprueba la regla y, si falta, abre una sola vez el aviso de administrador (UAC) para crearla.
    /// </summary>
    public static bool EnsureFirewall(bool force)
    {
        if (!force && FirewallOk()) return true;
        RunElevated("--setup-firewall");
        return FirewallOk();
    }

    static void RunElevated(string arg)
    {
        try
        {
            var psi = new ProcessStartInfo(ExePath, arg)
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            };
            using var p = Process.Start(psi);
            p?.WaitForExit(30000);
        }
        catch { } // el usuario canceló el UAC
    }

    /// <summary>Se ejecuta elevado (--setup-firewall): limpia reglas viejas/bloqueos y crea la regla de entrada.</summary>
    public static int FirewallSetupElevated()
    {
        var exe = ExePath;
        // Borra TODAS las reglas de este programa (incluidas las de "Bloquear" que Windows crea si se cancela su aviso).
        RunNetsh($"advfirewall firewall delete rule name=all program=\"{exe}\"");
        RunNetsh("advfirewall firewall delete rule name=\"SendDood\"");   // regla del nombre anterior
        RunNetsh($"advfirewall firewall delete rule name=\"{RuleName}\"");
        var (code, _) = RunNetsh(
            $"advfirewall firewall add rule name=\"{RuleName}\" dir=in action=allow program=\"{exe}\" " +
            "enable=yes profile=any remoteip=localsubnet description=\"TransferDood: recibir archivos en la red local\"");
        return code;
    }

    public static int FirewallRemoveElevated()
    {
        var exe = ExePath;
        RunNetsh($"advfirewall firewall delete rule name=all program=\"{exe}\"");
        var (code, _) = RunNetsh($"advfirewall firewall delete rule name=\"{RuleName}\"");
        return code;
    }

    /// <summary>--cleanup: deshace todo lo que la app registró en el sistema.</summary>
    public static void Cleanup()
    {
        SetAutostart(false);
        SetContextMenu(false);
        RunElevated("--remove-firewall");
    }
}
