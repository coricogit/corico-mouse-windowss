using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace DareuMouse;

/// <summary>The four looks of the app. Pixel is the default.</summary>
internal enum ThemeKind { Pixel, Fluent, Cards, Compact }

/// <summary>Colors and fonts of one theme.</summary>
internal sealed record Palette(
    Color Background, Color Surface, Color SurfaceHover, Color Track, Color Border,
    Color Text, Color Muted, Color Accent, Color AccentText,
    Color Warning, Color Urgent, Color Good,
    Font Body, Font Small, Font Section, Font Title, Font Pill, Font Display,
    int Radius, bool Light);

/// <summary>The current theme. Everything that draws reads its colors and fonts from here.</summary>
internal static class Theme
{
    public static ThemeKind Kind { get; private set; }
    public static Palette Current { get; private set; } = null!;

    static Theme() => Apply(ThemeSettings.Load());

    public static void Apply(ThemeKind kind)
    {
        Kind = kind;
        Current = kind switch
        {
            ThemeKind.Fluent => Fluent(),
            ThemeKind.Cards => Cards(),
            ThemeKind.Compact => Compact(),
            _ => Pixel(),
        };
    }

    public static Color Background => Current.Background;
    public static Color Surface => Current.Surface;
    public static Color SurfaceHover => Current.SurfaceHover;
    public static Color Track => Current.Track;
    public static Color Border => Current.Border;
    public static Color Text => Current.Text;
    public static Color Muted => Current.Muted;
    public static Color Accent => Current.Accent;
    public static Color AccentText => Current.AccentText;
    public static Color Warning => Current.Warning;
    public static Color Urgent => Current.Urgent;
    public static Color Good => Current.Good;
    public static Font Body => Current.Body;
    public static Font Small => Current.Small;
    public static Font Section => Current.Section;
    public static Font Title => Current.Title;
    public static Font Pill => Current.Pill;
    public static Font Display => Current.Display;

    /// <summary>Green, amber or red by charge; the accent while charging.</summary>
    public static Color BatteryColor(int battery, bool charging) =>
        charging ? Accent : battery <= 15 ? Urgent : battery <= 30 ? Warning : Good;

    // ---------- Palettes ----------

    private static Palette Pixel() => new(
        Background: Rgb(0x1E1F22), Surface: Rgb(0x2B2D31), SurfaceHover: Rgb(0x36393F), Track: Rgb(0x111214), Border: Rgb(0x3F4248),
        Text: Rgb(0xE8E8EA), Muted: Rgb(0x9AA0A6), Accent: Rgb(0x5B8CFF), AccentText: Color.White,
        Warning: Rgb(0xF2B544), Urgent: Rgb(0xFF6B6B), Good: Rgb(0x4CC38A),
        Body: Ui(9.75f), Small: Ui(8.25f), Section: PixelFont.Get(9f, bold: true), Title: PixelFont.Get(14f, bold: true),
        Pill: PixelFont.Get(8.5f), Display: PixelFont.Get(11f),
        Radius: 0, Light: false);

    private static Palette Fluent()
    {
        var light = WindowsLook.AppsUseLightTheme;
        var accent = WindowsLook.AccentColor;
        var text = UiVariable("Text");
        var display = UiVariable("Display");
        return light
            ? new(Rgb(0xF3F3F3), Rgb(0xFBFBFB), Rgb(0xF5F5F5), Rgb(0xEAEAEA), Rgb(0xE5E5E5),
                  Rgb(0x1A1A1A), Rgb(0x5D5D5D), accent ?? Rgb(0x005FB8), Color.White,
                  Rgb(0x9D5D00), Rgb(0xC42B1C), Rgb(0x0F7B0F),
                  new Font(text, 10f), new Font(text, 8.5f), new Font(text, 10f, FontStyle.Bold), new Font(display, 13f, FontStyle.Bold),
                  new Font(text, 9.5f), new Font(display, 12f, FontStyle.Bold), 8, true)
            : new(Rgb(0x202020), Rgb(0x2B2B2B), Rgb(0x323232), Rgb(0x1C1C1C), Rgb(0x3A3A3A),
                  Color.White, Rgb(0xC5C5C5), accent is { } a ? Lighten(a, 0.45f) : Rgb(0x4CC2FF), Color.Black,
                  Rgb(0xFCE100), Rgb(0xFF99A4), Rgb(0x6CCB5F),
                  new Font(text, 10f), new Font(text, 8.5f), new Font(text, 10f, FontStyle.Bold), new Font(display, 13f, FontStyle.Bold),
                  new Font(text, 9.5f), new Font(display, 12f, FontStyle.Bold), 8, false);
    }

