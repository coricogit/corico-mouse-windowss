namespace DareuMouse.Core;

/// <summary>An open HID collection that carries report 8 (the Dareu command channel).</summary>
public interface IHidChannel : IDisposable
{
    ushort ProductId { get; }

    /// <summary>Sends a 16-byte body as report 8.</summary>
    void Write(byte[] body);

    /// <summary>Waits for the next report-8 body, or returns null when the timeout passes.</summary>
    byte[]? Read(TimeSpan timeout);
}

public sealed class MouseException(string code, string message) : Exception(message)
{
    /// <summary>no-device, asleep, no-answer, bad-data or rejected.</summary>
    public string Code { get; } = code;
}

public sealed record DpiConfig(IReadOnlyList<int> Stages, int ActiveStage);

/// <summary>Talks to one Dareu command channel. Every write is read back to confirm it.</summary>
public sealed class MouseClient(IHidChannel channel) : IDisposable
{
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMilliseconds(800);

    public bool Wired => channel.ProductId == Protocol.WiredProductId;

    public void Dispose() => channel.Dispose();

    /// <summary>Sends a request and waits for its reply. One retry covers a dropped radio packet.</summary>
    private byte[] Exchange(byte[] request)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            // Drop anything left over so an old reply is never mistaken for this one.
            while (channel.Read(TimeSpan.Zero) is not null) { }
            channel.Write(request);
            var deadline = DateTime.UtcNow + Timeout;
            while (true)
            {
                var left = deadline - DateTime.UtcNow;
                if (left <= TimeSpan.Zero) break;
                var reply = channel.Read(left);
                if (reply is null) break;
                if (Protocol.Matches(request, reply)) return reply;
            }
        }
        throw new MouseException("no-answer", $"The mouse did not answer command {request[0]}.");
    }

    public bool IsOnline() => Exchange(Protocol.CheckActiveRequest())[5] == 1;

    public void EnsureOnline()
    {
        if (!IsOnline()) throw new MouseException("asleep", "The mouse is asleep or not paired to this receiver.");
    }

    public (int Percent, bool Charging) ReadBattery()
    {
        var reply = Exchange(Protocol.BatteryRequest());
        if (reply[5] > 100) throw new MouseException("bad-data", "Invalid battery reply.");
        return (reply[5], reply[6] is 1 or 2);
    }

    public byte[] ReadBuffer(int address, int length)
    {
        var result = new byte[length];
        var offset = 0;
        foreach (var request in Protocol.ReadRequests(address, length))
        {
            var chunk = Protocol.DecodeRead(request, Exchange(request))
                ?? throw new MouseException("bad-data", $"Memory read failed at {address + offset}.");
            chunk.CopyTo(result, offset);
            offset += chunk.Length;
        }
        return result;
    }

    public void WriteBuffer(int address, byte[] data)
    {
        // Each write is acknowledged before the next one is sent.
        foreach (var request in Protocol.WriteRequests(address, data)) Exchange(request);
    }

    public int ReadPolling()
    {
        var data = ReadBuffer(Protocol.AddrPolling, 2);
        if (!Protocol.HasValidChecksum(data)) throw new MouseException("bad-data", "Polling record checksum is invalid.");
        return Protocol.PollingHz(data[0], Wired)
            ?? throw new MouseException("bad-data", $"Unknown polling code {data[0]}.");
    }

    public DpiConfig ReadDpi()
    {
        var header = ReadBuffer(Protocol.AddrDpiHeader, 4);
        if (!Protocol.HasValidChecksum(header.AsSpan(0, 2)) || !Protocol.HasValidChecksum(header.AsSpan(2, 2)))
            throw new MouseException("bad-data", "DPI header checksum is invalid.");
        int count = header[0], active = header[2];
        if (count < 1 || count > Protocol.MaxStages || active >= count)
            throw new MouseException("bad-data", "DPI header is invalid.");
        var data = ReadBuffer(Protocol.AddrDpiStages, count * 4);
        var stages = new int[count];
        for (var i = 0; i < count; i++)
            stages[i] = Protocol.DecodeStage(data.AsSpan(i * 4, 4))
                ?? throw new MouseException("bad-data", $"DPI stage {i + 1} is invalid.");
        return new DpiConfig(stages, active);
    }

    public MouseState ReadState()
    {
        EnsureOnline();
        var (battery, charging) = ReadBattery();
        var dpi = ReadDpi();
        var polling = ReadPolling();
        return new MouseState(MouseStatus.Ok, Wired, battery, charging, polling,
            Protocol.PollingRates(Wired), dpi.Stages, dpi.ActiveStage, null);
    }

    public void SetPolling(int hz)
    {
        EnsureOnline();
        var code = Protocol.PollingCode(hz, Wired)
            ?? throw new MouseException("rejected", $"{hz} Hz is not available on this connection.");
        ReadPolling();
        WriteBuffer(Protocol.AddrPolling, Protocol.WithChecksum(code));
        var confirmed = ReadPolling();
        if (confirmed != hz) throw new MouseException("rejected", $"The mouse kept {confirmed} Hz.");
    }

    public void SetActiveStage(int stage)
    {
        EnsureOnline();
        var config = ReadDpi();
        if (stage < 0 || stage >= config.Stages.Count) throw new MouseException("rejected", "No such DPI stage.");
        WriteBuffer(Protocol.AddrActiveStage, Protocol.WithChecksum((byte)stage));
        if (ReadDpi().ActiveStage != stage) throw new MouseException("rejected", "The mouse kept its DPI stage.");
    }

    public void SetStageDpi(int stage, int dpi)
    {
        EnsureOnline();
        if (!Protocol.IsValidDpi(dpi)) throw new MouseException("rejected", $"{dpi} DPI is out of range.");
        var config = ReadDpi();
        if (stage < 0 || stage >= config.Stages.Count) throw new MouseException("rejected", "No such DPI stage.");
        WriteBuffer(Protocol.AddrDpiStages + stage * 4, Protocol.EncodeStage(dpi));
        var confirmed = ReadDpi().Stages[stage];
        if (confirmed != dpi) throw new MouseException("rejected", $"The mouse kept {confirmed} DPI.");
    }
}
