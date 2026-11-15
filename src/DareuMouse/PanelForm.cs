using System.Globalization;
using DareuMouse.Core;

namespace DareuMouse;

/// <summary>
/// The panel that opens from the tray icon. This file holds the state and the mouse and pointer actions;
/// each theme lays the shared controls out in its own file under Layouts/.
/// </summary>
internal sealed partial class PanelForm : Form
{
    private readonly MouseService _service;
    private readonly SemaphoreSlim _busy = new(1, 1);
    private readonly System.Windows.Forms.Timer _refreshTimer = new();
    private readonly System.Windows.Forms.Timer _dpiDebounce = new() { Interval = 350 };
    private readonly System.Windows.Forms.Timer _speedDebounce = new() { Interval = 250 };

    // Shared controls, created again for every theme (see CreateControls).
    private Label _subtitle = null!;
    private Label _battery = null!;
    private Label _status = null!;
    private FlowLayoutPanel _stagePills = null!;
    private Label _editLabel = null!;
    private Slider _dpiSlider = null!;
    private NumericUpDown _dpiBox = null!;
    private FlowLayoutPanel _pollingPills = null!;
    private Label _pollingNote = null!;
    private Slider _speedSlider = null!;
    private Label _speedValue = null!;
    private Switch _noAccel = null!;
    private Switch _startup = null!;

    /// <summary>Controls that only make sense while the mouse answers (hidden when asleep or missing).</summary>
    private readonly List<Control> _mouseOnly = [];

    /// <summary>What the current theme draws on top of the shared controls (battery ring, tiles...).</summary>
    private Action? _renderTheme;

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

    /// <summary>Raised after the theme changed (the tray icon follows it).</summary>
    public event Action? ThemeChanged;

    public MouseState State => _state;

    private int Width340 => Px(340);

    public PanelForm(MouseService service)
    {
        _service = service;
        Text = "Dareu Mouse";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        KeyPreview = true;
        ResizeRedraw = true;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        BuildForTheme();

        _refreshTimer.Tick += async (_, _) => await RefreshAsync();
        _dpiDebounce.Tick += async (_, _) => { _dpiDebounce.Stop(); await CommitDpiAsync(); };
        _speedDebounce.Tick += (_, _) => { _speedDebounce.Stop(); CommitSpeed(); };
        SetRefreshInterval(visible: false);
        _refreshTimer.Start();
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
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

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        WindowsLook.SetCorners(Handle, rounded: Theme.Kind != ThemeKind.Pixel);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (Theme.Kind == ThemeKind.Pixel)
        {
            // Black frame with a lighter inner line, like a pixel-art window.
            using var black = new Pen(Color.Black, Px(2)) { Alignment = System.Drawing.Drawing2D.PenAlignment.Inset };
            e.Graphics.DrawRectangle(black, 0, 0, Width - 1, Height - 1);
            using var inner = new Pen(Theme.Border, 1);
            e.Graphics.DrawRectangle(inner, Px(2), Px(2), Width - 1 - Px(4), Height - 1 - Px(4));
        }
        else if (Environment.OSVersion.Version.Build < 22000)
        {
            using var pen = new Pen(Theme.Border);
            e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }
    }

    // ---------- Themes ----------

    /// <summary>Saves the theme and rebuilds the panel with it.</summary>
    public void SetTheme(ThemeKind kind)
    {
        if (kind == Theme.Kind) return;
        Try(() => ThemeSettings.Save(kind));
        Theme.Apply(kind);
        Rebuild();
        ThemeChanged?.Invoke();
    }

    private void OnUserPreferenceChanged(object? sender, Microsoft.Win32.UserPreferenceChangedEventArgs e)
    {
        if (e.Category != Microsoft.Win32.UserPreferenceCategory.General) return;
        // Light/dark mode or the accent color changed: Fluent follows Windows, every tray icon follows the taskbar.
        BeginInvoke(() =>
        {
            if (Theme.Kind == ThemeKind.Fluent)
            {
                Theme.Apply(ThemeKind.Fluent);
                Rebuild();
            }
            ThemeChanged?.Invoke();
        });
    }

