using DareuMouse.Core;

namespace DareuMouse;

/// <summary>Compact: quick-settings tiles and a DPI slider; "More settings" opens everything else.</summary>
internal sealed partial class PanelForm
{
    private bool _moreOpen;

    private Control BuildCompact()
    {
        var w = Width340;
        var column = Column(w);
        var gap = Px(10);
        var tileW = (w - gap * 2) / 3;
        var tileH = Px(60);

        Pill Tile(string caption, Action onClick, Action? onRightClick = null)
        {
            var tile = new Pill { Caption = caption, Width = tileW, Height = tileH, Margin = Padding.Empty, BackColor = Theme.Background, AccessibleName = caption };
            tile.MouseUp += (_, e) =>
            {
                if (e.Button == MouseButtons.Left) onClick();
                else if (e.Button == MouseButtons.Right) onRightClick?.Invoke();
            };
            return tile;
        }

        var tileDpi = Tile("DPI", () => _ = StepStageAsync(1), () => _ = StepStageAsync(-1));
        var tileHz = Tile("Hz", () => _ = StepPollingAsync());
        var tileSpeed = Tile(T.Speed, () => { });   // opens "More settings", wired below
        var tileAccel = Tile(T.TileAccelCaption, () => _noAccel.Checked = !_noAccel.Checked);
        var tileStartup = Tile(T.TileStartupCaption, () => _startup.Checked = !_startup.Checked);
        var tileCopy = Tile(T.TileCopyCaption, CopyDiagnostics);
        tileDpi.AccessibleDescription = T.NextStage;
        tileAccel.Text = T.TileAccel;
        tileStartup.Text = T.TileStartup;
        tileCopy.Text = T.TileCopy;

        var grid = new TableLayoutPanel { ColumnCount = 3, RowCount = 2, AutoSize = true, Margin = Padding.Empty, BackColor = Color.Transparent };
        for (var i = 0; i < 3; i++) grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, tileW + (i < 2 ? gap : 0)));
        Pill[] tiles = [tileDpi, tileHz, tileSpeed, tileAccel, tileStartup, tileCopy];
        for (var i = 0; i < tiles.Length; i++)
        {
            tiles[i].Margin = new Padding(0, 0, i % 3 < 2 ? gap : 0, i < 3 ? gap : 0);
            grid.Controls.Add(tiles[i], i % 3, i / 3);
        }
        column.Controls.Add(grid);
        column.Controls.Add(_status);

        // DPI of the active stage.
        var dpiLabel = NewLabel(Theme.Section, Theme.Muted);
        dpiLabel.Text = "DPI";
        dpiLabel.Margin = new Padding(Px(2), 0, Px(10), 0);
        var dpiValue = NewLabel(Theme.Title, Theme.Text);
        dpiValue.MinimumSize = new Size(Px(56), 0);
        dpiValue.TextAlign = ContentAlignment.MiddleRight;
        dpiValue.Margin = new Padding(Px(8), 0, 0, 0);
        _dpiSlider.Margin = Padding.Empty;
        var dpiRow = Line(w, dpiLabel, _dpiSlider, dpiValue);
        dpiRow.Margin = new Padding(0, Px(12), 0, 0);
        _mouseOnly.Add(dpiRow);
        column.Controls.Add(dpiRow);

        // Everything else, folded. The pointer part works even without the mouse.
        var more = Column(w);
        more.Margin = new Padding(0, Px(4), 0, 0);
        var dpiHeader = SectionHeader(T.Dpi, T.DpiHint, w, top: 8);
        more.Controls.Add(dpiHeader);
        SizeRow(_stagePills, w);
        more.Controls.Add(_stagePills);
        more.Controls.Add(_editLabel);
        var exact = NewLabel(Theme.Body, Theme.Text);
        exact.Text = T.ExactDpi;
        _dpiBox.Margin = new Padding(0, Px(6), 0, 0);
        exact.Margin = new Padding(0, Px(6), 0, 0);
        var exactRow = Line(w, exact, Spacer(), _dpiBox);
        more.Controls.Add(exactRow);
        var pollHeader = SectionHeader(T.Polling, null, w);
        more.Controls.Add(pollHeader);
        SizeRow(_pollingPills, w);
        more.Controls.Add(_pollingPills);
        more.Controls.Add(_pollingNote);
        Control[] mouseParts = [dpiHeader, _stagePills, exactRow, pollHeader, _pollingPills];

        more.Controls.Add(SectionHeader(T.Pointer, T.SpeedHint, w));
        var speedLabel = NewLabel(Theme.Body, Theme.Text);
        speedLabel.Text = T.Speed;
        speedLabel.Margin = new Padding(0, 0, Px(10), 0);
        _speedValue.MinimumSize = new Size(Px(100), 0);
        _speedValue.Margin = new Padding(Px(8), 0, 0, 0);
        more.Controls.Add(Line(w, speedLabel, _speedSlider, _speedValue));
        _noAccel.Width = w;
        _noAccel.Margin = new Padding(0, Px(6), 0, 0);
        more.Controls.Add(_noAccel);
        more.Controls.Add(Note(T.PointerNote, w));
        _startup.Width = w;
        _startup.Margin = new Padding(0, Px(4), 0, 0);
        more.Controls.Add(_startup);
        more.Visible = _moreOpen;
        column.Controls.Add(more);

        // Footer: mouse summary, theme, the fold and exit.
        var rule = new Panel { Width = w, Height = 1, BackColor = Theme.Border, Margin = new Padding(0, Px(14), 0, Px(10)) };
        column.Controls.Add(rule);
        var info = NewLabel(Theme.Small, Theme.Muted);
        LinkLabel? moreLink = null;
        moreLink = Link(_moreOpen ? T.Less : T.More, () => SetMore(!_moreOpen));
        LinkLabel? theme = null;
        theme = Link(T.Theme, () => ShowThemeMenu(theme!));
        var exit = Link(T.Exit, () => ExitRequested?.Invoke());
        column.Controls.Add(Line(w, info, Spacer(), theme, moreLink, exit));

        void SetMore(bool open)
        {
            _moreOpen = open;
            moreLink!.Text = open ? T.Less : T.More;
            more.Visible = open;
            Render();
        }

        tileSpeed.MouseUp += (_, e) => { if (e.Button == MouseButtons.Left) SetMore(true); };

        _renderTheme = () =>
        {
            var s = _state;
            var ok = s.Status == MouseStatus.Ok;
            tileDpi.Text = ok ? FormatDpi(s.Stages[s.ActiveStage]) : "–";
            tileHz.Text = ok ? FormatHz(s.Polling) : "–";
            tileDpi.Active = tileHz.Active = ok;
            tileDpi.Enabled = tileHz.Enabled = ok;
            tileSpeed.Text = _speedSlider.Value.ToString();
            tileAccel.Active = _noAccel.Checked;
            tileStartup.Active = _startup.Checked;
            dpiValue.Text = FormatDpi((int)_dpiBox.Value);
            info.Text = ok
                ? $"{T.Title} · {(s.Charging ? "⚡ " : "")}{s.Battery} % · {(s.Wired ? T.Wired : T.Wireless)}"
                : T.Title;
            info.ForeColor = ok && !s.Charging && s.Battery <= 15 ? Theme.Urgent : Theme.Muted;
            foreach (var part in mouseParts) part.Visible = ok;
            if (!ok) _editLabel.Visible = _pollingNote.Visible = false;
        };
        _startup.CheckedChanged += (_, _) => _renderTheme?.Invoke();
        _noAccel.CheckedChanged += (_, _) => _renderTheme?.Invoke();
        return column;
    }
}