    private static Palette Cards() => new(
        Background: Rgb(0x16171A), Surface: Rgb(0x1E1F22), SurfaceHover: Rgb(0x2B2D31), Track: Rgb(0x16171A), Border: Rgb(0x2C2E33),
        Text: Rgb(0xE8E8EA), Muted: Rgb(0x9AA0A6), Accent: Rgb(0x5B8CFF), AccentText: Color.White,
        Warning: Rgb(0xF2B544), Urgent: Rgb(0xFF6B6B), Good: Rgb(0x4CC38A),
        Body: Ui(9.75f), Small: Ui(8.25f), Section: Ui(8.25f, FontStyle.Bold), Title: Ui(14f, FontStyle.Bold),
        Pill: Ui(9f, FontStyle.Bold), Display: Ui(34f, FontStyle.Bold),
        Radius: 12, Light: false);

    private static Palette Compact() => new(
        Background: Rgb(0x202124), Surface: Rgb(0x2D2F33), SurfaceHover: Rgb(0x383B40), Track: Rgb(0x45484E), Border: Rgb(0x3D4045),
        Text: Rgb(0xEDEDED), Muted: Rgb(0xC5C7CC), Accent: Rgb(0x4CC2FF), AccentText: Rgb(0x0B1A24),
        Warning: Rgb(0xF2B544), Urgent: Rgb(0xFF6B6B), Good: Rgb(0x4CC38A),
        Body: Ui(9.75f), Small: Ui(8.25f), Section: Ui(8.25f, FontStyle.Bold), Title: Ui(11f, FontStyle.Bold),
        Pill: Ui(11f, FontStyle.Bold), Display: Ui(11f, FontStyle.Bold),
        Radius: 6, Light: false);

    // ---------- Helpers ----------

    public static Color Rgb(int rgb) => Color.FromArgb(255, Color.FromArgb(rgb));

    public static Color Mix(Color a, Color b, float t) => Color.FromArgb(
        (int)Math.Round(a.R + (b.R - a.R) * t), (int)Math.Round(a.G + (b.G - a.G) * t), (int)Math.Round(a.B + (b.B - a.B) * t));

    private static Color Lighten(Color c, float t) => Mix(c, Color.White, t);

    private static Font Ui(float size, FontStyle style = FontStyle.Regular) => new("Segoe UI", size, style);

    /// <summary>Segoe UI Variable (Windows 11), or Segoe UI where it is missing.</summary>
    private static string UiVariable(string optical)
    {
        var name = "Segoe UI Variable " + optical;
        using var test = new Font(name, 10f);
        return test.Name == name ? name : "Segoe UI";
    }

    /// <summary>A rounded rectangle path (radius 0 gives a plain rectangle).</summary>
    public static GraphicsPath Rounded(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        if (radius <= 0) { path.AddRectangle(r); return path; }
        var d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}

/// <summary>The chosen theme, kept in HKCU\Software\DareuMouse.</summary>
internal static class ThemeSettings
{
    private const string Key = @"Software\DareuMouse";
    private const string ValueName = "Theme";

    public static ThemeKind Load()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(Key);
            return key?.GetValue(ValueName) is string s && Enum.TryParse<ThemeKind>(s, out var kind) && Enum.IsDefined(kind)
                ? kind : ThemeKind.Pixel;
        }
        catch { return ThemeKind.Pixel; }
    }

    public static void Save(ThemeKind kind)
    {
        using var key = Registry.CurrentUser.CreateSubKey(Key);
        key.SetValue(ValueName, kind.ToString());
    }
}

