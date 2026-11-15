using System.Drawing.Drawing2D;

namespace DareuMouse;

internal static class Theme
{
    public static readonly Color Background = Color.FromArgb(0x1E, 0x1F, 0x22);
    public static readonly Color Surface = Color.FromArgb(0x2B, 0x2D, 0x31);
    public static readonly Color SurfaceHover = Color.FromArgb(0x36, 0x39, 0x3F);
    public static readonly Color Border = Color.FromArgb(0x3F, 0x42, 0x48);
    public static readonly Color Text = Color.FromArgb(0xE8, 0xE8, 0xEA);
    public static readonly Color Muted = Color.FromArgb(0x9A, 0xA0, 0xA6);
    public static readonly Color Accent = Color.FromArgb(0x5B, 0x8C, 0xFF);
    public static readonly Color AccentText = Color.White;
    public static readonly Color Warning = Color.FromArgb(0xF2, 0xB5, 0x44);
    public static readonly Color Urgent = Color.FromArgb(0xFF, 0x6B, 0x6B);
    public static readonly Color Good = Color.FromArgb(0x4C, 0xC3, 0x8A);

    public static readonly Font Body = new("Segoe UI", 9.75f);
    public static readonly Font Small = new("Segoe UI", 8.25f);
    public static readonly Font Section = new("Segoe UI Semibold", 9f);
    public static readonly Font Title = new("Segoe UI Semibold", 13f);
    public static readonly Font Pill = new("Segoe UI Semibold", 9.75f);
}

/// <summary>A rounded option button. Active fills with the accent; Marked draws an accent outline.</summary>
internal sealed class Pill : Control
{
    private bool _active;
    private bool _marked;
    private bool _hover;

    public Pill()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint
            | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        Font = Theme.Pill;
        Cursor = Cursors.Hand;
        BackColor = Theme.Background;
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Active { get => _active; set { if (_active != value) { _active = value; Invalidate(); } } }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Marked { get => _marked; set { if (_marked != value) { _marked = value; Invalidate(); } } }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(BackColor);
        var rect = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        var radius = Math.Min(rect.Height / 2f, LogicalToDeviceUnits(10));
        using var path = Rounded(rect, radius);
        var fill = _active ? Theme.Accent : _hover && Enabled ? Theme.SurfaceHover : Theme.Surface;
        using (var brush = new SolidBrush(fill)) g.FillPath(brush, path);
        var outline = _marked ? Theme.Accent : _active ? Theme.Accent : Theme.Border;
        using (var pen = new Pen(outline, _marked ? LogicalToDeviceUnits(2) : 1)) g.DrawPath(pen, path);
        var color = !Enabled ? Theme.Muted : _active ? Theme.AccentText : Theme.Text;
        TextRenderer.DrawText(g, Text, Font, ClientRectangle, color,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
    }

    private static GraphicsPath Rounded(RectangleF r, float radius)
    {
        var d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}

/// <summary>
/// Draws the tray icon: a 16 × 16 pixel-art mouse whose body fills with the battery level.
/// tools/make_icon.py draws mouse.ico from the same sprite; keep both in sync.
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

    private static readonly Color Edge = Color.FromArgb(0x16, 0x17, 0x19);
    private static readonly Color Empty = Theme.Surface;

    public static Icon Draw(int battery, bool known, bool charging, int size)
    {
        var outline = known ? Color.White : Color.FromArgb(0xA0, 0xA0, 0xA0);
        var wheel = known ? Theme.Muted : Color.FromArgb(0x6A, 0x6E, 0x73);
        var fill = charging ? Theme.Accent : battery <= 15 ? Theme.Urgent : battery <= 30 ? Theme.Warning : Theme.Good;
        var bodyRows = BodyBottom - BodyTop + 1;
        // Round up so any charge shows at least one row.
        var filled = known ? (Math.Clamp(battery, 0, 100) * bodyRows + 99) / 100 : 0;

        using var bitmap = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.None;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.Clear(Color.Transparent);
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
                            color = x == first ? Mix(fill, Color.White, 0.35f) : x == last ? Mix(fill, Color.Black, 0.25f) : fill;
                            break;
                        default: continue;
                    }
                    // Cell edges rounded to whole pixels, so 16, 32 and 48 px stay crisp and 20 or 24 px still fill the icon.
                    int x0 = x * size / 16, y0 = y * size / 16, x1 = (x + 1) * size / 16, y1 = (y + 1) * size / 16;
                    using var brush = new SolidBrush(color);
                    g.FillRectangle(brush, x0, y0, x1 - x0, y1 - y0);
                }
            }
        }
        var handle = bitmap.GetHicon();
        try { return (Icon)Icon.FromHandle(handle).Clone(); }
        finally { DestroyIcon(handle); }
    }

    private static Color Mix(Color a, Color b, float t) => Color.FromArgb(
        (int)Math.Round(a.R + (b.R - a.R) * t), (int)Math.Round(a.G + (b.G - a.G) * t), (int)Math.Round(a.B + (b.B - a.B) * t));

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);
}
