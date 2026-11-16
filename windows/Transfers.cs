using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace TransferDood;

enum JobState { Waiting, Active, Done, Failed, Declined, Cancelled }

sealed class TransferJob
{
    long _done;
    int _state = (int)JobState.Waiting;

    public bool Incoming { get; init; }
    public string PeerName { get; set; } = "";
    public string Title { get; set; } = "";
    public int FileCount { get; set; }
    public long TotalBytes { get; set; }
    public string? Error { get; private set; }
    public string? SavedPath { get; set; }
    public DateTime ActiveSinceUtc { get; private set; }
    public CancellationTokenSource Cts { get; } = new();
    public string Kind { get; set; } = "files"; // files | text
    public string? Text { get; set; }
    public DateTime TimeUtc { get; set; } = DateTime.UtcNow;

    volatile bool _paused;
    public bool Paused => _paused;

    public void TogglePause()
    {
        if (State != JobState.Active) return;
        _paused = !_paused;
        TransferManager.RaiseChanged(this);
    }

    /// <summary>Espera mientras la transferencia está en pausa (se reanuda sola tras MaxPauseSec).</summary>
    public async Task WaitPauseAsync(CancellationToken ct)
    {
        if (!_paused) return;
        var start = DateTime.UtcNow;
        while (_paused && !ct.IsCancellationRequested)
        {
            if ((DateTime.UtcNow - start).TotalSeconds >= Protocol.MaxPauseSec)
            {
                _paused = false;
                TransferManager.RaiseChanged(this);
                break;
            }
            await Task.Delay(150, ct);
        }
    }

    public static TransferJob Restore(HistoryRecord r)
    {
        var j = new TransferJob
        {
            Incoming = r.Incoming, PeerName = r.PeerName, Title = r.Title, FileCount = r.FileCount,
            TotalBytes = r.TotalBytes, SavedPath = r.SavedPath, Kind = r.Kind ?? "files", Text = r.Text, TimeUtc = r.TimeUtc
        };
        j.Error = r.Error;
        j._state = r.State;
        j._done = r.State == (int)JobState.Done ? r.TotalBytes : 0;
        return j;
    }

    public HistoryRecord ToRecord() => new()
    {
        Incoming = Incoming, PeerName = PeerName, Title = Title, FileCount = FileCount, TotalBytes = TotalBytes,
        State = (int)State, Error = Error, SavedPath = SavedPath, Kind = Kind,
        Text = Text != null && Text.Length > 4000 ? Text[..4000] : Text, TimeUtc = TimeUtc
    };

    public long DoneBytes => Interlocked.Read(ref _done);
    public void Add(long n) => Interlocked.Add(ref _done, n);
    public JobState State => (JobState)Volatile.Read(ref _state);

    public bool IsFinished => State is JobState.Done or JobState.Failed or JobState.Declined or JobState.Cancelled;

    public double Speed
    {
        get
        {
            if (State != JobState.Active || ActiveSinceUtc == default) return 0;
            var secs = (DateTime.UtcNow - ActiveSinceUtc).TotalSeconds;
            return secs < 0.5 ? 0 : DoneBytes / secs;
        }
    }

    public void Set(JobState s, string? error = null)
    {
        if (error != null) Error = error;
        if (s == JobState.Active) ActiveSinceUtc = DateTime.UtcNow;
        Volatile.Write(ref _state, (int)s);
        TransferManager.RaiseChanged(this);
        if (IsFinished) TransferManager.RaiseFinished(this);
    }
}

static class TransferManager
{
    static readonly List<TransferJob> Jobs = new();
    static readonly object L = new();

    public static event Action<TransferJob>? Added;
    public static event Action<TransferJob>? Changed;
    public static event Action<TransferJob>? Finished;

    public static void Init()
    {
        lock (L) Jobs.AddRange(History.Load().Select(TransferJob.Restore));
    }

    public static TransferJob[] Snapshot()
    {
        lock (L) return Jobs.ToArray();
    }

