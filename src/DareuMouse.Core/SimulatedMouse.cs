namespace DareuMouse.Core;

/// <summary>
/// Simulates the A950's command channel: a memory map behind commands 7 and 8,
/// the online and battery commands, and the noise a real receiver produces.
/// Used by the tests and by the app's --demo mode.
/// </summary>
public sealed class SimulatedMouse : IHidChannel
{
    public readonly byte[] Memory = new byte[256];
    private readonly Queue<byte[]> _replies = new();

    public ushort ProductId { get; init; } = Protocol.ReceiverProductId;
    public bool Online { get; set; } = true;
    public int Battery { get; set; } = 40;
    public byte BatteryStatus { get; set; }
    public bool Disposed { get; private set; }
    public List<byte[]> Writes { get; } = [];

    /// <summary>Number of next requests to drop without answering, like a lost radio packet.</summary>
    public int DropNext { get; set; }

    /// <summary>Send an unsolicited event and a stale read reply before each real answer.</summary>
    public bool Noisy { get; set; }

    /// <summary>Accept writes to the polling record but keep the old value.</summary>
    public bool IgnorePollingWrites { get; set; }

    public SimulatedMouse(int polling = 2000, int[]? stages = null, int active = 1)
    {
        stages ??= [400, 800, 1600, 3200, 6400];
        Protocol.WithChecksum(Protocol.PollingCode(polling, false)!.Value).CopyTo(Memory, Protocol.AddrPolling);
        Protocol.WithChecksum((byte)stages.Length).CopyTo(Memory, Protocol.AddrDpiHeader);
        Protocol.WithChecksum((byte)active).CopyTo(Memory, Protocol.AddrActiveStage);
        for (var i = 0; i < stages.Length; i++)
            Protocol.EncodeStage(stages[i]).CopyTo(Memory, Protocol.AddrDpiStages + i * 4);
    }

    public void Write(byte[] body)
    {
        if (body.Length != Protocol.BodySize) throw new ArgumentException("A request body is 16 bytes.");
        var full = new byte[Protocol.BodySize + 1];
        full[0] = Protocol.ReportId;
        body.CopyTo(full, 1);
        if (!Protocol.HasValidChecksum(full)) throw new ArgumentException("The request checksum is wrong.");
        Writes.Add(body);

        if (DropNext > 0) { DropNext--; return; }

        var reply = new byte[Protocol.BodySize];
        reply[0] = body[0];
        switch (body[0])
        {
            case Protocol.CmdCheckActive:
                reply[5] = (byte)(Online ? 1 : 0);
                break;
            case Protocol.CmdBattery:
                reply[5] = (byte)Battery;
                reply[6] = BatteryStatus;
                break;
            case Protocol.CmdRead:
            {
                var address = body[2] << 8 | body[3];
                reply[2] = body[2]; reply[3] = body[3]; reply[4] = body[4];
                Memory.AsSpan(address, body[4]).CopyTo(reply.AsSpan(5));
                break;
            }
            case Protocol.CmdWrite:
            {
                var address = body[2] << 8 | body[3];
                if (!(IgnorePollingWrites && address == Protocol.AddrPolling))
                    body.AsSpan(5, body[4]).CopyTo(Memory.AsSpan(address));
                break;
            }
        }

        if (Noisy)
        {
            var evt = new byte[Protocol.BodySize];
            evt[0] = Protocol.CmdEvent;
            _replies.Enqueue(evt);
            var stale = new byte[Protocol.BodySize];
            stale[0] = Protocol.CmdRead;
            stale[3] = 0xEE;
            stale[4] = 1;
            _replies.Enqueue(stale);
        }
        _replies.Enqueue(reply);
    }

    public byte[]? Read(TimeSpan timeout) => _replies.Count > 0 ? _replies.Dequeue() : null;

    public void Dispose() => Disposed = true;
}
