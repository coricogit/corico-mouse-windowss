using DareuMouse.Core;

namespace DareuMouse;

/// <summary>Cards: battery ring, the DPI in big with − and +, and the Windows pointer folded away.</summary>
internal sealed partial class PanelForm
{
    private bool _pointerOpen;

    private Control BuildCards()
    {
        var w = Width340;
        var column = Column(w);
        var pad = Px(16);
        var gap = Px(10);

        // Header: battery ring, name and connection.
        var ring = new BatteryRing { Size = new Size(Px(64), Px(64)), Margin = new Padding(0, 0, Px(14), 0) };
        var title = NewLabel(Theme.Title, Theme.Text);
        title.Text = T.Title;
        _subtitle.Margin = new Padding(0, Px(4), 0, 0);
        var names = Column();
        names.Controls.Add(title);
        names.Controls.Add(_subtitle);
        var header = Line(w, ring, names);
        header.Margin = new Padding(Px(4), 0, 0, Px(4));
        column.Controls.Add(header);
        column.Controls.Add(_status);

        // DPI card: big number, − and +, stage bars, exact value.
        var dpi = new Card(w, pad) { Margin = new Padding(0, Px(8), 0, 0) };
        var inner = dpi.Inner;
        var dpiTitle = NewLabel(Theme.Section, Theme.Muted);
        dpiTitle.Text = "DPI";
        var stageInfo = NewLabel(Theme.Small, Theme.Muted);
        stageInfo.Anchor = AnchorStyles.Right;
        dpi.Controls.Add(Line(inner, dpiTitle, Spacer(), stageInfo));

        var minus = RoundButton("−", T.PreviousStage, () => StepStageAsync(-1));
        var plus = RoundButton("+", T.NextStage, () => StepStageAsync(1));
        var big = new Label
        {
            AutoSize = false, Height = Px(64), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter,
            Font = Theme.Display, ForeColor = Theme.Accent, BackColor = Color.Transparent, Margin = Padding.Empty,
        };
        var bigRow = new TableLayoutPanel { ColumnCount = 3, MinimumSize = new Size(inner, 0), MaximumSize = new Size(inner, 0), AutoSize = true, Margin = new Padding(0, Px(6), 0, Px(8)), BackColor = Color.Transparent };
        bigRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bigRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bigRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bigRow.Controls.Add(minus, 0, 0);
        bigRow.Controls.Add(big, 1, 0);
        bigRow.Controls.Add(plus, 2, 0);
        dpi.Controls.Add(bigRow);

        SizeRow(_stagePills, inner, Px(36));
        dpi.Controls.Add(_stagePills);
        dpi.Controls.Add(_editLabel);
        _dpiSlider.BackColor = Theme.Surface;
        _dpiSlider.Margin = new Padding(0, Px(8), Px(10), 0);
        _dpiBox.Margin = new Padding(0, Px(8), 0, 0);
        dpi.Controls.Add(Line(inner, _dpiSlider, _dpiBox));
        _mouseOnly.Add(dpi);
        column.Controls.Add(dpi);

        // Polling card.
        var polling = new Card(w, pad) { Margin = new Padding(0, gap, 0, 0) };
        var pollTitle = NewLabel(Theme.Section, Theme.Muted);
        pollTitle.Text = T.Polling.ToUpperInvariant();
        polling.Controls.Add(pollTitle);
        SizeRow(_pollingPills, inner, Px(36));
        _pollingPills.Margin = new Padding(0, Px(8), 0, 0);
        polling.Controls.Add(_pollingPills);
        _pollingNote.MaximumSize = new Size(inner, 0);
        polling.Controls.Add(_pollingNote);
        _mouseOnly.Add(polling);
        column.Controls.Add(polling);

        // Windows pointer: one line that opens to the settings.
        var pointer = new Card(w, pad) { Margin = new Padding(0, gap, 0, 0) };
        var pointerTitle = NewLabel(Theme.Section, Theme.Muted);
        pointerTitle.Text = T.Pointer.ToUpperInvariant();
        var summary = NewLabel(Theme.Body, Theme.Text);
        summary.Margin = new Padding(0, Px(3), 0, 0);
        var names2 = Column();
        names2.Controls.Add(pointerTitle);
        names2.Controls.Add(summary);
        var chevron = new Pill { Text = "›", Width = Px(32), Height = Px(32), BackColor = Theme.Surface, AccessibleName = T.Pointer, Font = Theme.Title, Anchor = AnchorStyles.Right };
        var toggleRow = Line(inner, names2, Spacer(), chevron);
        pointer.Controls.Add(toggleRow);

        var body = Column(inner);
        body.Margin = new Padding(0, Px(10), 0, 0);
        var speedLabel = NewLabel(Theme.Body, Theme.Text);
        speedLabel.Text = T.Speed;
        speedLabel.Margin = new Padding(0, 0, Px(10), 0);
        _speedSlider.BackColor = Theme.Surface;
        _speedValue.Margin = new Padding(Px(8), 0, 0, 0);
        body.Controls.Add(Line(inner, speedLabel, _speedSlider, _speedValue));
        _noAccel.BackColor = Theme.Surface;
        _noAccel.Width = inner;
        _noAccel.Margin = new Padding(0, Px(6), 0, 0);
        body.Controls.Add(_noAccel);
        var note = Note(T.PointerNote, inner - Px(24));
        note.Margin = new Padding(Px(24), 0, 0, 0);
        body.Controls.Add(note);
        body.Visible = _pointerOpen;
        pointer.Controls.Add(body);
        column.Controls.Add(pointer);

        void TogglePointer()
        {
            _pointerOpen = !_pointerOpen;
            body.Visible = _pointerOpen;
            chevron.Text = _pointerOpen ? "˅" : "›";
        }
        chevron.Text = _pointerOpen ? "˅" : "›";
        chevron.MouseUp += (_, e) => { if (e.Button == MouseButtons.Left) TogglePointer(); };
        foreach (var c in new Control[] { toggleRow, names2, pointerTitle, summary })
        {
            c.Cursor = Cursors.Hand;
            c.Click += (_, _) => TogglePointer();
        }

        var footer = Footer(w - Px(8));
        footer.Margin = new Padding(Px(4), Px(12), 0, 0);
        column.Controls.Add(footer);

        _renderTheme = () =>
        {
            var s = _state;
            var ok = s.Status == MouseStatus.Ok;
            ring.Known = ok;
            ring.Battery = ok ? s.Battery : 0;
            ring.Charging = ok && s.Charging;
            ring.Invalidate();
            if (ok)
            {
                big.Text = FormatDpi((int)_dpiBox.Value);
                stageInfo.Text = T.StageOf(ShownStage + 1, s.Stages.Count);
            }
            summary.Text = T.PointerSummary(_speedSlider.Value, _noAccel.Checked);
        };
        return column;
    }

    private Pill RoundButton(string text, string name, Func<Task> action)
    {
        var button = new Pill
        {
            Text = text, Outlined = true, Width = Px(44), Height = Px(44), Font = Theme.Title,
            BackColor = Theme.Surface, AccessibleName = name, Margin = Padding.Empty, Anchor = AnchorStyles.None,
        };
        button.MouseUp += async (_, e) => { if (e.Button == MouseButtons.Left) await action(); };
        return button;
    }
}