    public static void Add(TransferJob j)
    {
        lock (L)
        {
            Jobs.Add(j);
            while (Jobs.Count > 60)
            {
                var old = Jobs.FirstOrDefault(x => x.IsFinished);
                if (old == null) break;
                Jobs.Remove(old);
            }
        }
        Added?.Invoke(j);
    }

    public static void ClearFinished()
    {
        lock (L) Jobs.RemoveAll(j => j.IsFinished);
        History.Save(Snapshot());
    }

    public static void RaiseChanged(TransferJob j) => Changed?.Invoke(j);
    public static void RaiseFinished(TransferJob j)
    {
        History.Save(Snapshot());
        Finished?.Invoke(j);
    }
}

sealed record SendItem(string FullPath, string RelName, long Size);

sealed class IncomingRequest
{
    public string FromId { get; init; } = "";
    public string Ip { get; init; } = "";
    public string From { get; init; } = "";
    public int FileCount { get; init; }
    public long Total { get; init; }
    public string FirstName { get; init; } = "";
    public string Kind { get; init; } = "files"; // files | text | pair
    public string Preview { get; init; } = "";   // texto recibido (vista previa)
    public string Code { get; init; } = "";      // código de emparejamiento
    public bool Initiator { get; init; }         // true si este equipo inició el emparejamiento
}

// ───────────────────────────── Envío ─────────────────────────────

static class TransferClient
{
    public static void Start(Peer peer, List<string> paths, Settings s)
    {
        var job = new TransferJob { Incoming = false, PeerName = peer.Name, Title = Loc.T("Preparando…") };
        TransferManager.Add(job);
        _ = Task.Run(() => RunAsync(job, peer, paths, null, s));
    }

    public static void StartText(Peer peer, string text, Settings s)
    {
        text = text.Length > Protocol.MaxTextChars ? text[..Protocol.MaxTextChars] : text;
        var job = new TransferJob { Incoming = false, PeerName = peer.Name, Title = Preview(text), Kind = "text", Text = text };
        TransferManager.Add(job);
        _ = Task.Run(() => RunAsync(job, peer, new List<string>(), text, s));
    }

    internal static string Preview(string text)
    {
        var t = new string(text.Select(c => char.IsControl(c) ? ' ' : c).ToArray()).Trim();
        return t.Length > 80 ? t[..80] + "…" : t;
    }

    internal static async Task<TcpClient> ConnectAsync(Peer peer, CancellationToken ct)
    {
        var client = new TcpClient(AddressFamily.InterNetwork) { NoDelay = true, SendBufferSize = 1 << 20 };
        try
        {
            using var cc = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cc.CancelAfter(TimeSpan.FromSeconds(6));
            await client.ConnectAsync(peer.Address, peer.Port, cc.Token);
            return client;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            client.Dispose();
            throw new IOException(Loc.T("No se pudo conectar con el equipo. Revisa que esté abierto y el firewall."));
        }
        catch (SocketException)
        {
            client.Dispose();
            throw new IOException(Loc.T("No se pudo conectar con el equipo. Revisa que esté abierto y el firewall."));
        }
    }

    internal static async Task WriteHeaderAsync(Stream ns, TransferHeader header, CancellationToken ct)
    {
        var hb = JsonSerializer.SerializeToUtf8Bytes(header);
        var pre = new byte[8];
        Protocol.Magic.CopyTo(pre, 0);
        BinaryPrimitives.WriteInt32BigEndian(pre.AsSpan(4, 4), hb.Length);
        await ns.WriteAsync(pre, ct);
        await ns.WriteAsync(hb, ct);
    }

