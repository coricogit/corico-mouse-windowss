using System.Globalization;
using DareuMouse.Core;

namespace DareuMouse;

/// <summary>The panel that opens from the tray icon.</summary>
internal sealed class PanelForm : Form
{
    private readonly MouseService _service;
    private readonly SemaphoreSlim _busy = new(1, 1);
    private readonly System.Windows.Forms.Timer _refreshTimer = new();
    private readonly System.Windows.Forms.Timer _dpiDebounce = new() { Interval = 350 };
    private readonly System.Windows.Forms.Timer _speedDebounce = new() { Interval = 250 };

    private readonly Label _subtitle = new();
    private readonly Label _battery = new();
    private readonly Label _status = new();
    private readonly Panel _mouseSections = new();
    private readonly FlowLayoutPanel _stagePills = new();
    private readonly Label _editLabel = new();
    private readonly TrackBar _dpiSlider = new();
    private readonly NumericUpDown _dpiBox = new();
    private readonly FlowLayoutPanel _pollingPills = new();
    private readonly Label _pollingNote = new();
    private readonly TrackBar _speedSlider = new();
    private readonly Label _speedValue = new();
    private readonly CheckBox _noAccel = new();
    private readonly CheckBox _startup = new();

    private MouseState _state = MouseState.Of(MouseStatus.Loading);
    private int _editStage = -1;
    private bool _updating;
    private bool _warnedLowBattery;
    private DateTime _hiddenAt;
    private bool _anchorBottom;
    private int _anchorEdge;

    /// <summary>Raised on the UI thread whenever a new mouse state arrives.</summary>
    public event Action<MouseState>? StateChanged;

    public event Action? ExitRequested;

    public MouseState State => _state;

    public PanelForm(MouseService service)
    {
        _service = service;
        Text = "Dareu Mouse";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = Theme.Body;
        AutoScaleMode = AutoScaleMode.None;
        Padding = new Padding(Px(16));
        KeyPreview = true;
        ResizeRedraw = true;

        BuildLayout();

        _refreshTimer.Tick += async (_, _) => await RefreshAsync();
        _dpiDebounce.Tick += async (_, _) => { _dpiDebounce.Stop(); await CommitDpiAsync(); };
        _speedDebounce.Tick += (_, _) => { _speedDebounce.Stop(); CommitSpeed(); };
        SetRefreshInterval(visible: false);
        _refreshTimer.Start();
    }

