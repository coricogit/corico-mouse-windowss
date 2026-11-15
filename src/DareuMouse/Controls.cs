using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace DareuMouse;

/// <summary>Base for the owner-drawn controls: double-buffered, transparent-friendly, theme-aware.</summary>
internal abstract class Drawn : Control
{
    protected Drawn()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint
            | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
    }

    protected int Px(int logical) => LogicalToDeviceUnits(logical);

    protected static void PaintText(Graphics g, string text, Font font, Rectangle r, Color color,
        TextFormatFlags flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine)
        => TextRenderer.DrawText(g, text, font, r, color, flags | TextFormatFlags.NoPrefix);
}

/// <summary>An option button. Active fills with the accent; Marked draws an accent outline (stage being edited).</summary>
internal sealed class Pill : Drawn
{
    private bool _active;
    private bool _marked;
    private bool _hover;

    public Pill()
    {
        SetStyle(ControlStyles.Selectable, true);
        Font = Theme.Pill;
        Cursor = Cursors.Hand;
        AccessibleRole = AccessibleRole.PushButton;
        TabStop = true;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Active { get => _active; set { if (_active != value) { _active = value; Invalidate(); } } }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Marked { get => _marked; set { if (_marked != value) { _marked = value; Invalidate(); } } }

    /// <summary>Always draw a filled, outlined button (the − and + of the Cards theme).</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Outlined { get; set; }

    /// <summary>Second line under the text (Compact tiles).</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string? Caption { get; set; }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (e.KeyCode is Keys.Space or Keys.Enter) OnMouseUp(new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        var fg = !Enabled ? Theme.Muted : _active ? Theme.AccentText : Theme.Text;
        Rectangle textArea;
        switch (Theme.Kind)
        {
            case ThemeKind.Pixel:
            {
                // Square, black border, hard shadow down-right.
                var shadow = Px(3);
                var body = new Rectangle(0, 0, Width - shadow - 1, Height - shadow - 1);
                using (var black = new SolidBrush(Color.Black)) g.FillRectangle(black, shadow, shadow, body.Width + 1, body.Height + 1);
                using (var fill = new SolidBrush(_active ? Theme.Accent : _hover && Enabled ? Theme.SurfaceHover : Theme.Surface)) g.FillRectangle(fill, body);
                var line = Px(2);
                using (var pen = new Pen(_marked ? Theme.Warning : Color.Black, line) { Alignment = PenAlignment.Inset }) g.DrawRectangle(pen, body);
                textArea = body;
                break;
            }
            default:
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                float radius = Outlined ? Math.Min(Width, Height) / 2f : Theme.Kind switch { ThemeKind.Fluent => Px(4), ThemeKind.Cards => Px(7), _ => Px(Theme.Current.Radius) };
                var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
                using var path = Theme.Rounded(r, radius);
                // Fluent and Cards are segments on a track: only the chosen one is filled.
                var tile = Theme.Kind == ThemeKind.Compact;
                Color? fill = _active ? Theme.Accent
                    : Outlined ? (_hover && Enabled ? Theme.Border : Theme.SurfaceHover)
                    : _hover && Enabled ? Theme.SurfaceHover : tile ? Theme.Surface : null;
                if (fill is { } f) using (var brush = new SolidBrush(f)) g.FillPath(brush, path);
                if (_marked || ((tile || Outlined) && !_active))
                {
                    using var pen = new Pen(_marked ? Theme.Accent : Theme.Border, _marked ? Px(2) : 1);
                    g.DrawPath(pen, path);
                }
                textArea = ClientRectangle;
                break;
            }
        }
        if (Caption is null)
        {
            PaintText(g, base.Text, Font, textArea, fg);
        }
        else
        {
            var half = textArea.Height / 2;
            PaintText(g, base.Text, Font, new Rectangle(textArea.X, textArea.Y + Px(4), textArea.Width, half), fg, TextFormatFlags.HorizontalCenter | TextFormatFlags.Bottom | TextFormatFlags.SingleLine);
            PaintText(g, Caption, Theme.Small, new Rectangle(textArea.X, textArea.Y + half + Px(2), textArea.Width, half - Px(4)), _active ? fg : Theme.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }
        if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(textArea, -Px(3), -Px(3)));
    }
}

/// <summary>A slider drawn by the theme: blocks (Pixel), thin track and round thumb (others).</summary>
internal sealed class Slider : Drawn
{
    private int _min, _max = 100, _value;
    private bool _dragging;

