using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace TransferDood;

/// <summary>
/// Descubrimiento de dispositivos por broadcast UDP en la red local.
/// Cada equipo anuncia "hello" cada pocos segundos y responde a "query".
/// </summary>
sealed class Discovery : IDisposable
{
    readonly Settings _s;
    readonly object _lock = new();
    readonly Dictionary<string, Peer> _peers = new();
    readonly CancellationTokenSource _cts = new();
    UdpClient? _udp;

    // Ahorro de energía: con la ventana abierta se anuncia cada 4 s; en la bandeja, cada 30 s
    // (los demás nos recuerdan más tiempo gracias al campo "ttl"). Al abrir la ventana se pregunta de inmediato.
    volatile int _intervalMs = 30_000;
    volatile int _ttl = 75;
    readonly SemaphoreSlim _wake = new(0);

    public void SetActive(bool active)
    {
        int interval = active ? 4_000 : 30_000;
        if (interval == _intervalMs) return;
        _intervalMs = interval;
        _ttl = active ? 15 : 75;
        try { _wake.Release(); } catch (SemaphoreFullException) { }
        if (active) Refresh();
    }

    public event Action? PeersChanged;

    public Discovery(Settings s) { _s = s; }

    public void Start()
    {
        var udp = new UdpClient(AddressFamily.InterNetwork);
        udp.ExclusiveAddressUse = false;
        udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        udp.Client.Bind(new IPEndPoint(IPAddress.Any, Protocol.Port));
        udp.EnableBroadcast = true;
        try { udp.Client.IOControl(-1744830452, new byte[] { 0, 0, 0, 0 }, null); } catch { } // SIO_UDP_CONNRESET
        _udp = udp;

        var ct = _cts.Token;
        _ = Task.Run(() => ReceiveLoop(ct));
        _ = Task.Run(() => AnnounceLoop(ct));
    }

    public List<Peer> Snapshot()
    {
        lock (_lock) return _peers.Values.Select(p => p.Clone()).ToList();
    }

    /// <summary>Pregunta a todos quién está y anuncia este equipo.</summary>
    public void Refresh() => Task.Run(() => { Send("query"); Send("hello"); QueryManual(); });

    /// <summary>Busca un equipo por IP (para redes donde el broadcast no llega).</summary>
    public void AddManual(IPAddress ip)
    {
        _s.AddManual(ip.ToString());
        Task.Run(() => Send("query", new IPEndPoint(ip, Protocol.Port)));
    }

    void QueryManual()
    {
        foreach (var t in _s.ManualSnapshot())
            if (IPAddress.TryParse(t, out var ip)) Send("query", new IPEndPoint(ip, Protocol.Port));
    }

    /// <summary>Anuncia de inmediato (por ejemplo, tras cambiar el nombre).</summary>
    public void Announce() => Task.Run(() => Send("hello"));

    async Task AnnounceLoop(CancellationToken ct)
    {
        Send("query");
        while (!ct.IsCancellationRequested)
        {
            try
            {
                Send("hello");
                QueryManual();
                Expire();
                await _wake.WaitAsync(_intervalMs, ct);
            }
            catch (OperationCanceledException) { break; }
            catch { }
        }
    }

