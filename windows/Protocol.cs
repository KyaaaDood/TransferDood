using System.Net;
using System.Text.Json.Serialization;

namespace TransferDood;

/// <summary>Constantes del protocolo. Ver PROTOCOL.md para el detalle completo.</summary>
static class Protocol
{
    public const int Port = 47615;          // UDP (descubrimiento) y TCP (transferencia)
    public const int Version = 1;
    public const int DataTimeoutSec = 120;  // sin datos: se considera caída (permite pausas cortas)
    public const int MaxPauseSec = 90;      // una pausa se reanuda sola pasado este tiempo
    public const int MaxTextChars = 60000;
    public const int BufferSize = 256 * 1024;
    public const int MaxHeaderBytes = 16 * 1024 * 1024;
    public static readonly byte[] Magic = { (byte)'S', (byte)'D', (byte)'D', (byte)'1' };
}

/// <summary>Paquete UDP de descubrimiento.</summary>
sealed class Hello
{
    [JsonPropertyName("v")] public int V { get; set; } = Protocol.Version;
    [JsonPropertyName("t")] public string T { get; set; } = "hello"; // hello | query | bye
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("os")] public string Os { get; set; } = "windows";
    [JsonPropertyName("port")] public int Port { get; set; } = Protocol.Port;
    [JsonPropertyName("ttl")] public int Ttl { get; set; } = 15; // segundos que otros deben recordarnos
}

sealed class FileEntry
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("size")] public long Size { get; set; }
}

sealed class TransferHeader
{
    [JsonPropertyName("from")] public string From { get; set; } = "";
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("total")] public long Total { get; set; }
    [JsonPropertyName("files")] public List<FileEntry> Files { get; set; } = new();

    // v2 (todos opcionales): tipo de envío, texto, firma de favoritos y clave pública para emparejar.
    [JsonPropertyName("kind")] public string Kind { get; set; } = "files"; // files | text | pair
    [JsonPropertyName("text")] public string? Text { get; set; }
    [JsonPropertyName("ts")] public long Ts { get; set; }
    [JsonPropertyName("n")] public string? Nonce { get; set; }
    [JsonPropertyName("mac")] public string? Mac { get; set; }
    [JsonPropertyName("pub")] public string? Pub { get; set; }
}

sealed class Peer
{
    public string Id { get; init; } = "";
    public string Name { get; set; } = "";
    public string Os { get; set; } = "";
    public IPAddress Address { get; set; } = IPAddress.None;
    public int Port { get; set; } = Protocol.Port;
    public DateTime LastSeen { get; set; }
    public int TtlSec { get; set; } = 15;

    public Peer Clone() => new()
    {
        Id = Id, Name = Name, Os = Os, Address = Address, Port = Port, LastSeen = LastSeen, TtlSec = TtlSec
    };
}

static class StreamUtil
{
    public static async Task ReadExactAsync(Stream s, byte[] buf, int count, CancellationToken ct)
    {
        int off = 0;
        while (off < count)
        {
            int n = await s.ReadAsync(buf.AsMemory(off, count - off), ct);
            if (n == 0) throw new EndOfStreamException(Loc.T("La conexión se cerró."));
            off += n;
        }
    }
}