    private void Rebuild()
    {
        SuspendLayout();
        // Shared controls a theme did not place (e.g. Compact has no big battery label) have no parent: dispose them too.
        foreach (var c in new Control[] { _subtitle, _battery, _status, _stagePills, _editLabel, _dpiSlider, _dpiBox, _pollingPills, _pollingNote, _speedSlider, _speedValue, _noAccel, _startup })
            if (c.Parent is null) c.Dispose();
        var old = Controls.Cast<Control>().ToList();
        Controls.Clear();
        foreach (var c in old) c.Dispose();
        BuildForTheme();
        LoadPointer();
        _updating = true;
        _startup.Checked = SafeGet(() => Startup.Enabled);
        _updating = false;
        Render();
        ResumeLayout(true);
        if (IsHandleCreated) WindowsLook.SetCorners(Handle, rounded: Theme.Kind != ThemeKind.Pixel);
        if (Visible) KeepOnScreen();
        Invalidate(true);
    }

    private void BuildForTheme()
    {
        BackColor = Theme.Background;
        ForeColor = Theme.Text;
        Font = Theme.Body;
        Padding = new Padding(Px(Theme.Kind switch { ThemeKind.Pixel => 18, ThemeKind.Cards => 14, _ => 16 }));
        _mouseOnly.Clear();
        _renderTheme = null;
        CreateControls();
        var column = Theme.Kind switch
        {
            ThemeKind.Fluent => BuildFluent(),
            ThemeKind.Cards => BuildCards(),
            ThemeKind.Compact => BuildCompact(),
            _ => BuildPixel(),
        };
        column.Location = new Point(Padding.Left, Padding.Top);
        Controls.Add(column);
    }

    /// <summary>The menu with the four themes, the current one checked.</summary>
    public static void FillThemeMenu(ToolStripItemCollection items, Action<ThemeKind> pick)
    {
        foreach (var kind in Enum.GetValues<ThemeKind>())
        {
            var item = new ToolStripMenuItem(T.ThemeName(kind)) { Checked = kind == Theme.Kind };
            item.Click += (_, _) => pick(kind);
            items.Add(item);
        }
    }

    private void ShowThemeMenu(Control anchor)
    {
        var menu = new ContextMenuStrip();
        FillThemeMenu(menu.Items, SetTheme);
        menu.Closed += (_, _) => BeginInvoke(menu.Dispose);
        menu.Show(anchor, new Point(0, anchor.Height));
    }

    // ---------- Shared controls ----------