    private int Px(int logical) => LogicalToDeviceUnits(logical);

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ClassStyle |= 0x00020000; // CS_DROPSHADOW
            return cp;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(Theme.Border);
        e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }

    // ---------- Layout ----------

    private void BuildLayout()
    {
        var width = Px(340);
        var column = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Location = new Point(Padding.Left, Padding.Top),
            BackColor = Theme.Background,
            Margin = Padding.Empty,
        };

        // Header: name and connection on the left, battery on the right.
        var header = new TableLayoutPanel { ColumnCount = 2, RowCount = 2, MinimumSize = new Size(width, 0), MaximumSize = new Size(width, 0), AutoSize = true, Margin = Padding.Empty };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var title = new Label { Text = T.Title, Font = Theme.Title, AutoSize = true, Margin = Padding.Empty };
        Style(_subtitle, Theme.Small, Theme.Muted);
        _battery.Font = Theme.Title;
        _battery.AutoSize = true;
        _battery.Anchor = AnchorStyles.Right;
        _battery.Margin = Padding.Empty;
        header.Controls.Add(title, 0, 0);
        header.Controls.Add(_battery, 1, 0);
        header.Controls.Add(_subtitle, 0, 1);
        column.Controls.Add(header);

        Style(_status, Theme.Body, Theme.Warning);
        _status.MaximumSize = new Size(width, 0);
        _status.Margin = new Padding(0, Px(10), 0, 0);
        column.Controls.Add(_status);

        // Mouse settings (hidden while the mouse is asleep or missing).
        var mouseColumn = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = Padding.Empty,
        };
        mouseColumn.Controls.Add(SectionHeader(T.Dpi, T.DpiHint, width));
        Row(_stagePills, width);
        mouseColumn.Controls.Add(_stagePills);

        Style(_editLabel, Theme.Small, Theme.Muted);
        _editLabel.Margin = new Padding(0, Px(8), 0, 0);
        mouseColumn.Controls.Add(_editLabel);

        var dpiRow = new TableLayoutPanel { ColumnCount = 2, MinimumSize = new Size(width, 0), MaximumSize = new Size(width, 0), AutoSize = true, Margin = Padding.Empty };
        dpiRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        dpiRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _dpiSlider.Minimum = Protocol.DpiMin / Protocol.DpiStep;
        _dpiSlider.Maximum = Protocol.DpiMax / Protocol.DpiStep;
        _dpiSlider.SmallChange = 1;
        _dpiSlider.LargeChange = 8;
        _dpiSlider.TickStyle = TickStyle.None;
        _dpiSlider.Dock = DockStyle.Fill;
        _dpiSlider.BackColor = Theme.Background;
        _dpiSlider.ValueChanged += (_, _) =>
        {
            if (_updating) return;
            SetDpiControls(_dpiSlider.Value * Protocol.DpiStep, fromSlider: true);
        };
        _dpiBox.Minimum = Protocol.DpiMin;
        _dpiBox.Maximum = Protocol.DpiMax;
        _dpiBox.Increment = Protocol.DpiStep;
        _dpiBox.Width = Px(84);
        _dpiBox.BackColor = Theme.Surface;
        _dpiBox.ForeColor = Theme.Text;
        _dpiBox.BorderStyle = BorderStyle.FixedSingle;
        _dpiBox.TextAlign = HorizontalAlignment.Right;
        _dpiBox.Anchor = AnchorStyles.Right;
        _dpiBox.ValueChanged += (_, _) =>
        {
            if (_updating) return;
            SetDpiControls((int)_dpiBox.Value, fromSlider: false);
        };
        dpiRow.Controls.Add(_dpiSlider, 0, 0);
        dpiRow.Controls.Add(_dpiBox, 1, 0);
        mouseColumn.Controls.Add(dpiRow);

        mouseColumn.Controls.Add(SectionHeader(T.Polling, null, width));
        Row(_pollingPills, width);
        mouseColumn.Controls.Add(_pollingPills);
        Style(_pollingNote, Theme.Small, Theme.Warning);
        _pollingNote.MaximumSize = new Size(width, 0);
        _pollingNote.Margin = new Padding(0, Px(6), 0, 0);
        mouseColumn.Controls.Add(_pollingNote);

        _mouseSections.AutoSize = true;
        _mouseSections.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _mouseSections.Margin = Padding.Empty;
        _mouseSections.Controls.Add(mouseColumn);
        column.Controls.Add(_mouseSections);

        // Windows pointer settings: always available.
        column.Controls.Add(SectionHeader(T.Pointer, T.SpeedHint, width));
        var speedRow = new TableLayoutPanel { ColumnCount = 3, MinimumSize = new Size(width, 0), MaximumSize = new Size(width, 0), AutoSize = true, Margin = Padding.Empty };
        speedRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        speedRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        speedRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var speedLabel = new Label { Text = T.Speed, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, Px(8), 0) };
        _speedSlider.Minimum = WindowsPointer.SpeedMin;
        _speedSlider.Maximum = WindowsPointer.SpeedMax;
        _speedSlider.TickStyle = TickStyle.None;
        _speedSlider.Dock = DockStyle.Fill;
        _speedSlider.BackColor = Theme.Background;
        _speedSlider.ValueChanged += (_, _) =>
        {
            ShowSpeed(_speedSlider.Value);
            if (_updating) return;
            _speedDebounce.Stop();
            _speedDebounce.Start();
        };
        _speedSlider.MouseUp += (_, e) =>
        {
            if (e.Button == MouseButtons.Right) _speedSlider.Value = WindowsPointer.SpeedNeutral;
        };
        Style(_speedValue, Theme.Body, Theme.Text);
        _speedValue.Anchor = AnchorStyles.Right;
        _speedValue.MinimumSize = new Size(Px(110), 0);
        _speedValue.TextAlign = ContentAlignment.MiddleRight;
        speedRow.Controls.Add(speedLabel, 0, 0);
        speedRow.Controls.Add(_speedSlider, 1, 0);
        speedRow.Controls.Add(_speedValue, 2, 0);
        column.Controls.Add(speedRow);

        CheckStyle(_noAccel, T.NoAccel, width);
        _noAccel.CheckedChanged += (_, _) =>
        {
            if (_updating) return;
            Try(() => WindowsPointer.SetAcceleration(!_noAccel.Checked));
            LoadPointer();
        };
        column.Controls.Add(_noAccel);
        var pointerNote = new Label { Text = T.PointerNote };
        Style(pointerNote, Theme.Small, Theme.Muted);
        pointerNote.Margin = new Padding(Px(20), 0, 0, 0);
        pointerNote.MaximumSize = new Size(width - Px(20), 0);
        column.Controls.Add(pointerNote);

        // Footer.
        var line = new Panel { Height = 1, Width = width, BackColor = Theme.Border, Margin = new Padding(0, Px(14), 0, Px(8)) };
        column.Controls.Add(line);
        var footer = new TableLayoutPanel { ColumnCount = 3, MinimumSize = new Size(width, 0), MaximumSize = new Size(width, 0), AutoSize = true, Margin = Padding.Empty };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        CheckStyle(_startup, T.StartWithWindows, 0);
        _startup.Margin = Padding.Empty;
        _startup.CheckedChanged += (_, _) =>
        {
            if (_updating) return;
            Try(() => Startup.Enabled = _startup.Checked);
        };
        var diagnose = Link(T.CopyDiagnostics, CopyDiagnostics);
        var exit = Link(T.Exit, () => ExitRequested?.Invoke());
        footer.Controls.Add(_startup, 0, 0);
        footer.Controls.Add(diagnose, 1, 0);
        footer.Controls.Add(exit, 2, 0);
        column.Controls.Add(footer);

        Controls.Add(column);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
    }

    private Control SectionHeader(string text, string? hint, int width)
    {
        var row = new TableLayoutPanel { ColumnCount = 2, MinimumSize = new Size(width, 0), MaximumSize = new Size(width, 0), AutoSize = true, Margin = new Padding(0, Px(16), 0, Px(6)) };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var label = new Label { Text = text.ToUpper(CultureInfo.CurrentCulture) };
        Style(label, Theme.Section, Theme.Muted);
        row.Controls.Add(label, 0, 0);
        if (hint is not null)
        {
            var h = new Label { Text = hint, Anchor = AnchorStyles.Right };
            Style(h, Theme.Small, Theme.Muted);
            row.Controls.Add(h, 1, 0);
        }
        return row;
    }

    private void Row(FlowLayoutPanel row, int width)
    {
        row.FlowDirection = FlowDirection.LeftToRight;
        row.WrapContents = false;
        row.Width = width;
        row.Height = Px(34);
        row.Margin = Padding.Empty;
        row.BackColor = Theme.Background;
    }

    private static void Style(Label label, Font font, Color color)
    {
        label.Font = font;
        label.ForeColor = color;
        label.AutoSize = true;
        label.Margin = Padding.Empty;
    }

    private void CheckStyle(CheckBox box, string text, int width)
    {
        box.Text = text;
        box.AutoSize = true;
        if (width > 0) box.MaximumSize = new Size(width, 0);
        box.ForeColor = Theme.Text;
        box.FlatStyle = FlatStyle.System;
        box.Margin = new Padding(0, Px(8), 0, 0);
        box.Cursor = Cursors.Hand;
    }

    private LinkLabel Link(string text, Action action)
    {
        var link = new LinkLabel
        {
            Text = text, AutoSize = true, LinkColor = Theme.Accent, ActiveLinkColor = Theme.Text,
            LinkBehavior = LinkBehavior.HoverUnderline, Anchor = AnchorStyles.Right, Margin = new Padding(Px(10), 0, 0, 0),
        };
        link.LinkClicked += (_, _) => action();
        return link;
    }

    private Pill NewPill(string text, int width)
    {
        return new Pill { Text = text, Width = width, Height = Px(30), Margin = new Padding(0, 0, Px(6), 0) };
    }

    // ---------- Showing and hiding ----------

    public void Toggle()
    {
        if (Visible) { HidePanel(); return; }
        // A click on the tray icon first deactivates the panel; don't reopen it right away.
        if ((DateTime.UtcNow - _hiddenAt).TotalMilliseconds < 300) return;
        ShowPanel();
    }

    public void ShowPanel()
    {
        LoadPointer();
        _updating = true;
        _startup.Checked = SafeGet(() => Startup.Enabled);
        _updating = false;
        Render();
        PerformLayout();
        PlaceNearTray();
        Show();
        Activate();
        ActiveControl = null;
        SetRefreshInterval(visible: true);
        _ = RefreshAsync();
    }

    private void HidePanel()
    {
        _hiddenAt = DateTime.UtcNow;
        _editStage = -1;
        Hide();
        SetRefreshInterval(visible: false);
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        if (Visible) HidePanel();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Escape) HidePanel();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; HidePanel(); return; }
        base.OnFormClosing(e);
    }

    private void PlaceNearTray()
    {
        var cursor = Cursor.Position;
        var area = Screen.FromPoint(cursor).WorkingArea;
        var gap = Px(10);
        var x = Math.Clamp(cursor.X - Width / 2, area.Left + gap, area.Right - Width - gap);
        _anchorBottom = cursor.Y > area.Top + area.Height / 2;
        _anchorEdge = _anchorBottom ? area.Bottom - gap : area.Top + gap;
        Location = new Point(x, _anchorBottom ? _anchorEdge - Height : _anchorEdge);
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        // Keep the panel against the taskbar when a section appears or disappears.
        if (Visible && _anchorBottom) Top = _anchorEdge - Height;
    }

    private void SetRefreshInterval(bool visible)
    {
        // Every 10 s while open, every minute in the tray for the icon and the low-battery notice.
        _refreshTimer.Interval = visible ? 10_000 : 60_000;
    }

    // ---------- Mouse actions ----------

    public Task RefreshAsync() => RunAsync(_service.ReadState, skipIfBusy: true);

    private async Task RunAsync(Func<MouseState> operation, bool skipIfBusy = false)
    {
        if (skipIfBusy && _busy.CurrentCount == 0) return;
        await _busy.WaitAsync();
        try
        {
            var state = await Task.Run(operation);
            Apply(state);
        }
        catch (Exception e)
        {
            Apply(MouseState.Of(MouseStatus.Error, e.Message));
        }
        finally
        {
            _busy.Release();
        }
    }

    private void Apply(MouseState state)
    {
        _state = state;
        if (state.Status == MouseStatus.Ok)
        {
            if (_editStage >= state.Stages.Count) _editStage = -1;
            if (state.Charging || state.Battery > 20) _warnedLowBattery = false;
        }
        Render();
        StateChanged?.Invoke(state);
    }

    public bool ShouldWarnLowBattery()
    {
        if (_state.Status != MouseStatus.Ok || _state.Charging || _state.Battery > 15 || _warnedLowBattery) return false;
        _warnedLowBattery = true;
        return true;
    }

    private int ShownStage => _editStage >= 0 ? _editStage : _state.ActiveStage;

    private void SetDpiControls(int dpi, bool fromSlider)
    {
        dpi = Protocol.ClampDpi(dpi);
        _updating = true;
        if (!fromSlider) _dpiSlider.Value = dpi / Protocol.DpiStep;
        _dpiBox.Value = dpi;
        _updating = false;
        var stage = ShownStage;
        if (stage >= 0 && stage < _stagePills.Controls.Count) _stagePills.Controls[stage].Text = FormatDpi(dpi);
        _dpiDebounce.Stop();
        _dpiDebounce.Start();
    }

    private async Task CommitDpiAsync()
    {
        var stage = ShownStage;
        var dpi = (int)_dpiBox.Value;
        if (_state.Status != MouseStatus.Ok || stage < 0 || stage >= _state.Stages.Count || _state.Stages[stage] == dpi) return;
        await RunAsync(() => _service.SetStageDpi(stage, dpi));
    }

    private async Task SelectStageAsync(int stage)
    {
        _editStage = -1;
        if (stage == _state.ActiveStage) { Render(); return; }
        _state = _state with { ActiveStage = stage };
        Render();
        await RunAsync(() => _service.SetActiveStage(stage));
    }

    private void EditStage(int stage)
    {
        _editStage = stage == _state.ActiveStage ? -1 : stage;
        Render();
    }

    private async Task SetPollingAsync(int hz)
    {
        if (hz == _state.Polling) return;
        _state = _state with { Polling = hz };
        Render();
        await RunAsync(() => _service.SetPolling(hz));
    }

    // ---------- Windows pointer ----------

    private void LoadPointer()
    {
        _updating = true;
        _speedSlider.Value = Math.Clamp(SafeGet(WindowsPointer.GetSpeed, WindowsPointer.SpeedNeutral), WindowsPointer.SpeedMin, WindowsPointer.SpeedMax);
        _noAccel.Checked = !SafeGet(WindowsPointer.GetAcceleration, true);
        _updating = false;
        ShowSpeed(_speedSlider.Value);
    }

    private void CommitSpeed() => Try(() => WindowsPointer.SetSpeed(_speedSlider.Value));

    private void ShowSpeed(int speed) =>
        _speedValue.Text = speed == WindowsPointer.SpeedNeutral ? $"{speed} · {T.SpeedNeutral}" : speed.ToString(CultureInfo.CurrentCulture);

    // ---------- Rendering ----------

    private static string FormatDpi(int dpi) => dpi.ToString("N0", CultureInfo.CurrentCulture);

    private static string FormatHz(int hz) => hz >= 1000 ? $"{hz / 1000}K" : hz.ToString(CultureInfo.CurrentCulture);

    private void Render()
    {
        SuspendLayout();
        var s = _state;
        var ok = s.Status == MouseStatus.Ok;

        _subtitle.Text = ok ? $"{(s.Wired ? T.Wired : T.Wireless)} · {s.Polling:N0} Hz".ToUpper(CultureInfo.CurrentCulture) : "";
        _battery.Visible = ok;
        if (ok)
        {
            _battery.Text = s.Charging ? $"⚡ {s.Battery} %" : $"{s.Battery} %";
            _battery.ForeColor = !s.Charging && s.Battery <= 15 ? Theme.Urgent : Theme.Text;
        }

        _status.Text = s.Status switch
        {
            MouseStatus.Loading => T.Loading,
            MouseStatus.Asleep => T.Asleep,
            MouseStatus.NoDevice => T.NoDevice,
            MouseStatus.Error => T.ErrorPrefix + s.Error,
            _ => s.Error is null ? "" : T.ErrorPrefix + s.Error,
        };
        _status.ForeColor = s.Status == MouseStatus.Error || s.Error is not null ? Theme.Urgent : Theme.Warning;
        _status.Visible = _status.Text.Length > 0;

        _mouseSections.Visible = ok;
        if (ok)
        {
            RenderStages();
            RenderPolling();
        }
        ResumeLayout(true);
    }

    private void RenderStages()
    {
        var s = _state;
        var gap = Px(6);
        var width = (_stagePills.Width - gap * (Protocol.MaxStages - 1)) / Protocol.MaxStages;
        while (_stagePills.Controls.Count > s.Stages.Count) _stagePills.Controls[^1].Dispose();
        while (_stagePills.Controls.Count < s.Stages.Count)
        {
            var index = _stagePills.Controls.Count;
            var pill = NewPill("", width);
            pill.MouseUp += async (_, e) =>
            {
                if (e.Button == MouseButtons.Right) EditStage(index);
                else if (e.Button == MouseButtons.Left) await SelectStageAsync(index);
            };
            _stagePills.Controls.Add(pill);
        }
        for (var i = 0; i < s.Stages.Count; i++)
        {
            var pill = (Pill)_stagePills.Controls[i];
            pill.Text = FormatDpi(s.Stages[i]);
            pill.Active = i == s.ActiveStage;
            pill.Marked = i == _editStage;
        }

        var stage = ShownStage;
        _editLabel.Text = _editStage >= 0 ? T.EditingStage(stage + 1) : "";
        _editLabel.Visible = _editStage >= 0;
        if (!_dpiDebounce.Enabled && stage >= 0)
        {
            _updating = true;
            _dpiSlider.Value = s.Stages[stage] / Protocol.DpiStep;
            _dpiBox.Value = s.Stages[stage];
            _updating = false;
        }
    }

    private void RenderPolling()
    {
        var s = _state;
        var options = s.PollingOptions;
        var gap = Px(6);
        var width = (_pollingPills.Width - gap * (6 - 1)) / 6;
        if (_pollingPills.Controls.Count != options.Count
            || !_pollingPills.Controls.Cast<Control>().Select(c => (int)c.Tag!).SequenceEqual(options))
        {
            while (_pollingPills.Controls.Count > 0) _pollingPills.Controls[0].Dispose();
            foreach (var hz in options)
            {
                var pill = NewPill(FormatHz(hz), width);
                pill.Tag = hz;
                pill.MouseUp += async (_, e) => { if (e.Button == MouseButtons.Left) await SetPollingAsync(hz); };
                _pollingPills.Controls.Add(pill);
            }
        }
        foreach (Pill pill in _pollingPills.Controls) pill.Active = (int)pill.Tag! == s.Polling;

        _pollingNote.Text = s.Polling > 1000 ? T.PollingWarning : s.Wired ? T.WiredPollingNote : "";
        _pollingNote.ForeColor = s.Polling > 1000 ? Theme.Warning : Theme.Muted;
        _pollingNote.Visible = _pollingNote.Text.Length > 0;
    }

    // ---------- Helpers ----------

    private void CopyDiagnostics()
    {
        var text = $"Dareu Mouse {Application.ProductVersion}\r\nWindows {Environment.OSVersion.Version}\r\n"
            + $"Estado: {_state.Status} {_state.Error}\r\n"
            + (_state.Status == MouseStatus.Ok
                ? $"Batería {_state.Battery} % carga={_state.Charging} cable={_state.Wired} {_state.Polling} Hz "
                  + $"DPI [{string.Join(", ", _state.Stages)}] activo={_state.ActiveStage + 1}\r\n"
                : "")
            + "\r\n" + SafeGet(WinHid.Diagnose, "(no se pudieron listar los HID)");
        Try(() => Clipboard.SetText(text));
        _status.Text = T.Copied;
        _status.ForeColor = Theme.Muted;
        _status.Visible = true;
    }

    private void Try(Action action)
    {
        try { action(); }
        catch (Exception e)
        {
            _status.Text = T.ErrorPrefix + e.Message;
            _status.ForeColor = Theme.Urgent;
            _status.Visible = true;
        }
    }

    private static TValue SafeGet<TValue>(Func<TValue> get, TValue fallback = default!)
    {
        try { return get(); } catch { return fallback; }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _refreshTimer.Dispose();
            _dpiDebounce.Dispose();
            _speedDebounce.Dispose();
        }
        base.Dispose(disposing);
    }
}