    public Slider()
    {
        SetStyle(ControlStyles.Selectable, true);
        TabStop = true;
        Cursor = Cursors.Hand;
        AccessibleRole = AccessibleRole.Slider;
        Height = LogicalToDeviceUnits(26);
    }

    public event EventHandler? ValueChanged;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Minimum { get => _min; set { _min = value; Value = _value; Invalidate(); } }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Maximum { get => _max; set { _max = Math.Max(value, _min); Value = _value; Invalidate(); } }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int SmallChange { get; set; } = 1;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int LargeChange { get; set; } = 5;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Value
    {
        get => _value;
        set
        {
            var v = Math.Clamp(value, _min, _max);
            if (v == _value) return;
            _value = v;
            AccessibilityNotifyClients(AccessibleEvents.ValueChange, -1);
            Invalidate();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private float Fraction => _max == _min ? 0 : (_value - _min) / (float)(_max - _min);

    private Rectangle TrackArea => new(Px(10), 0, Width - Px(20), Height);

    private void SetFromX(int x)
    {
        var t = TrackArea;
        var f = Math.Clamp((x - t.Left) / (float)Math.Max(1, t.Width), 0f, 1f);
        Value = _min + (int)Math.Round(f * (_max - _min));
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (e.Button != MouseButtons.Left) return;
        _dragging = true;
        Capture = true;
        SetFromX(e.X);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragging) SetFromX(e.X);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _dragging = false;
        Capture = false;
        base.OnMouseUp(e);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        Value += Math.Sign(e.Delta) * SmallChange;
    }

    protected override bool IsInputKey(Keys key) => key is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(key);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        switch (e.KeyCode)
        {
            case Keys.Left or Keys.Down: Value -= SmallChange; break;
            case Keys.Right or Keys.Up: Value += SmallChange; break;
            case Keys.PageDown: Value -= LargeChange; break;
            case Keys.PageUp: Value += LargeChange; break;
            case Keys.Home: Value = _min; break;
            case Keys.End: Value = _max; break;
        }
    }

    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override AccessibleObject CreateAccessibilityInstance() => new SliderAccessible(this);

    private sealed class SliderAccessible(Slider owner) : ControlAccessibleObject(owner)
    {
        public override string? Value { get => owner.Value.ToString(); set { if (int.TryParse(value, out var v)) owner.Value = v; } }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        var t = TrackArea;
        var mid = Height / 2;
        if (Theme.Kind == ThemeKind.Pixel)
        {
            // A "health bar": framed row of blocks.
            var frame = new Rectangle(0, mid - Px(9), Width - 1, Px(18));
            using (var bg = new SolidBrush(Theme.Track)) g.FillRectangle(bg, frame);
            using (var pen = new Pen(Color.Black, Px(2)) { Alignment = PenAlignment.Inset }) g.DrawRectangle(pen, frame);
            var inner = Rectangle.Inflate(frame, -Px(4), -Px(4));
            var gap = Px(2);
            var count = Math.Max(8, inner.Width / Px(14));
            var lit = (int)Math.Round(Fraction * count);
            if (_value > _min && lit == 0) lit = 1;
            for (var i = 0; i < count; i++)
            {
                var x0 = inner.Left + i * (inner.Width + gap) / count;
                var x1 = inner.Left + (i + 1) * (inner.Width + gap) / count - gap;
                using var brush = new SolidBrush(i < lit ? Theme.Accent : Theme.Surface);
                g.FillRectangle(brush, x0, inner.Top, Math.Max(1, x1 - x0), inner.Height);
            }
            if (Focused && ShowFocusCues) using (var focus = new Pen(Theme.Warning, 1)) g.DrawRectangle(focus, frame.X, frame.Y, frame.Width, frame.Height);
            return;
        }

        g.SmoothingMode = SmoothingMode.AntiAlias;
        var thick = Theme.Kind == ThemeKind.Cards ? Px(6) : Px(4);
        var track = new RectangleF(t.Left, mid - thick / 2f, t.Width, thick);
        using (var path = Theme.Rounded(track, thick / 2f))
        using (var brush = new SolidBrush(Theme.Track)) g.FillPath(brush, path);
        var filled = track with { Width = Math.Max(thick, track.Width * Fraction) };
        using (var path = Theme.Rounded(filled, thick / 2f))
        using (var brush = new SolidBrush(Theme.Accent)) g.FillPath(brush, path);
        var cx = t.Left + t.Width * Fraction;
        var r = Px(9);
        var thumb = new RectangleF(cx - r, mid - r, r * 2, r * 2);
        using (var brush = new SolidBrush(Theme.Kind == ThemeKind.Cards ? Theme.Text : Theme.Surface)) g.FillEllipse(brush, thumb);
        using (var pen = new Pen(Theme.Border, 1)) g.DrawEllipse(pen, thumb);
        if (Theme.Kind != ThemeKind.Cards)
        {
            var dot = _dragging ? Px(6) : Px(5);
            using var brush = new SolidBrush(Theme.Accent);
            g.FillEllipse(brush, cx - dot, mid - dot, dot * 2, dot * 2);
        }
        if (Focused && ShowFocusCues) using (var focus = new Pen(Theme.Accent, 1.5f)) g.DrawEllipse(focus, RectangleF.Inflate(thumb, 2, 2));
    }
}

