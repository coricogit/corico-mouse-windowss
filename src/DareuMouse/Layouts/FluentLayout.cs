namespace DareuMouse;

/// <summary>Windows 11: light or dark like the system, cards, segmented controls and switches.</summary>
internal sealed partial class PanelForm
{
    private Control BuildFluent()
    {
        var w = Width340;
        var column = Column(w);
        var pad = Px(14);
        var gap = Px(10);

        // Header: name and connection, battery on the right.
        var title = NewLabel(Theme.Title, Theme.Text);
        title.Text = T.Title;
        var names = Column();
        names.Controls.Add(title);
        names.Controls.Add(_subtitle);
        _battery.Font = Theme.Display;
        _battery.Anchor = AnchorStyles.Right;
        var header = Line(w, names, Spacer(), _battery);
        header.Margin = new Padding(Px(4), 0, Px(4), Px(4));
        header.MinimumSize = header.MaximumSize = new Size(w - Px(8), 0);
        column.Controls.Add(header);
        _status.Margin = new Padding(Px(4), Px(4), 0, Px(6));
        column.Controls.Add(_status);

        // DPI card.
        var dpi = new Card(w, pad) { Margin = new Padding(0, Px(8), 0, 0) };
        var inner = dpi.Inner;
        dpi.Controls.Add(SectionHeader(T.Dpi, T.DpiHint, inner, top: 0));
        SizeRow(_stagePills, inner);
        dpi.Controls.Add(_stagePills);
        dpi.Controls.Add(_editLabel);
        _dpiSlider.BackColor = Theme.Surface;
        _dpiSlider.Margin = new Padding(0, Px(10), Px(10), 0);
        _dpiBox.Margin = new Padding(0, Px(10), 0, 0);
        dpi.Controls.Add(Line(inner, _dpiSlider, _dpiBox));
        _mouseOnly.Add(dpi);
        column.Controls.Add(dpi);

        // Polling card.
        var polling = new Card(w, pad) { Margin = new Padding(0, gap, 0, 0) };
        polling.Controls.Add(SectionHeader(T.Polling, null, inner, top: 0));
        SizeRow(_pollingPills, inner);
        polling.Controls.Add(_pollingPills);
        polling.Controls.Add(_pollingNote);
        _pollingNote.MaximumSize = new Size(inner, 0);
        _mouseOnly.Add(polling);
        column.Controls.Add(polling);

        // Pointer card: speed, acceleration, start with Windows, separated by lines.
        var pointer = new Card(w, pad) { Margin = new Padding(0, gap, 0, 0) };
        var speedTitle = NewLabel(Theme.Body, Theme.Text);
        speedTitle.Text = T.PointerSpeed;
        _speedValue.Font = Theme.Small;
        _speedValue.ForeColor = Theme.Muted;
        _speedValue.TextAlign = ContentAlignment.MiddleLeft;
        var speedNames = Column();
        speedNames.Controls.Add(speedTitle);
        speedNames.Controls.Add(_speedValue);
        _speedSlider.BackColor = Theme.Surface;
        _speedSlider.Width = Px(130);
        _speedSlider.Margin = new Padding(Px(10), 0, 0, 0);
        var speedRow = Line(inner, speedNames, Spacer(), _speedSlider);
        _speedSlider.Dock = DockStyle.None;
        _speedSlider.Anchor = AnchorStyles.Right;
        pointer.Controls.Add(speedRow);
        pointer.Controls.Add(Rule(inner));
        _noAccel.BackColor = Theme.Surface;
        _noAccel.Width = inner;
        _noAccel.Margin = Padding.Empty;
        pointer.Controls.Add(_noAccel);
        pointer.Controls.Add(Note(T.PointerNote, inner - Px(50)));
        pointer.Controls.Add(Rule(inner));
        _startup.BackColor = Theme.Surface;
        _startup.Width = inner;
        _startup.Margin = Padding.Empty;
        pointer.Controls.Add(_startup);
        column.Controls.Add(pointer);

        var footer = Footer(w - Px(8), withStartup: false);
        footer.Margin = new Padding(Px(4), Px(12), 0, 0);
        column.Controls.Add(footer);
        return column;
    }

    /// <summary>A thin divider between rows of a card.</summary>
    private Control Rule(int width) =>
        new Panel { Width = width, Height = 1, BackColor = Theme.Border, Margin = new Padding(0, Px(10), 0, Px(6)) };

    /// <summary>An empty cell that takes the free width of a Line.</summary>
    private static Control Spacer() => new Stretch();

    /// <summary>Marks the cell of a Line that takes the free width.</summary>
    private sealed class Stretch : Control
    {
        public Stretch()
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            TabStop = false;
            Height = 1;
            Margin = Padding.Empty;
        }
    }
}
