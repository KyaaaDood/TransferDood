using System.Text.Json;
using System.Text.Json.Serialization;

namespace TransferDood;

static class Branding
{
    public const string AppName = "TransferDood";
    public const string Credit = "By KyaaaDood with Claude AI";
    public const string PatreonUrl = "https://www.patreon.com/cw/KyaaaDood";
}

sealed class FavoriteDevice
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Secret { get; set; } = ""; // clave compartida (base64) obtenida al emparejar
}

sealed class Settings
{
    public string DeviceId { get; set; } = Guid.NewGuid().ToString("N");
    public string DeviceName { get; set; } = "";
    public string ReceiveFolder { get; set; } = "";
    public string Language { get; set; } = "auto"; // auto | es | en
    public string Theme { get; set; } = "system"; // system | light | dark
    public bool AutoAccept { get; set; }
    public bool StartWithWindows { get; set; } = true;
    public bool ContextMenu { get; set; } = true;
    public string FirewallPath { get; set; } = "";
    public bool FirstRunDone { get; set; }

    /// <summary>No molestar: hasta cuándo se rechazan los envíos entrantes (0 = apagado, long.MaxValue = hasta que se desactive).</summary>
    public long DndUntilTicks { get; set; }

    /// <summary>Rechaza envíos entrantes mientras haya una app a pantalla completa (juegos, video).</summary>
    public bool SilenceFullscreen { get; set; }

    [JsonIgnore]
    public bool IsDnd => DndUntilTicks == long.MaxValue || (DndUntilTicks > 0 && DateTime.UtcNow.Ticks < DndUntilTicks);

    public bool RefuseIncoming() => IsDnd || (SilenceFullscreen && Native.FullscreenBusy());

    /// <summary>Equipos emparejados: sus envíos firmados se aceptan sin preguntar.</summary>
    public List<FavoriteDevice> Favorites { get; set; } = new();

    /// <summary>Equipos bloqueados: se ignoran por completo.</summary>
    public List<FavoriteDevice> Blocked { get; set; } = new();

    /// <summary>IP de equipos añadidos a mano (redes donde no funciona el descubrimiento).</summary>
    public List<string> ManualPeers { get; set; } = new();

    public bool IsFavorite(string? id)
    {
        if (string.IsNullOrEmpty(id)) return false;
        lock (Favorites) return Favorites.Any(f => f.Id == id);
    }

    public byte[]? GetSecret(string? id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        lock (Favorites)
        {
            var f = Favorites.FirstOrDefault(x => x.Id == id);
            if (f == null || string.IsNullOrEmpty(f.Secret)) return null;
            return Dpapi.Unwrap(f.Secret);
        }
    }

    public List<FavoriteDevice> FavoritesSnapshot()
    {
        lock (Favorites)
            return Favorites
                .Select(f => new FavoriteDevice { Id = f.Id, Name = f.Name })
                .OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
    }

    /// <summary>Guarda un emparejamiento nuevo (favorito con clave).</summary>
    public void Pair(string id, string name, byte[] secret)
    {
        if (string.IsNullOrEmpty(id)) return;
        lock (Favorites)
        {
            Favorites.RemoveAll(f => f.Id == id);
            Favorites.Add(new FavoriteDevice { Id = id, Name = name, Secret = Dpapi.Wrap(secret) });
        }
        lock (Blocked) Blocked.RemoveAll(b => b.Id == id);
        Save();
    }

    public void RemoveFavorite(string id)
    {
        lock (Favorites) Favorites.RemoveAll(f => f.Id == id);
        Save();
    }

    public bool IsBlocked(string? id)
    {
        if (string.IsNullOrEmpty(id)) return false;
        lock (Blocked) return Blocked.Any(b => b.Id == id);
    }

    public List<FavoriteDevice> BlockedSnapshot()
    {
        lock (Blocked)
            return Blocked
                .Select(f => new FavoriteDevice { Id = f.Id, Name = f.Name })
                .OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
    }

    public void SetBlocked(string id, string name, bool on)
    {
        if (string.IsNullOrEmpty(id)) return;
        lock (Blocked)
        {
            Blocked.RemoveAll(b => b.Id == id);
            if (on) Blocked.Add(new FavoriteDevice { Id = id, Name = name });
        }
        if (on) lock (Favorites) Favorites.RemoveAll(f => f.Id == id);
        Save();
    }

    public List<string> ManualSnapshot()
    {
        lock (ManualPeers) return ManualPeers.ToList();
    }

    public void AddManual(string ip)
    {
        lock (ManualPeers) if (!ManualPeers.Contains(ip)) ManualPeers.Add(ip);
        Save();
    }

    public void ClearManual()
    {
        lock (ManualPeers) ManualPeers.Clear();
        Save();
    }

    public void UpdateFavoriteName(string id, string name)
    {
        bool changed = false;
        lock (Favorites)
        {
            var f = Favorites.FirstOrDefault(x => x.Id == id);
            if (f != null && f.Name != name)
            {
                f.Name = name;
                changed = true;
            }
        }
        if (changed) Save();
    }

    [JsonIgnore]
    public string EffectiveName =>
        string.IsNullOrWhiteSpace(DeviceName) ? Environment.MachineName : DeviceName.Trim();

    [JsonIgnore]
    public string EffectiveFolder =>
        string.IsNullOrWhiteSpace(ReceiveFolder) ? DefaultFolder : ReceiveFolder.Trim();

    [JsonIgnore]
    public static string DefaultFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "TransferDood");

    public static string Dir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TransferDood");

    static string FilePath => Path.Combine(Dir, "settings.json");

    public static Settings Load()
    {
        // Migración desde el nombre anterior (SendDood): se conservan ajustes, favoritos e historial.
        try
        {
            var old = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SendDood");
            if (Directory.Exists(old) && !Directory.Exists(Dir)) Directory.Move(old, Dir);
        }
        catch { }
        try
        {
            if (File.Exists(FilePath))
            {
                var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath));
                if (s != null)
                {
                    // Los favoritos de versiones anteriores no tenían clave: hay que emparejarlos de nuevo.
                    s.Favorites ??= new(); s.Blocked ??= new(); s.ManualPeers ??= new();
                    s.Favorites.RemoveAll(f => string.IsNullOrEmpty(f.Secret));
                    // Claves guardadas en claro por versiones anteriores: se protegen con DPAPI.
                    bool migrated = false;
                    foreach (var f in s.Favorites)
                    {
                        if (Dpapi.IsWrapped(f.Secret)) continue;
                        var raw = Dpapi.Unwrap(f.Secret);
                        if (raw == null) continue;
                        f.Secret = Dpapi.Wrap(raw);
                        migrated = true;
                    }
                    if (migrated) s.Save();
                    return s;
                }
            }
        }
        catch { }
        var fresh = new Settings();
        fresh.Save();
        return fresh;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