/// <summary>A checkbox drawn by the theme: square pixel box, rounded box (Cards) or a switch (Fluent, Compact).</summary>
internal sealed class Switch : Drawn
{
    private bool _checked;
    private bool _hover;

    public Switch()
    {
        SetStyle(ControlStyles.Selectable, true);
        TabStop = true;
        Cursor = Cursors.Hand;
        AccessibleRole = AccessibleRole.CheckButton;
        Font = Theme.Body;
        Height = LogicalToDeviceUnits(28);
    }

    public event EventHandler? CheckedChanged;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value) return;
            _checked = value;
            AccessibilityNotifyClients(AccessibleEvents.StateChange, -1);
            Invalidate();
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private bool IsToggle => Theme.Kind is ThemeKind.Fluent or ThemeKind.Compact;

    public override Size GetPreferredSize(Size proposed)
    {
        var text = TextRenderer.MeasureText(base.Text, Font);
        return new Size(text.Width + Px(IsToggle ? 60 : 30), Math.Max(Px(28), text.Height + Px(6)));
    }

    protected override void OnClick(EventArgs e) { Focus(); Checked = !Checked; base.OnClick(e); }
    protected override void OnKeyUp(KeyEventArgs e) { base.OnKeyUp(e); if (e.KeyCode == Keys.Space) Checked = !Checked; }
    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

    protected override AccessibleObject CreateAccessibilityInstance() => new SwitchAccessible(this);

    private sealed class SwitchAccessible(Switch owner) : ControlAccessibleObject(owner)
    {
        public override AccessibleStates State => base.State | (owner.Checked ? AccessibleStates.Checked : AccessibleStates.None);
        public override string DefaultAction => owner.Checked ? "Uncheck" : "Check";
        public override void DoDefaultAction() => owner.Checked = !owner.Checked;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        var mid = Height / 2;
        if (IsToggle)
        {
            // Text on the left, switch on the right.
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var w = Px(40);
            var h = Px(20);
            var box = new RectangleF(Width - w - 1, mid - h / 2f, w, h);
            using var path = Theme.Rounded(box, h / 2f);
            if (_checked) using (var brush = new SolidBrush(Theme.Accent)) g.FillPath(brush, path);
            else using (var pen = new Pen(_hover ? Theme.Text : Theme.Muted, 1.2f)) g.DrawPath(pen, path);
            var knob = _checked ? Px(12) : Px(10);
            var kx = _checked ? box.Right - Px(4) - knob : box.Left + Px(5);
            using (var brush = new SolidBrush(_checked ? Theme.AccentText : Theme.Muted)) g.FillEllipse(brush, kx, mid - knob / 2f, knob, knob);
            PaintText(g, base.Text, Font, new Rectangle(0, 0, (int)box.Left - Px(8), Height), Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            if (Focused && ShowFocusCues) using (var focus = new Pen(Theme.Accent, 1.5f)) g.DrawPath(focus, Theme.Rounded(RectangleF.Inflate(box, 2, 2), h / 2f + 2));
            return;
        }

        var size = Px(16);
        var square = new Rectangle(0, mid - size / 2, size, size);
        if (Theme.Kind == ThemeKind.Pixel)
        {
            using (var bg = new SolidBrush(_checked ? Theme.Accent : Theme.Track)) g.FillRectangle(bg, square);
            using (var pen = new Pen(Color.Black, Px(2)) { Alignment = PenAlignment.Inset }) g.DrawRectangle(pen, square);
            if (_checked)
            {
                // A pixel check mark.
                var p = Math.Max(1, size / 8);
                int[,] marks = { { 2, 4 }, { 3, 5 }, { 4, 4 }, { 5, 3 }, { 6, 2 } };
                using var white = new SolidBrush(Color.White);
                for (var i = 0; i < marks.GetLength(0); i++) g.FillRectangle(white, square.X + marks[i, 0] * p, square.Y + marks[i, 1] * p, p, p);
            }
            if (Focused && ShowFocusCues) using (var focus = new Pen(Theme.Warning, 1)) g.DrawRectangle(focus, Rectangle.Inflate(square, 2, 2));
        }
        else
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = Theme.Rounded(square, Px(4));
            if (_checked) using (var brush = new SolidBrush(Theme.Accent)) g.FillPath(brush, path);
            else using (var pen = new Pen(_hover ? Theme.Text : Theme.Muted, 1.2f)) g.DrawPath(pen, path);
            if (_checked)
            {
                using var pen = new Pen(Theme.AccentText, Px(2)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
                g.DrawLines(pen, [new PointF(square.X + size * 0.25f, mid), new PointF(square.X + size * 0.43f, mid + size * 0.18f), new PointF(square.X + size * 0.75f, mid - size * 0.18f)]);
            }
            if (Focused && ShowFocusCues) using (var focus = new Pen(Theme.Accent, 1.5f)) g.DrawPath(focus, Theme.Rounded(RectangleF.Inflate(square, 2, 2), Px(5)));
        }
        PaintText(g, base.Text, Font, new Rectangle(size + Px(8), 0, Width - size - Px(8), Height), Theme.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
    }
}

/// <summary>A rounded card that holds a column of controls.</summary>
internal sealed class Card : FlowLayoutPanel
{
    public Card(int width, int padding)
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        FlowDirection = FlowDirection.TopDown;
        WrapContents = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Width = width;
        MinimumSize = new Size(width, 0);
        MaximumSize = new Size(width, 0);
        Padding = new Padding(padding);
        BackColor = Theme.Background;
    }

    /// <summary>Width available to the children.</summary>
    public int Inner => Width - Padding.Horizontal;

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Theme.Rounded(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), LogicalToDeviceUnits(Theme.Current.Radius));
        using (var brush = new SolidBrush(Theme.Surface)) g.FillPath(brush, path);
        using (var pen = new Pen(Theme.Border)) g.DrawPath(pen, path);
    }
}