    async Task ReceiveLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var r = await _udp!.ReceiveAsync(ct);
                Handle(r.Buffer, r.RemoteEndPoint);
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (SocketException)
            {
                try { await Task.Delay(200, ct); }
                catch (OperationCanceledException) { break; }
            }
            catch { }
        }
    }

    void Handle(byte[] data, IPEndPoint remote)
    {
        Hello? h;
        try { h = JsonSerializer.Deserialize<Hello>(data); }
        catch { return; }

        if (h == null || h.V != Protocol.Version || string.IsNullOrEmpty(h.Id) || h.Id.Length > 64) return;
        if (h.Id == _s.DeviceId) return;

        if (h.T == "bye")
        {
            bool removed;
            lock (_lock) removed = _peers.Remove(h.Id);
            if (removed) PeersChanged?.Invoke();
            return;
        }
        if (h.T != "hello" && h.T != "query") return;

        var addr = remote.Address;
        if (addr.IsIPv4MappedToIPv6) addr = addr.MapToIPv4();
        int port = (h.Port > 0 && h.Port < 65536) ? h.Port : Protocol.Port;
        var name = Clean(h.Name, Loc.T("Dispositivo"));
        _s.UpdateFavoriteName(h.Id, name);
        if (_s.IsBlocked(h.Id)) return;
        var os = Clean(h.Os, "");
        var now = DateTime.UtcNow;
        bool changed = false;

        lock (_lock)
        {
            if (_peers.TryGetValue(h.Id, out var p))
            {
                if (p.Name != name) { p.Name = name; changed = true; }
                if (p.Port != port) { p.Port = port; changed = true; }
                // Si el equipo tiene varias tarjetas llegan paquetes desde varias IP: no saltar de una a otra.
                if (!p.Address.Equals(addr) && (now - p.LastSeen).TotalSeconds > 10)
                {
                    p.Address = addr;
                    changed = true;
                }
                p.Os = os;
                p.TtlSec = Math.Clamp(h.Ttl, 10, 180);
                p.LastSeen = now;
            }
            else
            {
                _peers[h.Id] = new Peer { Id = h.Id, Name = name, Os = os, Address = addr, Port = port, LastSeen = now, TtlSec = Math.Clamp(h.Ttl, 10, 180) };
                changed = true;
            }
        }

        if (changed) PeersChanged?.Invoke();
        if (h.T == "query") Send("hello", new IPEndPoint(addr, port));
    }

    void Expire()
    {
        bool changed = false;
        var now = DateTime.UtcNow;
        lock (_lock)
        {
            foreach (var k in _peers.Where(kv => (now - kv.Value.LastSeen).TotalSeconds > kv.Value.TtlSec).Select(kv => kv.Key).ToList())
            {
                _peers.Remove(k);
                changed = true;
            }
        }
        if (changed) PeersChanged?.Invoke();
    }

    void Send(string type, IPEndPoint? unicast = null)
    {
        var udp = _udp;
        if (udp == null) return;
        try
        {
            var hello = new Hello { T = type, Id = _s.DeviceId, Name = _s.EffectiveName, Os = "windows", Port = Protocol.Port, Ttl = _ttl };
            var data = JsonSerializer.SerializeToUtf8Bytes(hello);
            if (unicast != null)
            {
                udp.Send(data, data.Length, unicast);
                return;
            }
            foreach (var ep in BroadcastTargets())
            {
                try { udp.Send(data, data.Length, ep); } catch { }
            }
        }
        catch { }
    }

    static IEnumerable<IPEndPoint> BroadcastTargets()
    {
        var set = new HashSet<IPAddress> { IPAddress.Broadcast };
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                foreach (var ua in nic.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily != AddressFamily.InterNetwork || ua.IPv4Mask == null) continue;
                    var ip = ua.Address.GetAddressBytes();
                    var mask = ua.IPv4Mask.GetAddressBytes();
                    var b = new byte[4];
                    for (int i = 0; i < 4; i++) b[i] = (byte)(ip[i] | ~mask[i]);
                    set.Add(new IPAddress(b));
                }
            }
        }
        catch { }
        return set.Select(a => new IPEndPoint(a, Protocol.Port)).ToList();
    }

    static string Clean(string? s, string fallback)
    {
        if (string.IsNullOrEmpty(s)) return fallback;
        var t = new string(s.Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (t.Length > 48) t = t[..48];
        return t.Length == 0 ? fallback : t;
    }

    public void Dispose()
    {
        try { Send("bye"); } catch { }
        _cts.Cancel();
        try { _udp?.Dispose(); } catch { }
    }
}