/// <summary>What Windows itself looks like: light or dark apps and taskbar, accent color.</summary>
internal static class WindowsLook
{
    private const string Personalize = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    public static bool AppsUseLightTheme => ReadDword(Personalize, "AppsUseLightTheme", 1) != 0;

    public static bool TaskbarIsLight => ReadDword(Personalize, "SystemUsesLightTheme", 0) != 0;

    /// <summary>The accent color the user picked in Settings, or null.</summary>
    public static Color? AccentColor
    {
        get
        {
            var abgr = ReadDword(@"Software\Microsoft\Windows\DWM", "AccentColor", -1);
            if (abgr == -1) return null;
            return Color.FromArgb(abgr & 0xFF, (abgr >> 8) & 0xFF, (abgr >> 16) & 0xFF);
        }
    }

    private static int ReadDword(string path, string name, int fallback)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(path);
            return key?.GetValue(name) is int v ? v : fallback;
        }
        catch { return fallback; }
    }

    /// <summary>Rounded (or square) corners for a borderless window on Windows 11.</summary>
    public static void SetCorners(IntPtr hwnd, bool rounded)
    {
        var pref = rounded ? 2 : 1; // DWMWCP_ROUND : DWMWCP_DONOTROUND
        try { DwmSetWindowAttribute(hwnd, 33, ref pref, sizeof(int)); } catch { /* Windows 10 */ }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}

/// <summary>Silkscreen (SIL Open Font License, Fonts/OFL.txt), embedded in the app.</summary>
internal static class PixelFont
{
    private static readonly PrivateFontCollection Fonts = new();
    private static readonly FontFamily? Family;

    static PixelFont()
    {
        Load("Silkscreen-Regular.ttf");
        Load("Silkscreen-Bold.ttf");
        Family = Fonts.Families.FirstOrDefault(f => f.Name.StartsWith("Silkscreen", StringComparison.OrdinalIgnoreCase));
    }

    public static Font Get(float size, bool bold = false)
    {
        var style = bold ? FontStyle.Bold : FontStyle.Regular;
        if (Family is null) return new Font("Consolas", size, style);
        return new Font(Family, size, Family.IsStyleAvailable(style) ? style : FontStyle.Regular);
    }

    private static void Load(string file)
    {
        try
        {
            using var stream = typeof(PixelFont).Assembly.GetManifestResourceStream("DareuMouse.Fonts." + file);
            if (stream is null) return;
            var data = new byte[stream.Length];
            stream.ReadExactly(data);
            var memory = Marshal.AllocCoTaskMem(data.Length);   // kept for the life of the app, as GDI+ requires
            Marshal.Copy(data, 0, memory, data.Length);
            Fonts.AddMemoryFont(memory, data.Length);
            uint count = 0;
            AddFontMemResourceEx(memory, (uint)data.Length, IntPtr.Zero, ref count);   // so GDI text (labels) can use it too
        }
        catch { /* falls back to Consolas */ }
    }

    [DllImport("gdi32.dll")]
    private static extern IntPtr AddFontMemResourceEx(IntPtr font, uint length, IntPtr reserved, ref uint fonts);
}

/// <summary>
/// The notification-area icon, one per theme. Pixel is the 16 × 16 sprite (tools/make_icon.py draws mouse.ico
/// from the same sprite; keep both in sync); the others follow the taskbar's light or dark color.
/// </summary>
internal static class TrayIcon
{
    // K dark edge (visible on light and dark taskbars), O outline, W scroll wheel, b body (battery fill).
    private static readonly string[] Sprite =
    [
        ".....KKKKKK.....",
        "....KOOOOOOK....",
        "...KObbOObbOK...",
        "..KObbbWWbbbOK..",
        "..KObbbWWbbbOK..",
        "..KObbbOObbbOK..",
        "..KObbbbbbbbOK..",
        "..KObbbbbbbbOK..",
        "..KObbbbbbbbOK..",
        "..KObbbbbbbbOK..",
        "..KObbbbbbbbOK..",
        "..KObbbbbbbbOK..",
        "..KObbbbbbbbOK..",
        "...KObbbbbbOK...",
        "....KOOOOOOK....",
        ".....KKKKKK.....",
    ];
    private const int BodyTop = 2, BodyBottom = 13;     // rows that hold battery pixels