/// <summary>Cards theme: the battery as a ring with the percentage inside.</summary>
internal sealed class BatteryRing : Drawn
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Battery { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Charging { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Known { get; set; }

    public BatteryRing() => AccessibleRole = AccessibleRole.Graphic;

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var thick = Px(6);
        var r = new RectangleF(thick / 2f + 1, thick / 2f + 1, Width - thick - 2, Height - thick - 2);
        using (var track = new Pen(Theme.SurfaceHover, thick)) g.DrawEllipse(track, r);
        if (Known && Battery > 0)
        {
            using var arc = new Pen(Theme.BatteryColor(Battery, Charging), thick) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawArc(arc, r, -90, 360f * Math.Clamp(Battery, 0, 100) / 100f);
        }
        var text = Known ? Battery.ToString() : "–";
        using var big = new Font(Theme.Title.FontFamily, Theme.Title.Size, FontStyle.Bold);
        var top = new Rectangle(0, 0, Width, Height - Px(10));
        PaintText(g, text, big, top, Theme.Text);
        PaintText(g, Charging ? "⚡ %" : "%", Theme.Small, new Rectangle(0, Height / 2 + Px(4), Width, Px(16)), Theme.Muted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.SingleLine);
        AccessibleName = Known ? $"{Battery} %" : "";
    }
}

/// <summary>Pixel theme: the tray sprite drawn large.</summary>
internal sealed class PixelMouse : Drawn
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Battery { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Charging { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Known { get; set; }

    public PixelMouse() => AccessibleRole = AccessibleRole.Graphic;

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        var size = Math.Min(Width, Height) / 16 * 16;   // whole pixels per cell
        TrayIcon.DrawSprite(e.Graphics, new Rectangle((Width - size) / 2, (Height - size) / 2, size, size), Battery, Known, Charging);
    }
}

/// <summary>Pixel theme: the battery as a row of blocks.</summary>
internal sealed class BlockMeter : Drawn
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Battery { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Charging { get; set; }

    public BlockMeter() => AccessibleRole = AccessibleRole.Graphic;

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        const int count = 10;
        var gap = Px(2);
        var w = (Width - gap * (count - 1)) / count;
        var lit = (Math.Clamp(Battery, 0, 100) + 9) / 10;
        for (var i = 0; i < count; i++)
        {
            using var brush = new SolidBrush(i < lit ? Theme.BatteryColor(Battery, Charging) : Theme.Surface);
            g.FillRectangle(brush, i * (w + gap), 0, w, Height);
        }
    }
}