    private void CreateControls()
    {
        _subtitle = NewLabel(Theme.Small, Theme.Muted);
        _battery = NewLabel(Theme.Title, Theme.Text);
        _status = NewLabel(Theme.Body, Theme.Warning);
        _status.MaximumSize = new Size(Width340, 0);
        _status.Margin = new Padding(0, Px(8), 0, 0);

        _stagePills = NewRow();
        _editLabel = NewLabel(Theme.Small, Theme.Muted);
        _editLabel.Margin = new Padding(0, Px(6), 0, 0);

        _dpiSlider = new Slider
        {
            Minimum = Protocol.DpiMin / Protocol.DpiStep,
            Maximum = Protocol.DpiMax / Protocol.DpiStep,
            SmallChange = 1,
            LargeChange = 8,
            AccessibleName = "DPI",
        };
        _dpiSlider.ValueChanged += (_, _) =>
        {
            if (_updating) return;
            SetDpiControls(_dpiSlider.Value * Protocol.DpiStep, fromSlider: true);
        };
        _dpiBox = new NumericUpDown
        {
            Minimum = Protocol.DpiMin,
            Maximum = Protocol.DpiMax,
            Increment = Protocol.DpiStep,
            Width = Px(84),
            BackColor = Theme.Kind == ThemeKind.Pixel ? Theme.Track : Theme.Surface,
            ForeColor = Theme.Text,
            BorderStyle = BorderStyle.FixedSingle,
            TextAlign = HorizontalAlignment.Right,
            Font = Theme.Kind == ThemeKind.Pixel ? Theme.Display : Theme.Body,
            Anchor = AnchorStyles.Right,
            AccessibleName = "DPI",
        };
        _dpiBox.ValueChanged += (_, _) =>
        {
            if (_updating) return;
            SetDpiControls((int)_dpiBox.Value, fromSlider: false);
        };

        _pollingPills = NewRow();
        _pollingNote = NewLabel(Theme.Small, Theme.Warning);
        _pollingNote.MaximumSize = new Size(Width340, 0);
        _pollingNote.Margin = new Padding(0, Px(6), 0, 0);

        _speedSlider = new Slider
        {
            Minimum = WindowsPointer.SpeedMin,
            Maximum = WindowsPointer.SpeedMax,
            AccessibleName = T.Speed,
        };
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
        _speedValue = NewLabel(Theme.Body, Theme.Text);
        _speedValue.TextAlign = ContentAlignment.MiddleRight;

        _noAccel = new Switch { Text = T.NoAccel, AccessibleName = T.NoAccel };
        _noAccel.CheckedChanged += (_, _) =>
        {
            if (_updating) return;
            Try(() => WindowsPointer.SetAcceleration(!_noAccel.Checked));
            LoadPointer();
        };
        _startup = new Switch { Text = T.StartWithWindows, AccessibleName = T.StartWithWindows };
        _startup.CheckedChanged += (_, _) =>
        {
            if (_updating) return;
            Try(() => Startup.Enabled = _startup.Checked);
        };
    }

