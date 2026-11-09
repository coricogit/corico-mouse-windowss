using DareuMouse.Core;

namespace DareuMouse;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        using var mutex = new Mutex(true, @"Local\DareuMouse.SingleInstance", out var first);
        if (!first)
        {
            MessageBox.Show(T.AlreadyRunning, "Dareu Mouse", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        var service = args.Contains("--demo")
            ? DemoService()
            : new MouseService(WinHid.OpenDareuChannels);
        Application.Run(new TrayContext(service, startHidden: args.Contains("--tray")));
    }

    /// <summary>A simulated A950 for trying the panel without the hardware.</summary>
    private static MouseService DemoService()
    {
        var mouse = new SimulatedMouse(polling: 2000, stages: [400, 800, 1600, 3200, 6400], active: 1)
        {
            Battery = 40,
        };
        return new MouseService(() => [mouse]);
    }
}

/// <summary>Lives in the notification area. Left-click opens the panel; right-click shows the menu.</summary>
internal sealed class TrayContext : ApplicationContext
{
    private readonly PanelForm _panel;
    private readonly NotifyIcon _tray = new();
    private Icon? _icon;

    public TrayContext(MouseService service, bool startHidden)
    {
        _panel = new PanelForm(service);
        var menu = new ContextMenuStrip();
        menu.Items.Add(T.Open, null, (_, _) => _panel.ShowPanel());
        menu.Items.Add(T.Refresh, null, async (_, _) => await _panel.RefreshAsync());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(T.Exit, null, (_, _) => ExitThread());
        _tray.ContextMenuStrip = menu;
        _tray.Text = "Dareu A950";
        _tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) _panel.Toggle(); };
        SetIcon(MouseState.Of(MouseStatus.Loading));
        _tray.Visible = true;

        _panel.StateChanged += OnState;
        _panel.ExitRequested += ExitThread;
        _ = _panel.Handle; // create the window so timers and callbacks have a thread to run on
        if (startHidden) _ = _panel.RefreshAsync();
        else _panel.ShowPanel();
    }

    private void OnState(MouseState state)
    {
        SetIcon(state);
        var text = state.Status switch
        {
            MouseStatus.Ok => $"Dareu A950 · {state.Battery} %{(state.Charging ? " ⚡" : "")} · "
                + $"{state.Stages[state.ActiveStage]:N0} DPI · {state.Polling:N0} Hz",
            MouseStatus.Asleep => "Dareu A950 · " + T.Asleep,
            MouseStatus.NoDevice => "Dareu A950 · " + T.NoDevice,
            _ => "Dareu A950",
        };
        _tray.Text = text.Length > 127 ? text[..127] : text;

        if (_panel.ShouldWarnLowBattery())
            _tray.ShowBalloonTip(8000, T.LowBatteryTitle, T.LowBattery(state.Battery), ToolTipIcon.Warning);
    }

    private void SetIcon(MouseState state)
    {
        var size = SystemInformation.SmallIconSize.Width;
        var icon = TrayIcon.Draw(state.Battery, state.Status == MouseStatus.Ok, state.Charging, Math.Max(16, size));
        _tray.Icon = icon;
        _icon?.Dispose();
        _icon = icon;
    }

    protected override void ExitThreadCore()
    {
        _tray.Visible = false;
        _tray.Dispose();
        _icon?.Dispose();
        _panel.Dispose();
        base.ExitThreadCore();
    }
}
