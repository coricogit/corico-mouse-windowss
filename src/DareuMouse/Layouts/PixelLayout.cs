using System.Globalization;
using DareuMouse.Core;

namespace DareuMouse;

/// <summary>Pixel art (default): the tray sprite in big, block meters and square buttons with hard shadows.</summary>
internal sealed partial class PanelForm
{
    private Control BuildPixel()
    {
        var w = Width340;
        var column = Column(w);

        // Header: big pixel mouse, name, connection, battery blocks.
        var mouse = new PixelMouse { Size = new Size(Px(64), Px(64)), Margin = new Padding(0, 0, Px(14), 0) };
        var title = NewLabel(Theme.Title, Theme.Text);
        title.Text = T.Title.ToUpper(CultureInfo.CurrentCulture);
        _subtitle.Margin = new Padding(0, Px(4), 0, 0);
        var blocks = new BlockMeter { Size = new Size(Px(120), Px(12)), Margin = new Padding(0, Px(9), Px(8), 0) };
        _battery.Font = Theme.Display;
        _battery.Margin = new Padding(0, Px(5), 0, 0);
        var batteryRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty, BackColor = Color.Transparent };
        batteryRow.Controls.Add(blocks);
        batteryRow.Controls.Add(_battery);
        var info = Column();
        info.Controls.Add(title);
        info.Controls.Add(_subtitle);
        info.Controls.Add(batteryRow);
        _mouseOnly.Add(batteryRow);
        var header = Line(w, mouse, info);
        column.Controls.Add(header);
        column.Controls.Add(_status);
        column.Controls.Add(Dashes(w, top: 14));

        // Mouse settings.
        var mouseColumn = Column(w);
        mouseColumn.Controls.Add(SectionHeader(T.Dpi, T.DpiHint, w, top: 10));
        SizeRow(_stagePills, w);
        mouseColumn.Controls.Add(_stagePills);
        mouseColumn.Controls.Add(_editLabel);
        _dpiSlider.Margin = new Padding(0, Px(6), Px(10), 0);
        _dpiBox.Margin = new Padding(0, Px(6), 0, 0);
        mouseColumn.Controls.Add(Line(w, _dpiSlider, _dpiBox));
        mouseColumn.Controls.Add(SectionHeader(T.Polling, null, w));
        SizeRow(_pollingPills, w);
        mouseColumn.Controls.Add(_pollingPills);
        mouseColumn.Controls.Add(_pollingNote);
        _mouseOnly.Add(mouseColumn);
        column.Controls.Add(mouseColumn);

        // Windows pointer.
        column.Controls.Add(SectionHeader(T.Pointer, T.SpeedHint, w));
        var speedLabel = NewLabel(Theme.Body, Theme.Text);
        speedLabel.Text = T.Speed;
        speedLabel.Margin = new Padding(0, 0, Px(10), 0);
        _speedValue.MinimumSize = new Size(Px(110), 0);
        _speedValue.Margin = new Padding(Px(8), 0, 0, 0);
        column.Controls.Add(Line(w, speedLabel, _speedSlider, _speedValue));
        _noAccel.Width = w;
        _noAccel.Margin = new Padding(0, Px(6), 0, 0);
        column.Controls.Add(_noAccel);
        var note = Note(T.PointerNote, w - Px(24));
        note.Margin = new Padding(Px(24), 0, 0, 0);
        column.Controls.Add(note);

        column.Controls.Add(Dashes(w, top: 14, bottom: 8));
        column.Controls.Add(Footer(w));

        _renderTheme = () =>
        {
            var s = _state;
            var ok = s.Status == MouseStatus.Ok;
            mouse.Known = ok;
            mouse.Battery = ok ? s.Battery : 0;
            mouse.Charging = ok && s.Charging;
            mouse.Invalidate();
            blocks.Battery = mouse.Battery;
            blocks.Charging = mouse.Charging;
            blocks.Invalidate();
            if (ok) _battery.Text = s.Charging ? $"+{s.Battery}%" : $"{s.Battery}%";
        };
        return column;
    }

    /// <summary>A dashed pixel separator.</summary>
    private Control Dashes(int width, int top = 12, int bottom = 0)
    {
        var line = new Panel { Width = width, Height = Px(2), Margin = new Padding(0, Px(top), 0, Px(bottom)), BackColor = Theme.Background };
        line.Paint += (_, e) =>
        {
            using var brush = new SolidBrush(Theme.Border);
            var dash = Px(4);
            for (var x = 0; x < line.Width; x += dash * 2) e.Graphics.FillRectangle(brush, x, 0, dash, line.Height);
        };
        return line;
    }
}