    private FlowLayoutPanel NewRow()
    {
        var segmented = Theme.Kind is ThemeKind.Fluent or ThemeKind.Cards;
        return new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = segmented ? new Padding(Px(3)) : Padding.Empty,
            BackColor = segmented ? Theme.Track : Theme.Background,
        };
    }

    /// <summary>Sizes a pill row for the theme (pixel pills need room for their shadow).</summary>
    private void SizeRow(FlowLayoutPanel row, int width, int? height = null)
    {
        row.Width = width;
        row.Height = height ?? (Theme.Kind switch { ThemeKind.Pixel => Px(37), ThemeKind.Compact => Px(34), _ => Px(36) });
    }

    private static Label NewLabel(Font font, Color color) =>
        new() { Font = font, ForeColor = color, AutoSize = true, Margin = Padding.Empty, BackColor = Color.Transparent };

    private Label Note(string text, int width)
    {
        var label = NewLabel(Theme.Small, Theme.Muted);
        label.Text = text;
        label.MaximumSize = new Size(width, 0);
        return label;
    }

    /// <summary>Small upper-case section title with an optional hint on the right.</summary>
    private Control SectionHeader(string text, string? hint, int width, int top = 16)
    {
        var row = new TableLayoutPanel { ColumnCount = 2, MinimumSize = new Size(width, 0), MaximumSize = new Size(width, 0), AutoSize = true, Margin = new Padding(0, Px(top), 0, Px(6)), BackColor = Color.Transparent };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var label = NewLabel(Theme.Section, Theme.Kind == ThemeKind.Pixel ? Theme.Accent : Theme.Kind == ThemeKind.Fluent ? Theme.Text : Theme.Muted);
        label.Text = Theme.Kind == ThemeKind.Fluent ? text : text.ToUpper(CultureInfo.CurrentCulture);
        row.Controls.Add(label, 0, 0);
        if (hint is not null)
        {
            var h = NewLabel(Theme.Small, Theme.Muted);
            h.Text = hint;
            h.Anchor = AnchorStyles.Right;
            row.Controls.Add(h, 1, 0);
        }
        return row;
    }

    private static FlowLayoutPanel Column(int width = 0) => new()
    {
        FlowDirection = FlowDirection.TopDown,
        WrapContents = false,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        Margin = Padding.Empty,
        BackColor = Color.Transparent,
        MinimumSize = new Size(width, 0),
    };

    /// <summary>A table row: [fixed | stretch | fixed] columns, the usual label/slider/value line.</summary>
    private TableLayoutPanel Line(int width, params Control[] cells)
    {
        var row = new TableLayoutPanel { ColumnCount = cells.Length, MinimumSize = new Size(width, 0), MaximumSize = new Size(width, 0), AutoSize = true, Margin = Padding.Empty, BackColor = Color.Transparent };
        for (var i = 0; i < cells.Length; i++)
        {
            row.ColumnStyles.Add(cells[i] is Slider or Stretch ? new ColumnStyle(SizeType.Percent, 100) : new ColumnStyle(SizeType.AutoSize));
            if (cells[i] is Slider or Stretch) cells[i].Dock = DockStyle.Fill;
            else if (cells[i].Anchor == (AnchorStyles.Top | AnchorStyles.Left)) cells[i].Anchor = AnchorStyles.Left;
            row.Controls.Add(cells[i], i, 0);
        }
        return row;
    }

    private LinkLabel Link(string text, Action action)
    {
        var link = new LinkLabel
        {
            Text = text, AutoSize = true, LinkColor = Theme.Accent, ActiveLinkColor = Theme.Text, BackColor = Color.Transparent,
            LinkBehavior = LinkBehavior.HoverUnderline, Anchor = AnchorStyles.Right, Margin = new Padding(Px(10), 0, 0, 0),
        };
        link.LinkClicked += (_, _) => action();
        return link;
    }

    /// <summary>Footer: start with Windows on the left; theme, diagnostics and exit on the right.</summary>
    private Control Footer(int width, bool withStartup = true)
    {
        var cells = new List<Control>();
        if (withStartup)
        {
            _startup.Width = _startup.GetPreferredSize(Size.Empty).Width;
            _startup.Margin = Padding.Empty;
            cells.Add(_startup);
        }
        else
        {
            cells.Add(new Label { AutoSize = true, Margin = Padding.Empty });
        }
        LinkLabel? theme = null;
        theme = Link(T.Theme, () => ShowThemeMenu(theme!));
        cells.Add(theme);
        var diagnose = Link(T.TileCopyCaption, CopyDiagnostics);
        diagnose.AccessibleName = T.CopyDiagnostics;
        cells.Add(diagnose);
        cells.Add(Link(T.Exit, () => ExitRequested?.Invoke()));
        var row = new TableLayoutPanel { ColumnCount = cells.Count, MinimumSize = new Size(width, 0), MaximumSize = new Size(width, 0), AutoSize = true, Margin = Padding.Empty, BackColor = Color.Transparent };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 1; i < cells.Count; i++) row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        for (var i = 0; i < cells.Count; i++) row.Controls.Add(cells[i], i, 0);
        return row;
    }

    private Pill NewPill(string text, int width, int height, Control row) =>
        new() { Text = text, Width = width, Height = height, Margin = new Padding(0, 0, PillGap, 0), BackColor = row.BackColor };

    private int PillGap => Theme.Kind switch { ThemeKind.Pixel => Px(5), ThemeKind.Fluent => Px(2), ThemeKind.Cards => Px(4), _ => Px(6) };

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

    /// <summary>After a theme change: same edge of the taskbar, still inside the screen.</summary>
    private void KeepOnScreen()
    {
        var area = Screen.FromControl(this).WorkingArea;
        var gap = Px(10);
        var x = Math.Clamp(Left, area.Left + gap, Math.Max(area.Left + gap, area.Right - Width - gap));
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
        _renderTheme?.Invoke();
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

    /// <summary>Next (+1) or previous (-1) DPI stage, wrapping around.</summary>
    private Task StepStageAsync(int step)
    {
        if (_state.Status != MouseStatus.Ok || _state.Stages.Count == 0) return Task.CompletedTask;
        var count = _state.Stages.Count;
        return SelectStageAsync(((_state.ActiveStage + step) % count + count) % count);
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

    /// <summary>The next polling rate the connection allows, wrapping around.</summary>
    private Task StepPollingAsync()
    {
        var options = _state.PollingOptions;
        if (_state.Status != MouseStatus.Ok || options.Count == 0) return Task.CompletedTask;
        var i = options.ToList().IndexOf(_state.Polling);
        return SetPollingAsync(options[(i + 1) % options.Count]);
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

    private void ShowSpeed(int speed)
    {
        _speedValue.Text = speed == WindowsPointer.SpeedNeutral ? $"{speed} · {T.SpeedNeutral}" : speed.ToString(CultureInfo.CurrentCulture);
        _renderTheme?.Invoke();
    }

    // ---------- Rendering ----------

    private static string FormatDpi(int dpi) => dpi.ToString("N0", CultureInfo.CurrentCulture);

    private static string FormatHz(int hz) => hz >= 1000 ? $"{hz / 1000}K" : hz.ToString(CultureInfo.CurrentCulture);

    private void Render()
    {
        SuspendLayout();
        var s = _state;
        var ok = s.Status == MouseStatus.Ok;

        _subtitle.Text = ok ? $"{(s.Wired ? T.Wired : T.Wireless)} · {s.Polling:N0} Hz" : "";
        if (Theme.Kind == ThemeKind.Pixel) _subtitle.Text = _subtitle.Text.ToUpper(CultureInfo.CurrentCulture);
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

        foreach (var c in _mouseOnly) c.Visible = ok;
        if (ok)
        {
            RenderStages();
            RenderPolling();
        }
        _renderTheme?.Invoke();
        ResumeLayout(true);
    }

    private void RenderStages()
    {
        var s = _state;
        var gap = PillGap;
        var inner = _stagePills.Width - _stagePills.Padding.Horizontal;
        var width = (inner - gap * (Protocol.MaxStages - 1)) / Protocol.MaxStages;
        var height = _stagePills.Height - _stagePills.Padding.Vertical;
        while (_stagePills.Controls.Count > s.Stages.Count) _stagePills.Controls[^1].Dispose();
        while (_stagePills.Controls.Count < s.Stages.Count)
        {
            var index = _stagePills.Controls.Count;
            var pill = NewPill("", width, height, _stagePills);
            pill.AccessibleDescription = T.DpiHint;
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
            pill.Margin = new Padding(0, 0, i == s.Stages.Count - 1 ? 0 : gap, 0);
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
        var gap = PillGap;
        var inner = _pollingPills.Width - _pollingPills.Padding.Horizontal;
        var width = (inner - gap * (6 - 1)) / 6;
        var height = _pollingPills.Height - _pollingPills.Padding.Vertical;
        if (_pollingPills.Controls.Count != options.Count
            || !_pollingPills.Controls.Cast<Control>().Select(c => (int)c.Tag!).SequenceEqual(options))
        {
            while (_pollingPills.Controls.Count > 0) _pollingPills.Controls[0].Dispose();
            foreach (var hz in options)
            {
                var pill = NewPill(FormatHz(hz), width, height, _pollingPills);
                pill.Tag = hz;
                pill.AccessibleName = $"{hz} Hz";
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
            + $"Tema: {Theme.Kind}\r\n"
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
            if (_status is null) return;
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
            Microsoft.Win32.SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            _refreshTimer.Dispose();
            _dpiDebounce.Dispose();
            _speedDebounce.Dispose();
        }
        base.Dispose(disposing);
    }
}
