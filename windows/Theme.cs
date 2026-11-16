using Microsoft.Win32;

namespace TransferDood;

sealed class Palette
{
    public Color Bg, Surface, SurfaceAlt, Border, Text, Sub, Accent, AccentHover, AccentSoft, Danger, Success;
}

static class Theme
{
    static Color C(int rgb) => Color.FromArgb(0xFF, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);

    static readonly Palette LightPal = new()
    {
        Bg = C(0xF4F6FA), Surface = C(0xFFFFFF), SurfaceAlt = C(0xE9EDF4), Border = C(0xDCE1EA),
        Text = C(0x141824), Sub = C(0x667084), Accent = C(0x2F6BFF), AccentHover = C(0x1F58E6),
        AccentSoft = C(0xE6EEFF), Danger = C(0xE5484D), Success = C(0x24A867)
    };

    static readonly Palette DarkPal = new()
    {
        Bg = C(0x12141A), Surface = C(0x1A1D26), SurfaceAlt = C(0x262A38), Border = C(0x2E3342),
        Text = C(0xE8EBF2), Sub = C(0x98A1B5), Accent = C(0x5B8CFF), AccentHover = C(0x7BA0FF),
        AccentSoft = C(0x1F2A4A), Danger = C(0xFF6B70), Success = C(0x46C985)
    };

    public static Palette P { get; private set; } = LightPal;
    public static bool IsDark { get; private set; }
    public static string Mode { get; private set; } = "system";

    /// <summary>Se dispara (posiblemente desde otro hilo) cuando cambia el tema efectivo.</summary>
    public static event Action? Changed;

    public static void Init()
    {
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (Mode == "system" && e.Category == UserPreferenceCategory.General) Apply("system");
        };
    }

    public static void Apply(string mode)
    {
        Mode = mode;
        bool dark = mode == "dark" || (mode != "light" && SystemUsesDark());
        IsDark = dark;
        P = dark ? DarkPal : LightPal;
        Changed?.Invoke();
    }

    static bool SystemUsesDark()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return k?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch { return false; }
    }
}