    static async Task RunAsync(TransferJob job, Peer peer, List<string> paths, string? text, Settings s)
    {
        var ct = job.Cts.Token;
        try
        {
            var header = new TransferHeader { From = s.EffectiveName, Id = s.DeviceId };
            List<SendItem> items = new();
            if (text != null)
            {
                header.Kind = "text";
                header.Text = text;
                job.TotalBytes = 0;
            }
            else
            {
                items = BuildItems(paths);
                if (items.Count == 0)
                {
                    job.Set(JobState.Failed, Loc.T("No hay archivos para enviar."));
                    return;
                }
                job.FileCount = items.Count;
                job.TotalBytes = items.Sum(i => i.Size);
                job.Title = DescribeTitle(paths, items);
                TransferManager.RaiseChanged(job);
                header.Total = job.TotalBytes;
                header.Files = items.Select(i => new FileEntry { Name = i.RelName, Size = i.Size }).ToList();
            }
            Crypto.Sign(header, s, peer.Id);

            using var client = await ConnectAsync(peer, ct);
            using var ns = await SecureChannel.ClientAsync(client.GetStream(), s, peer.Id, ct);
            await WriteHeaderAsync(ns, header, ct);

            // Esperar a que el otro equipo acepte (hasta 75 s).
            var one = new byte[1];
            int got;
            using (var wc = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                wc.CancelAfter(TimeSpan.FromSeconds(75));
                try { got = await ns.ReadAsync(one, wc.Token); }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    throw new TimeoutException(Loc.T("El otro equipo no respondió."));
                }
            }
            if (got == 0) throw new IOException(Loc.T("El otro equipo cerró la conexión."));
            if (one[0] != 1)
            {
                job.Set(JobState.Declined);
                return;
            }

            job.Set(JobState.Active);

            var buf = new byte[Protocol.BufferSize];
            foreach (var item in items)
            {
                using var fs = new FileStream(item.FullPath, new FileStreamOptions
                {
                    Mode = FileMode.Open,
                    Access = FileAccess.Read,
                    Share = FileShare.ReadWrite | FileShare.Delete,
                    BufferSize = 1,
                    Options = FileOptions.Asynchronous | FileOptions.SequentialScan
                });

                long remaining = item.Size;
                while (remaining > 0)
                {
                    await job.WaitPauseAsync(ct);
                    int want = (int)Math.Min(buf.Length, remaining);
                    int n = await fs.ReadAsync(buf.AsMemory(0, want), ct);
                    if (n == 0) throw new IOException(Loc.T("El archivo cambió mientras se enviaba: ") + item.RelName);
                    await ns.WriteAsync(buf.AsMemory(0, n), ct);
                    remaining -= n;
                    job.Add(n);
                }
            }

            // Confirmación final del receptor.
            using (var ac = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                ac.CancelAfter(TimeSpan.FromSeconds(30));
                try { got = await ns.ReadAsync(one, ac.Token); }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    throw new TimeoutException(Loc.T("El otro equipo no confirmó la recepción."));
                }
            }
            if (got == 1 && one[0] == 1) job.Set(JobState.Done);
            else job.Set(JobState.Failed, Loc.T("El otro equipo no confirmó la recepción."));
        }
        catch (OperationCanceledException)
        {
            job.Set(JobState.Cancelled);
        }
        catch (Exception ex)
        {
            job.Set(JobState.Failed, Friendly(ex));
        }
    }

    static string DescribeTitle(List<string> paths, List<SendItem> items)
    {
        if (items.Count == 1) return Path.GetFileName(items[0].FullPath);
        if (paths.Count == 1 && Directory.Exists(paths[0]))
        {
            var name = new DirectoryInfo(paths[0]).Name;
            return Loc.F("{0} ({1} archivos)", name, items.Count);
        }
        return Loc.F("{0} archivos", items.Count);
    }

    public static List<SendItem> BuildItems(IEnumerable<string> paths)
    {
        var list = new List<SendItem>();
        var opts = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        };
        foreach (var p in paths)
        {
            try
            {
                if (File.Exists(p))
                {
                    var fi = new FileInfo(p);
                    list.Add(new SendItem(fi.FullName, fi.Name, fi.Length));
                }
                else if (Directory.Exists(p))
                {
                    var root = new DirectoryInfo(p);
                    var top = root.Name.TrimEnd('\\', ':');
                    if (top.Length == 0) top = Loc.T("Carpeta");
                    foreach (var f in root.EnumerateFiles("*", opts))
                    {
                        var rel = Path.GetRelativePath(root.FullName, f.FullName).Replace('\\', '/');
                        list.Add(new SendItem(f.FullName, top + "/" + rel, f.Length));
                    }
                }
            }
            catch { }
        }
        return list;
    }

    internal static string Friendly(Exception ex) => ex switch
    {
        TimeoutException => ex.Message,
        EndOfStreamException => Loc.T("La conexión se interrumpió."),
        UnauthorizedAccessException => Loc.T("No hay permiso para acceder al archivo o carpeta."),
        IOException io when io.InnerException is SocketException => Loc.T("La conexión se interrumpió."),
        _ => ex.Message
    };
}

