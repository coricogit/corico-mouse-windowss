namespace DareuMouse.Core;

public enum MouseStatus { Loading, Ok, Asleep, NoDevice, Error }

public sealed record MouseState(
    MouseStatus Status,
    bool Wired,
    int Battery,
    bool Charging,
    int Polling,
    IReadOnlyList<int> PollingOptions,
    IReadOnlyList<int> Stages,
    int ActiveStage,
    string? Error)
{
    public static MouseState Of(MouseStatus status, string? error = null) =>
        new(status, false, 0, false, 0, [], [], -1, error);
}

/// <summary>
/// Opens the Dareu command channel for each operation and closes it afterwards,
/// so unplugging the receiver or switching to the cable never leaves a stale
/// handle behind. Operations run one at a time.
/// </summary>
public sealed class MouseService(Func<IReadOnlyList<IHidChannel>> openChannels)
{
    private readonly Lock _gate = new();

    public MouseState ReadState() => Run(client => client.ReadState());

    public MouseState SetPolling(int hz) => Run(client => { client.SetPolling(hz); return client.ReadState(); });

    public MouseState SetActiveStage(int stage) => Run(client => { client.SetActiveStage(stage); return client.ReadState(); });

    public MouseState SetStageDpi(int stage, int dpi) => Run(client => { client.SetStageDpi(stage, dpi); return client.ReadState(); });

    private MouseState Run(Func<MouseClient, MouseState> operation)
    {
        lock (_gate)
        {
            IReadOnlyList<IHidChannel> channels;
            try
            {
                channels = openChannels();
            }
            catch (Exception e)
            {
                return MouseState.Of(MouseStatus.Error, e.Message);
            }

            var clients = channels
                // With the cable plugged in, talk to the mouse directly first.
                .OrderBy(c => c.ProductId == Protocol.WiredProductId ? 0 : 1)
                .Select(c => new MouseClient(c))
                .ToList();
            try
            {
                if (clients.Count == 0) return MouseState.Of(MouseStatus.NoDevice);
                var sawAsleep = false;
                string? lastError = null;
                foreach (var client in clients)
                {
                    try
                    {
                        if (!client.IsOnline()) { sawAsleep = true; continue; }
                        return operation(client);
                    }
                    catch (MouseException e) when (e.Code == "asleep")
                    {
                        sawAsleep = true;
                    }
                    catch (MouseException e) when (e.Code == "rejected")
                    {
                        // The mouse answered but refused the change: report its current state with the reason.
                        try { return client.ReadState() with { Error = e.Message }; }
                        catch (Exception inner) { lastError = inner.Message; }
                    }
                    catch (Exception e)
                    {
                        lastError = e.Message;
                    }
                }
                if (lastError is not null) return MouseState.Of(MouseStatus.Error, lastError);
                return MouseState.Of(sawAsleep ? MouseStatus.Asleep : MouseStatus.NoDevice);
            }
            finally
            {
                foreach (var client in clients) client.Dispose();
            }
        }
    }
}
