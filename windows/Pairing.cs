using System.Buffers.Binary;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TransferDood;

/// <summary>Criptografía mínima: firma de envíos de favoritos y emparejamiento por clave pública (ECDH P-256).</summary>
static class Crypto
{
    static readonly Dictionary<string, long> Seen = new();
    static readonly object L = new();

    public static string NewNonce() => Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant();

    public static string Mac(byte[] secret, string id, long ts, string nonce)
    {
        var data = Encoding.UTF8.GetBytes($"{id}|{ts}|{nonce}");
        return Convert.ToHexString(HMACSHA256.HashData(secret, data)).ToLowerInvariant();
    }

    /// <summary>Añade la firma al encabezado si ya hay emparejamiento con ese equipo.</summary>
    public static void Sign(TransferHeader h, Settings s, string peerId)
    {
        var secret = s.GetSecret(peerId);
        if (secret == null) return;
        h.Ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        h.Nonce = NewNonce();
        h.Mac = Mac(secret, h.Id, h.Ts, h.Nonce);
    }

    /// <summary>True si el encabezado trae una firma válida, reciente y no repetida de un favorito.</summary>
    public static bool VerifyTrusted(TransferHeader h, Settings s)
    {
        if (string.IsNullOrEmpty(h.Id) || string.IsNullOrEmpty(h.Nonce) || string.IsNullOrEmpty(h.Mac)) return false;
        var secret = s.GetSecret(h.Id);
        if (secret == null) return false;
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (Math.Abs(now - h.Ts) > 300 || h.Nonce.Length > 64) return false;

        var expect = Mac(secret, h.Id, h.Ts, h.Nonce);
        if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expect), Encoding.ASCII.GetBytes(h.Mac.ToLowerInvariant())))
            return false;

        lock (L)
        {
            foreach (var k in Seen.Where(kv => kv.Value < now).Select(kv => kv.Key).ToList()) Seen.Remove(k);
            var key = h.Id + h.Nonce;
            if (Seen.ContainsKey(key)) return false;
            Seen[key] = now + 600;
        }
        return true;
    }

    public static string Sas(byte[] initiatorPub, byte[] responderPub)
    {
        using var sha = SHA256.Create();
        var all = Encoding.ASCII.GetBytes("SDD-SAS").Concat(initiatorPub).Concat(responderPub).ToArray();
        var h = sha.ComputeHash(all);
        uint v = BinaryPrimitives.ReadUInt32BigEndian(h.AsSpan(0, 4)) % 1_000_000;
        var t = v.ToString("D6");
        return t[..3] + " " + t[3..];
    }

    public static byte[] Derive(ECDiffieHellman mine, byte[] peerPub)
    {
        using var other = ECDiffieHellman.Create();
        other.ImportSubjectPublicKeyInfo(peerPub, out _);
        var raw = mine.DeriveRawSecretAgreement(other.PublicKey);
        return SHA256.HashData(raw);
    }
}

/// <summary>Lado que inicia el emparejamiento (toca la estrella de un equipo).</summary>
static class PairClient
{
    /// <summary>confirm: muestra el código y devuelve si el usuario confirma que coincide en ambos equipos.</summary>
    public static async Task<string?> RunAsync(Peer peer, Settings s, Func<string, Task<bool>> confirm)
    {
        try
        {
            using var client = new TcpClient(AddressFamily.InterNetwork) { NoDelay = true };
            using (var cc = new CancellationTokenSource(TimeSpan.FromSeconds(6)))
            {
                try { await client.ConnectAsync(peer.Address, peer.Port, cc.Token); }
                catch (Exception) { return Loc.T("No se pudo conectar con el equipo."); }
            }
            using var ns = await SecureChannel.ClientAsync(client.GetStream(), s, "", CancellationToken.None);

            using var ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
            var myPub = ecdh.ExportSubjectPublicKeyInfo();

            var header = new TransferHeader { From = s.EffectiveName, Id = s.DeviceId, Kind = "pair", Pub = Convert.ToBase64String(myPub) };
            var hb = JsonSerializer.SerializeToUtf8Bytes(header);
            var pre = new byte[8];
            Protocol.Magic.CopyTo(pre, 0);
            BinaryPrimitives.WriteInt32BigEndian(pre.AsSpan(4, 4), hb.Length);
            await ns.WriteAsync(pre);
            await ns.WriteAsync(hb);

            var one = new byte[1];
            using (var c1 = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
                await StreamUtil.ReadExactAsync(ns, one, 1, c1.Token);
            if (one[0] != 3) return Loc.T("El otro equipo no aceptó emparejar.");

            var lb = new byte[4];
            using (var c2 = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
            {
                await StreamUtil.ReadExactAsync(ns, lb, 4, c2.Token);
                int len = BinaryPrimitives.ReadInt32BigEndian(lb);
                if (len <= 0 || len > 4096) return Loc.T("Respuesta no válida.");
                var peerPub = new byte[len];
                await StreamUtil.ReadExactAsync(ns, peerPub, len, c2.Token);

                string code = Crypto.Sas(myPub, peerPub);
                bool ok = await confirm(code);
                await ns.WriteAsync(new byte[] { (byte)(ok ? 1 : 0) });
                if (!ok) return Loc.T("Emparejamiento cancelado.");

                using var c3 = new CancellationTokenSource(TimeSpan.FromSeconds(75));
                await StreamUtil.ReadExactAsync(ns, one, 1, c3.Token);
                if (one[0] != 1) return Loc.T("El otro equipo no confirmó el código.");

                s.Pair(peer.Id, peer.Name, Crypto.Derive(ecdh, peerPub));
            }
            return null;
        }
        catch (OperationCanceledException) { return Loc.T("El otro equipo no respondió."); }
        catch (Exception ex) { return ex is EndOfStreamException ? Loc.T("El otro equipo cerró la conexión.") : ex.Message; }
    }
}

/// <summary>Historial de transferencias guardado en disco (últimas 60).</summary>
sealed class HistoryRecord
{
    public bool Incoming { get; set; }
    public string PeerName { get; set; } = "";
    public string Title { get; set; } = "";
    public int FileCount { get; set; }
    public long TotalBytes { get; set; }
    public int State { get; set; }
    public string? Error { get; set; }
    public string? SavedPath { get; set; }
    public string Kind { get; set; } = "files";
    public string? Text { get; set; }
    public DateTime TimeUtc { get; set; }
}

static class History
{
    static readonly object L = new();
    static string FilePath => Path.Combine(Settings.Dir, "history.json");

    public static List<HistoryRecord> Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<List<HistoryRecord>>(File.ReadAllText(FilePath)) ?? new();
        }
        catch { }
        return new();
    }

    public static void Save(IEnumerable<TransferJob> jobs)
    {
        try
        {
            var list = jobs.Where(j => j.IsFinished).TakeLast(60).Select(j => j.ToRecord()).ToList();
            lock (L)
            {
                Directory.CreateDirectory(Settings.Dir);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(list));
            }
        }
        catch { }
    }
}
