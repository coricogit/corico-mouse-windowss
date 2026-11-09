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

/// <summary>Draws the tray icon: a mouse whose body fills with the battery level.</summary>
internal static class TrayIcon
{
    public static Icon Draw(int battery, bool known, bool charging, int size)
    {
        using var bitmap = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            var stroke = Math.Max(1.5f, size / 12f);
            var body = new RectangleF(size * 0.22f, size * 0.06f, size * 0.56f, size * 0.88f);
            using var path = new GraphicsPath();
            var r = body.Width / 2f;
            path.AddArc(body.X, body.Y, body.Width, body.Width, 180, 180);
            path.AddArc(body.X, body.Bottom - body.Width, body.Width, body.Width, 0, 180);
            path.CloseFigure();

            if (known)
            {
                var level = Math.Clamp(battery, 0, 100) / 100f;
                var color = charging ? Theme.Accent : battery <= 15 ? Theme.Urgent : battery <= 30 ? Theme.Warning : Theme.Good;
                var top = body.Bottom - body.Height * level;
                var clip = g.Clip;
                g.SetClip(path);
                using (var brush = new SolidBrush(color)) g.FillRectangle(brush, body.X, top, body.Width, body.Bottom - top);
                g.Clip = clip;
            }

            var outline = known ? Color.White : Color.FromArgb(0xA0, 0xA0, 0xA0);
            using var pen = new Pen(outline, stroke);
            g.DrawPath(pen, path);
            g.DrawLine(pen, body.X + r, body.Y + stroke, body.X + r, body.Y + body.Height * 0.3f);
        }
        var handle = bitmap.GetHicon();
        try { return (Icon)Icon.FromHandle(handle).Clone(); }
        finally { DestroyIcon(handle); }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);
}
