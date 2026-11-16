using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace TransferDood;

/// <summary>
/// Cifrado de toda la conexión (AES-256-GCM con aceleración por hardware).
/// Handshake: ECDH P-256 efímero en cada conexión (secreto perfecto hacia adelante). Si los dos equipos son favoritos,
/// el secreto del emparejamiento se mezcla en la clave: así el canal también queda autenticado contra intermediarios.
/// Después del handshake el protocolo normal (SDD1…) corre igual que antes, dentro de este canal.
///
///   Cliente → servidor: "SDE1" | flags(1) | idLen(1) | id | pubLen(1) | pub (SPKI P-256, 91 bytes)
///   Servidor → cliente: flags(1) | pubLen(1) | pub        (flags bit0 = se mezcló el secreto de favoritos)
///   Luego, en cada sentido: tramos [len(4, BE)] [cifrado(len)] [etiqueta(16)], nonce = contador de 64 bits.
/// </summary>
static class SecureChannel
{
    static readonly byte[] Hello = Encoding.ASCII.GetBytes("SDE1");
    const int PubLen = 91;

    static CancellationTokenSource Timeout(CancellationToken ct)
    {
        var c = CancellationTokenSource.CreateLinkedTokenSource(ct);
        c.CancelAfter(TimeSpan.FromSeconds(10));
        return c;
    }

    public static async Task<Stream> ClientAsync(Stream raw, Settings s, string peerId, CancellationToken ct)
    {
        using var ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var pub = ecdh.ExportSubjectPublicKeyInfo();
        if (pub.Length != PubLen) throw new IOException("clave no válida");
        var secret = string.IsNullOrEmpty(peerId) ? null : s.GetSecret(peerId);

        var id = Encoding.UTF8.GetBytes(s.DeviceId);
        if (id.Length > 64) id = id[..64];
        var msg = new byte[4 + 1 + 1 + id.Length + 1 + PubLen];
        Hello.CopyTo(msg, 0);
        msg[4] = (byte)(secret != null ? 1 : 0);
        msg[5] = (byte)id.Length;
        id.CopyTo(msg, 6);
        msg[6 + id.Length] = PubLen;
        pub.CopyTo(msg, 7 + id.Length);

        using var hc = Timeout(ct);
        await raw.WriteAsync(msg, hc.Token);
        var head = new byte[2];
        await StreamUtil.ReadExactAsync(raw, head, 2, hc.Token);
        if (head[1] != PubLen) throw new IOException(Loc.T("Respuesta no válida."));
        var spub = new byte[PubLen];
        await StreamUtil.ReadExactAsync(raw, spub, PubLen, hc.Token);

        bool mixed = (head[0] & 1) != 0 && secret != null;
        return Build(raw, ecdh, pub, spub, mixed ? secret : null, client: true);
    }

    /// <summary>Devuelve null si el otro lado no habla este protocolo.</summary>
    public static async Task<Stream?> ServerAsync(Stream raw, Settings s, CancellationToken ct)
    {
        using var hc = Timeout(ct);
        var pre = new byte[6];
        await StreamUtil.ReadExactAsync(raw, pre, 6, hc.Token);
        if (!pre.AsSpan(0, 4).SequenceEqual(Hello)) return null;
        int idLen = pre[5];
        if (idLen > 64) return null;
        var idb = new byte[idLen];
        if (idLen > 0) await StreamUtil.ReadExactAsync(raw, idb, idLen, hc.Token);
        var pl = new byte[1];
        await StreamUtil.ReadExactAsync(raw, pl, 1, hc.Token);
        if (pl[0] != PubLen) return null;
        var cpub = new byte[PubLen];
        await StreamUtil.ReadExactAsync(raw, cpub, PubLen, hc.Token);

        byte[]? secret = null;
        if ((pre[4] & 1) != 0) secret = s.GetSecret(Encoding.UTF8.GetString(idb));

        using var ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var pub = ecdh.ExportSubjectPublicKeyInfo();
        var reply = new byte[2 + pub.Length];
        reply[0] = (byte)(secret != null ? 1 : 0);
        reply[1] = (byte)pub.Length;
        pub.CopyTo(reply, 2);
        await raw.WriteAsync(reply, hc.Token);

        return Build(raw, ecdh, pub, cpub, secret, client: false);
    }