// ───────────────────────────── Recepción ─────────────────────────────

sealed class TransferServer : IDisposable
{
    readonly Settings _s;
    readonly CancellationTokenSource _cts = new();
    TcpListener? _listener;

    /// <summary>Lo asigna la interfaz: pregunta al usuario si acepta. Devuelve true si acepta.</summary>
    public Func<IncomingRequest, Task<bool>>? Ask { get; set; }

    /// <summary>Se dispara al recibir un texto (remitente, texto).</summary>
    public event Action<string, string>? TextReceived;

    /// <summary>Se dispara al terminar un emparejamiento correcto.</summary>
    public event Action<string>? Paired;

    public TransferServer(Settings s) { _s = s; }

    public void Start()
    {
        _listener = new TcpListener(IPAddress.Any, Protocol.Port);
        _listener.Start(32);
        var ct = _cts.Token;
        _ = Task.Run(() => AcceptLoop(ct));
    }

    async Task AcceptLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var c = await _listener!.AcceptTcpClientAsync(ct);
                _ = Task.Run(() => HandleGuardedAsync(c, ct));
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (SocketException)
            {
                try { await Task.Delay(100, ct); }
                catch (OperationCanceledException) { break; }
            }
            catch { }
        }
    }

    // Límites contra abuso en la red local: pocas conexiones a la vez y pocos avisos pendientes por equipo.
    const int MaxConns = 16, MaxPendingPerIp = 3;
    int _conns;
    readonly Dictionary<string, int> _pendingByIp = new();

    bool TryEnterPrompt(string ip)
    {
        lock (_pendingByIp)
        {
            _pendingByIp.TryGetValue(ip, out var n);
            if (n >= MaxPendingPerIp) return false;
            _pendingByIp[ip] = n + 1;
            return true;
        }
    }

    void LeavePrompt(string ip)
    {
        lock (_pendingByIp)
        {
            if (_pendingByIp.TryGetValue(ip, out var n)) { if (n <= 1) _pendingByIp.Remove(ip); else _pendingByIp[ip] = n - 1; }
        }
    }

    async Task HandleGuardedAsync(TcpClient c, CancellationToken ct)
    {
        if (Interlocked.Increment(ref _conns) > MaxConns)
        {
            Interlocked.Decrement(ref _conns);
            try { c.Dispose(); } catch { }
            return;
        }
        try { await HandleAsync(c, ct); }
        finally { Interlocked.Decrement(ref _conns); }
    }

    async Task HandleAsync(TcpClient client, CancellationToken appCt)
    {
        TransferJob? job = null;
        FileStream? fs = null;
        string? partPath = null;

        void CleanupPartial()
        {
            try { fs?.Dispose(); } catch { }
            fs = null;
            try { if (partPath != null && File.Exists(partPath)) File.Delete(partPath); } catch { }
            partPath = null;
        }

        try
        {
            using (client)
            {
                client.NoDelay = true;
                client.ReceiveBufferSize = 1 << 20;
                var secure = await SecureChannel.ServerAsync(client.GetStream(), _s, appCt);
                if (secure == null) return;
                using var ns = secure;

                // 1) Cabecera
                TransferHeader? header;
                using (var hc = CancellationTokenSource.CreateLinkedTokenSource(appCt))
                {
                    hc.CancelAfter(TimeSpan.FromSeconds(10));
                    var pre = new byte[8];
                    await StreamUtil.ReadExactAsync(ns, pre, 8, hc.Token);
                    if (!pre.AsSpan(0, 4).SequenceEqual(Protocol.Magic)) return;
                    int len = BinaryPrimitives.ReadInt32BigEndian(pre.AsSpan(4, 4));
                    if (len <= 0 || len > Protocol.MaxHeaderBytes) return;
                    var hb = new byte[len];
                    await StreamUtil.ReadExactAsync(ns, hb, len, hc.Token);
                    header = JsonSerializer.Deserialize<TransferHeader>(hb);
                }
                if (header == null) return;
                header.Files ??= new();

                // Equipos bloqueados: se rechazan sin mostrar nada.
                if (_s.IsBlocked(header.Id))
                {
                    await ns.WriteAsync(new byte[] { 0 }, appCt);
                    return;
                }

                // No molestar / pantalla completa: se rechaza sin mostrar nada.
                if (_s.RefuseIncoming())
                {
                    await ns.WriteAsync(new byte[] { 0 }, appCt);
                    return;
                }

                if (header.Kind == "pair")
                {
                    await HandlePairAsync(ns, header, appCt, (client.Client.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? "");
                    return;
                }

                bool isText = header.Kind == "text";
                if (isText)
                {
                    if (string.IsNullOrEmpty(header.Text) || header.Text.Length > Protocol.MaxTextChars) return;
                    header.Files.Clear();
                }
                else if (header.Files.Count == 0 || header.Files.Count > 500_000) return;

                long total = 0;
                var parts = new List<string[]>(header.Files.Count);
                foreach (var f in header.Files)
                {
                    if (f.Size < 0) return;
                    total = checked(total + f.Size);
                    parts.Add(CleanParts(f.Name ?? ""));
                }

                string fromName = CleanLabel(header.From);
                bool allInFolder = parts.All(p => p.Length > 1) && parts.Select(p => p[0]).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1;
                string title = isText ? TransferClient.Preview(header.Text!)
                    : parts.Count == 1
                    ? parts[0][^1]
                    : allInFolder ? Loc.F("{0} ({1} archivos)", parts[0][0], parts.Count) : Loc.F("{0} archivos", parts.Count);

                job = new TransferJob
                {
                    Incoming = true,
                    PeerName = fromName,
                    Title = title,
                    FileCount = parts.Count,
                    TotalBytes = total,
                    Kind = isText ? "text" : "files",
                    Text = isText ? header.Text : null
                };
                TransferManager.Add(job);

                using var linked = CancellationTokenSource.CreateLinkedTokenSource(appCt, job.Cts.Token);
                var ct = linked.Token;

                // 2) Carpeta destino y espacio libre
                var folder = Path.GetFullPath(_s.EffectiveFolder);
                if (!isText) Directory.CreateDirectory(folder);
                if (!isText) try
                {
                    var root = Path.GetPathRoot(folder);
                    if (!string.IsNullOrEmpty(root) && new DriveInfo(root).AvailableFreeSpace < total + 16L * 1024 * 1024)
                    {
                        await ns.WriteAsync(new byte[] { 0 }, ct);
                        job.Set(JobState.Failed, Loc.T("No hay espacio suficiente en el disco."));
                        return;
                    }
                }
                catch (IOException) { throw; }
                catch (ArgumentException) { }

                // 3) Preguntar al usuario
                bool accepted = _s.AutoAccept || Crypto.VerifyTrusted(header, _s);
                if (!accepted && Ask != null)
                {
                    string ip = (client.Client.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? "";
                    if (!TryEnterPrompt(ip))
                    {
                        await ns.WriteAsync(new byte[] { 0 }, ct);
                        job.Set(JobState.Declined);
                        return;
                    }
                    try
                    {
                    var req = new IncomingRequest
                    {
                        FromId = header.Id ?? "", From = fromName, Ip = ip, FileCount = parts.Count, Total = isText ? header.Text!.Length : total,
                        FirstName = isText ? "" : parts[0][^1], Kind = isText ? "text" : "files",
                        Preview = isText ? TransferClient.Preview(header.Text!) : ""
                    };
                    var task = Ask(req);
                    var first = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(65), ct));
                    accepted = first == task && task.Result;
                    }
                    finally { LeavePrompt(ip); }
                }

                await ns.WriteAsync(new byte[] { (byte)(accepted ? 1 : 0) }, ct);
                if (!accepted)
                {
                    job.Set(JobState.Declined);
                    return;
                }

                if (isText)
                {
                    try { TextReceived?.Invoke(fromName, header.Text!); } catch { }
                    await ns.WriteAsync(new byte[] { 1 }, ct);
                    job.Set(JobState.Done);
                    return;
                }

                job.Set(JobState.Active);

                // 4) Datos
                var buf = new byte[Protocol.BufferSize];
                var topMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                string? lastFinal = null;

                for (int i = 0; i < header.Files.Count; i++)
                {
                    var entry = header.Files[i];
                    var p = parts[i];

                    string dir = folder;
                    if (p.Length > 1)
                    {
                        if (!topMap.TryGetValue(p[0], out var topName))
                        {
                            topName = UniqueDir(folder, p[0]);
                            topMap[p[0]] = topName;
                        }
                        dir = Path.Combine(folder, topName);
                        for (int k = 1; k < p.Length - 1; k++) dir = Path.Combine(dir, p[k]);
                    }

                    var finalPath = UniqueFile(Path.Combine(dir, p[^1]));
                    if (!Path.GetFullPath(finalPath).StartsWith(folder.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                        throw new IOException(Loc.T("Ruta de destino no válida."));
                    Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);

                    partPath = finalPath + ".sdpart";
                    fs = new FileStream(partPath, new FileStreamOptions
                    {
                        Mode = FileMode.Create,
                        Access = FileAccess.Write,
                        Share = FileShare.None,
                        BufferSize = 1,
                        Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
                        PreallocationSize = entry.Size
                    });

                    long remaining = entry.Size;
                    while (remaining > 0)
                    {
                        await job.WaitPauseAsync(ct);
                        int want = (int)Math.Min(buf.Length, remaining);
                        int n;
                        using (var rc = CancellationTokenSource.CreateLinkedTokenSource(ct))
                        {
                            rc.CancelAfter(TimeSpan.FromSeconds(Protocol.DataTimeoutSec));
                            try { n = await ns.ReadAsync(buf.AsMemory(0, want), rc.Token); }
                            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                            {
                                throw new TimeoutException(Loc.T("El otro equipo dejó de enviar datos."));
                            }
                        }
                        if (n == 0) throw new EndOfStreamException(Loc.T("La conexión se cerró antes de terminar."));
                        await fs.WriteAsync(buf.AsMemory(0, n), ct);
                        remaining -= n;
                        job.Add(n);
                    }

                    await fs.DisposeAsync();
                    fs = null;
                    File.Move(partPath, finalPath, overwrite: false);
                    partPath = null;
                    lastFinal = finalPath;
                }

                job.SavedPath = parts.Count == 1 ? lastFinal
                    : topMap.Count == 1 && allInFolder ? Path.Combine(folder, topMap.Values.First())
                    : folder;

                await ns.WriteAsync(new byte[] { 1 }, ct);
                job.Set(JobState.Done);
            }
        }
        catch (OperationCanceledException)
        {
            CleanupPartial();
            job?.Set(JobState.Cancelled);
        }
        catch (Exception ex)
        {
            CleanupPartial();
            job?.Set(JobState.Failed, TransferClient.Friendly(ex));
        }
    }

    async Task HandlePairAsync(Stream ns, TransferHeader header, CancellationToken ct, string ip)
    {
        if (string.IsNullOrEmpty(header.Pub) || string.IsNullOrEmpty(header.Id) || Ask == null) return;
        if (!TryEnterPrompt(ip)) return;
        try { await HandlePairCoreAsync(ns, header, ct, ip); }
        finally { LeavePrompt(ip); }
    }

    async Task HandlePairCoreAsync(Stream ns, TransferHeader header, CancellationToken ct, string ip)
    {
        var peerPub = Convert.FromBase64String(header.Pub);
        if (peerPub.Length > 4096) return;

        using var ecdh = System.Security.Cryptography.ECDiffieHellman.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
        var myPub = ecdh.ExportSubjectPublicKeyInfo();
        string code = Crypto.Sas(peerPub, myPub);

        var reply = new byte[1 + 4 + myPub.Length];
        reply[0] = 3;
        BinaryPrimitives.WriteInt32BigEndian(reply.AsSpan(1, 4), myPub.Length);
        myPub.CopyTo(reply, 5);
        await ns.WriteAsync(reply, ct);

        string name = CleanLabel(header.From);
        var userTask = Ask(new IncomingRequest { Kind = "pair", FromId = header.Id!, From = name, Code = code, Ip = ip });

        var one = new byte[1];
        using var rc = CancellationTokenSource.CreateLinkedTokenSource(ct);
        rc.CancelAfter(TimeSpan.FromSeconds(70));
        await StreamUtil.ReadExactAsync(ns, one, 1, rc.Token);
        bool theirOk = one[0] == 1;

        var first = await Task.WhenAny(userTask, Task.Delay(TimeSpan.FromSeconds(65), ct));
        bool ok = theirOk && first == userTask && userTask.Result;

        await ns.WriteAsync(new byte[] { (byte)(ok ? 1 : 0) }, ct);
        if (ok)
        {
            _s.Pair(header.Id, name, Crypto.Derive(ecdh, peerPub));
            try { Paired?.Invoke(name); } catch { }
        }
    }

    // ── utilidades de nombres ──

    static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    static string[] CleanParts(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var parts = new List<string>();
        foreach (var raw in name.Split('/', '\\'))
        {
            var p = new string(raw.Where(c => c >= 32 && Array.IndexOf(invalid, c) < 0).ToArray()).Trim().TrimEnd('.', ' ');
            if (p.Length == 0 || p == "." || p == "..") continue;
            if (p.Length > 150) p = p[..150];
            var stem = p.Split('.')[0];
            if (Reserved.Contains(stem)) p = "_" + p;
            parts.Add(p);
        }
        if (parts.Count == 0) parts.Add("archivo");
        return parts.ToArray();
    }

    static string CleanLabel(string? s)
    {
        if (string.IsNullOrEmpty(s)) return Loc.T("Otro equipo");
        var t = new string(s.Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (t.Length > 48) t = t[..48];
        return t.Length == 0 ? Loc.T("Otro equipo") : t;
    }

    static string UniqueFile(string path)
    {
        if (!File.Exists(path) && !File.Exists(path + ".sdpart")) return path;
        var dir = Path.GetDirectoryName(path)!;
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        for (int i = 1; i < 10000; i++)
        {
            var c = Path.Combine(dir, $"{name} ({i}){ext}");
            if (!File.Exists(c) && !File.Exists(c + ".sdpart")) return c;
        }
        return Path.Combine(dir, $"{name}-{Guid.NewGuid():N}{ext}");
    }

    static string UniqueDir(string root, string name)
    {
        if (!Directory.Exists(Path.Combine(root, name)) && !File.Exists(Path.Combine(root, name))) return name;
        for (int i = 1; i < 10000; i++)
        {
            var c = $"{name} ({i})";
            if (!Directory.Exists(Path.Combine(root, c)) && !File.Exists(Path.Combine(root, c))) return c;
        }
        return $"{name}-{Guid.NewGuid():N}";
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _listener?.Stop(); } catch { }
    }
}
