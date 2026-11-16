using System.IO.Pipes;
using System.Text;

namespace TransferDood;

static class Program
{
    public static readonly string PipeName = "TransferDood." + Environment.UserName;
    const string MutexName = @"Local\TransferDood.Single";

    [STAThread]
    static int Main(string[] args)
    {
        // Modos auxiliares (sin interfaz)
        if (args.Contains("--setup-firewall")) return SystemIntegration.FirewallSetupElevated();
        if (args.Contains("--remove-firewall")) return SystemIntegration.FirewallRemoveElevated();
        if (args.Contains("--cleanup")) { SystemIntegration.Cleanup(); return 0; }

        var paths = ParseSendPaths(args);

        // Instancia única: si ya hay una abierta, le pasamos los archivos y salimos.
        using var mutex = new Mutex(true, MutexName, out bool first);
        if (!first)
        {
            Forward(paths);
            return 0;
        }

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.AddMessageFilter(new WheelFilter());

        var settings = Settings.Load();
        Loc.Init(settings.Language);
        Theme.Init();
        Theme.Apply(settings.Theme);

        bool startHidden = args.Contains("--tray") && paths.Count == 0;
        Application.Run(new TrayApp(settings, startHidden, paths));
        return 0;
    }

    static List<string> ParseSendPaths(string[] args)
    {
        var list = new List<string>();
        bool collecting = false;
        foreach (var a in args)
        {
            if (a == "--send") { collecting = true; continue; }
            if (a.StartsWith("--", StringComparison.Ordinal)) { collecting = false; continue; }
            if (!collecting) continue;
            try { list.Add(Path.GetFullPath(a)); } catch { }
        }
        return list;
    }

    static void Forward(List<string> paths)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            pipe.Connect(5000);
            var sb = new StringBuilder();
            if (paths.Count == 0) sb.Append("SHOW\n");
            else foreach (var p in paths) sb.Append("SEND\t").Append(p).Append('\n');
            var bytes = Encoding.UTF8.GetBytes(sb.ToString());
            pipe.Write(bytes, 0, bytes.Length);
            pipe.Flush();
        }
        catch { }
    }
}