    static Stream Build(Stream raw, ECDiffieHellman mine, byte[] myPub, byte[] peerPub, byte[]? secret, bool client)
    {
        using var other = ECDiffieHellman.Create();
        other.ImportSubjectPublicKeyInfo(peerPub, out _);
        var shared = mine.DeriveRawSecretAgreement(other.PublicKey);

        var epkC = client ? myPub : peerPub;
        var epkS = client ? peerPub : myPub;
        var data = Encoding.ASCII.GetBytes("tdood-e2e").Concat(shared).Concat(epkC).Concat(epkS).ToArray();
        var prk = HMACSHA256.HashData(secret ?? new byte[32], data);
        var kc = HMACSHA256.HashData(prk, Encoding.ASCII.GetBytes("c2s\u0001"));
        var ks = HMACSHA256.HashData(prk, Encoding.ASCII.GetBytes("s2c\u0001"));
        return client ? new SecureStream(raw, kc, ks) : new SecureStream(raw, ks, kc);
    }
}

/// <summary>Flujo cifrado AES-GCM por tramos de hasta 64 KB. Se usa igual que el NetworkStream de abajo.</summary>
sealed class SecureStream : Stream
{
    const int MaxFrame = 64 * 1024;

    readonly Stream _in;
    readonly AesGcm _enc;
    readonly AesGcm _dec;
    readonly byte[] _wbuf = new byte[4 + MaxFrame + 16];
    readonly byte[] _rbuf = new byte[MaxFrame + 16];
    readonly byte[] _plain = new byte[MaxFrame];
    readonly byte[] _lenR = new byte[4];
    readonly byte[] _nonceW = new byte[12];
    readonly byte[] _nonceR = new byte[12];
    readonly SemaphoreSlim _wlock = new(1, 1);
    ulong _wc, _rc;
    int _rpos, _rlen;

    public SecureStream(Stream inner, byte[] sendKey, byte[] recvKey)
    {
        _in = inner;
        _enc = new AesGcm(sendKey, 16);
        _dec = new AesGcm(recvKey, 16);
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() => _in.Flush();
    public override Task FlushAsync(CancellationToken ct) => _in.FlushAsync(ct);
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
    {
        await _wlock.WaitAsync(ct);
        try
        {
            while (buffer.Length > 0)
            {
                int n = Math.Min(buffer.Length, MaxFrame);
                BinaryPrimitives.WriteInt32BigEndian(_wbuf.AsSpan(0, 4), n);
                BinaryPrimitives.WriteUInt64BigEndian(_nonceW.AsSpan(4), _wc++);
                _enc.Encrypt(_nonceW, buffer.Span[..n], _wbuf.AsSpan(4, n), _wbuf.AsSpan(4 + n, 16), _wbuf.AsSpan(0, 4));
                await _in.WriteAsync(_wbuf.AsMemory(0, 4 + n + 16), ct);
                buffer = buffer[n..];
            }
        }
        finally { _wlock.Release(); }
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
        WriteAsync(buffer.AsMemory(offset, count), ct).AsTask();

    public override void Write(byte[] buffer, int offset, int count) =>
        WriteAsync(buffer.AsMemory(offset, count), CancellationToken.None).AsTask().GetAwaiter().GetResult();

    public override async ValueTask<int> ReadAsync(Memory<byte> dest, CancellationToken ct = default)
    {
        if (dest.Length == 0) return 0;
        if (_rpos >= _rlen)
        {
            int got = 0;
            while (got < 4)
            {
                int r = await _in.ReadAsync(_lenR.AsMemory(got, 4 - got), ct);
                if (r == 0)
                {
                    if (got == 0) return 0;
                    throw new EndOfStreamException(Loc.T("La conexión se cerró."));
                }
                got += r;
            }
            int len = BinaryPrimitives.ReadInt32BigEndian(_lenR);
            if (len <= 0 || len > MaxFrame) throw new IOException(Loc.T("La conexión no es segura (datos alterados)."));
            await StreamUtil.ReadExactAsync(_in, _rbuf, len + 16, ct);
            BinaryPrimitives.WriteUInt64BigEndian(_nonceR.AsSpan(4), _rc++);
            try
            {
                _dec.Decrypt(_nonceR, _rbuf.AsSpan(0, len), _rbuf.AsSpan(len, 16), _plain.AsSpan(0, len), _lenR);
            }
            catch (CryptographicException)
            {
                throw new IOException(Loc.T("La conexión no es segura (datos alterados)."));
            }
            _rpos = 0;
            _rlen = len;
        }
        int n = Math.Min(dest.Length, _rlen - _rpos);
        _plain.AsSpan(_rpos, n).CopyTo(dest.Span);
        _rpos += n;
        return n;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
        ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();

    public override int Read(byte[] buffer, int offset, int count) =>
        ReadAsync(buffer.AsMemory(offset, count), CancellationToken.None).AsTask().GetAwaiter().GetResult();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _enc.Dispose();
            _dec.Dispose();
            _wlock.Dispose();
        }
        base.Dispose(disposing);
    }
}