    // Fixed colors so the icon looks the same whatever theme the panel uses.
    private static readonly Color Edge = Color.FromArgb(0x16, 0x17, 0x19);
    private static readonly Color Empty = Color.FromArgb(0x2B, 0x2D, 0x31);
    private static readonly Color Good = Color.FromArgb(0x4C, 0xC3, 0x8A);
    private static readonly Color Warning = Color.FromArgb(0xF2, 0xB5, 0x44);
    private static readonly Color Urgent = Color.FromArgb(0xFF, 0x6B, 0x6B);
    private static readonly Color Charging = Color.FromArgb(0x5B, 0x8C, 0xFF);

    private static Color Level(int battery, bool charging) =>
        charging ? Charging : battery <= 15 ? Urgent : battery <= 30 ? Warning : Good;

    public static Icon Draw(ThemeKind theme, int battery, bool known, bool charging, int size)
    {
        using var bitmap = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.Clear(Color.Transparent);
            switch (theme)
            {
                case ThemeKind.Fluent: DrawOutlineMouse(g, size, battery, known, charging); break;
                case ThemeKind.Cards: DrawRing(g, size, battery, known, charging); break;
                case ThemeKind.Compact: DrawBattery(g, size, battery, known, charging); break;
                default: DrawSprite(g, new Rectangle(0, 0, size, size), battery, known, charging); break;
            }
        }
        var handle = bitmap.GetHicon();
        try { return (Icon)Icon.FromHandle(handle).Clone(); }
        finally { DestroyIcon(handle); }
    }

    /// <summary>The pixel-art mouse in any square area (also the big mouse of the Pixel panel).</summary>
    public static void DrawSprite(Graphics g, Rectangle area, int battery, bool known, bool charging)
    {
        var outline = known ? Color.White : Color.FromArgb(0xA0, 0xA0, 0xA0);
        var wheel = known ? Color.FromArgb(0x9A, 0xA0, 0xA6) : Color.FromArgb(0x6A, 0x6E, 0x73);
        var fill = Level(battery, charging);
        var bodyRows = BodyBottom - BodyTop + 1;
        // Round up so any charge shows at least one row.
        var filled = known ? (Math.Clamp(battery, 0, 100) * bodyRows + 99) / 100 : 0;
        var size = Math.Min(area.Width, area.Height);

        var smoothing = g.SmoothingMode;
        var offset = g.PixelOffsetMode;
        g.SmoothingMode = SmoothingMode.None;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        for (var y = 0; y < Sprite.Length; y++)
        {
            var row = Sprite[y];
            int first = row.IndexOf('b'), last = row.LastIndexOf('b');
            for (var x = 0; x < row.Length; x++)
            {
                Color color;
                switch (row[x])
                {
                    case 'K': color = Edge; break;
                    case 'O': color = outline; break;
                    case 'W': color = wheel; break;
                    case 'b':
                        if (BodyBottom - y >= filled) { color = Empty; break; }
                        // Light left column, shaded right column: the usual pixel-art volume.
                        color = x == first ? Theme.Mix(fill, Color.White, 0.35f) : x == last ? Theme.Mix(fill, Color.Black, 0.25f) : fill;
                        break;
                    default: continue;
                }
                // Cell edges rounded to whole pixels, so 16, 32 and 48 px stay crisp and 20 or 24 px still fill the icon.
                int x0 = x * size / 16, y0 = y * size / 16, x1 = (x + 1) * size / 16, y1 = (y + 1) * size / 16;
                using var brush = new SolidBrush(color);
                g.FillRectangle(brush, area.X + x0, area.Y + y0, x1 - x0, y1 - y0);
            }
        }
        g.SmoothingMode = smoothing;
        g.PixelOffsetMode = offset;
    }

    private static Color Ink(bool known) =>
        !known ? Color.FromArgb(0x90, 0x90, 0x90) : WindowsLook.TaskbarIsLight ? Color.FromArgb(0x1A, 0x1A, 0x1A) : Color.White;

    /// <summary>Fluent: a smooth outlined mouse whose body fills with the battery.</summary>
    private static void DrawOutlineMouse(Graphics g, int size, int battery, bool known, bool charging)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var stroke = Math.Max(1.5f, size / 12f);
        var body = new RectangleF(size * 0.22f, size * 0.06f, size * 0.56f, size * 0.88f);
        using var path = new GraphicsPath();
        var r = body.Width / 2f;
        path.AddArc(body.X, body.Y, body.Width, body.Width, 180, 180);
        path.AddArc(body.X, body.Bottom - body.Width, body.Width, body.Width, 0, 180);
        path.CloseFigure();
        if (known)
        {
            var top = body.Bottom - body.Height * Math.Clamp(battery, 0, 100) / 100f;
            var clip = g.Clip;
            g.SetClip(path);
            using (var brush = new SolidBrush(Level(battery, charging))) g.FillRectangle(brush, body.X, top, body.Width, body.Bottom - top);
            g.Clip = clip;
        }
        using var pen = new Pen(Ink(known), stroke);
        g.DrawPath(pen, path);
        g.DrawLine(pen, body.X + r, body.Y + stroke, body.X + r, body.Y + body.Height * 0.3f);
    }

    /// <summary>Cards: a battery ring around a small mouse.</summary>
    private static void DrawRing(Graphics g, int size, int battery, bool known, bool charging)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var ink = Ink(known);
        var thick = Math.Max(2f, size / 7f);
        var ring = new RectangleF(thick / 2 + 0.5f, thick / 2 + 0.5f, size - thick - 1, size - thick - 1);
        using (var track = new Pen(Color.FromArgb(90, ink), thick)) g.DrawEllipse(track, ring);
        if (known && battery > 0)
        {
            using var arc = new Pen(Level(battery, charging), thick) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawArc(arc, ring, -90, 360f * Math.Clamp(battery, 0, 100) / 100f);
        }
        var w = size * 0.26f;
        var h = size * 0.38f;
        using var mouse = Theme.Rounded(new RectangleF((size - w) / 2, (size - h) / 2, w, h), w / 2);
        using var fill = new SolidBrush(ink);
        g.FillPath(fill, mouse);
    }

    /// <summary>Compact: a horizontal battery like Windows quick settings.</summary>
    private static void DrawBattery(Graphics g, int size, int battery, bool known, bool charging)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var ink = Ink(known);
        var stroke = Math.Max(1.25f, size / 14f);
        var body = new RectangleF(size * 0.06f, size * 0.27f, size * 0.78f, size * 0.46f);
        using (var path = Theme.Rounded(body, size * 0.1f))
        using (var pen = new Pen(ink, stroke)) g.DrawPath(pen, path);
        using (var nub = Theme.Rounded(new RectangleF(body.Right + stroke * 0.6f, size * 0.4f, size * 0.08f, size * 0.2f), size * 0.03f))
        using (var brush = new SolidBrush(ink)) g.FillPath(brush, nub);
        if (known && battery > 0)
        {
            var inset = stroke * 1.5f;
            var inner = RectangleF.Inflate(body, -inset, -inset);
            inner.Width = Math.Max(1, inner.Width * Math.Clamp(battery, 0, 100) / 100f);
            using var level = Theme.Rounded(inner, size * 0.04f);
            using var brush = new SolidBrush(Level(battery, charging));
            g.FillPath(brush, level);
        }
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);
}
