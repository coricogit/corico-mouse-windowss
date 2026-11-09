namespace DareuMouse.Core;

/// <summary>
/// The Dareu "Jm" command protocol used by the A950 PRO and its TM265 2.4 GHz
/// receiver. Every command is a 16-byte body sent as HID report 8. The last
/// byte of a body is a checksum computed over the report ID plus the body.
/// The format follows the OpenMouse project's documentation; this code is an
/// independent implementation.
/// </summary>
public static class Protocol
{
    public const ushort VendorId = 0x260D;
    public const ushort ReceiverProductId = 0x1114;
    public const ushort WiredProductId = 0x1117;
    public const ushort CommandUsagePage = 0xFF02;
    public const ushort CommandUsage = 0x02;

    public const byte ReportId = 8;
    public const int BodySize = 16;
    public const int MaxChunk = 10;

    public const int DpiMin = 100;
    public const int DpiMax = 26000;
    public const int DpiStep = 50;
    public const int MaxStages = 5;

    public const byte CmdCheckActive = 3;
    public const byte CmdBattery = 4;
    public const byte CmdWrite = 7;
    public const byte CmdRead = 8;
    public const byte CmdEvent = 10;

    public const int AddrPolling = 0;
    public const int AddrDpiHeader = 2;
    public const int AddrActiveStage = 4;
    public const int AddrDpiStages = 12;

    private static readonly (int Hz, byte Code)[] PollingCodes =
    [
        (125, 0x08), (250, 0x04), (500, 0x02), (1000, 0x01), (2000, 0x10), (4000, 0x20),
    ];

    public static IReadOnlyList<int> PollingRates(bool wired) =>
        PollingCodes.Where(p => !wired || p.Hz <= 1000).Select(p => p.Hz).ToArray();

    public static byte? PollingCode(int hz, bool wired)
    {
        foreach (var (rate, code) in PollingCodes)
            if (rate == hz && (!wired || hz <= 1000)) return code;
        return null;
    }

    public static int? PollingHz(byte code, bool wired)
    {
        foreach (var (rate, candidate) in PollingCodes)
            if (candidate == code && (!wired || rate <= 1000)) return rate;
        return null;
    }

    /// <summary>Vendor checksum over every byte except the last (the checksum slot).</summary>
    public static byte Checksum(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 2) throw new ArgumentException("Checksum needs a payload byte and a checksum slot.");
        var sum = 0;
        for (var i = 0; i < bytes.Length - 1; i++) sum += bytes[i];
        return (byte)((sum < 55 ? 85 - sum : 341 - sum) & 0xFF);
    }

    /// <summary>Appends the checksum byte to a memory record.</summary>
    public static byte[] WithChecksum(params byte[] data)
    {
        var result = new byte[data.Length + 1];
        data.CopyTo(result, 0);
        result[^1] = Checksum(result);
        return result;
    }

    public static bool HasValidChecksum(ReadOnlySpan<byte> record) =>
        record.Length >= 2 && record[^1] == Checksum(record);

    private static byte[] NewFrame(byte command)
    {
        var full = new byte[BodySize + 1];
        full[0] = ReportId;
        full[1] = command;
        return full;
    }

    private static byte[] Seal(byte[] full)
    {
        full[^1] = Checksum(full);
        return full[1..];
    }

    public static byte[] CheckActiveRequest() => Seal(NewFrame(CmdCheckActive));

    public static byte[] BatteryRequest() => Seal(NewFrame(CmdBattery));

    public static byte[] ReadRequest(int address, int length)
    {
        CheckRange(address, length);
        var full = NewFrame(CmdRead);
        full[3] = (byte)(address >> 8);
        full[4] = (byte)(address & 0xFF);
        full[5] = (byte)length;
        return Seal(full);
    }

    public static byte[] WriteRequest(int address, ReadOnlySpan<byte> data)
    {
        CheckRange(address, data.Length);
        var full = NewFrame(CmdWrite);
        full[3] = (byte)(address >> 8);
        full[4] = (byte)(address & 0xFF);
        full[5] = (byte)data.Length;
        data.CopyTo(full.AsSpan(6));
        return Seal(full);
    }

    public static IEnumerable<byte[]> ReadRequests(int address, int length)
    {
        for (var offset = 0; offset < length; offset += MaxChunk)
            yield return ReadRequest(address + offset, Math.Min(MaxChunk, length - offset));
    }

    public static IEnumerable<byte[]> WriteRequests(int address, byte[] data)
    {
        for (var offset = 0; offset < data.Length; offset += MaxChunk)
            yield return WriteRequest(address + offset, data.AsSpan(offset, Math.Min(MaxChunk, data.Length - offset)));
    }

    private static void CheckRange(int address, int length)
    {
        if (address < 0 || address > 0xFFFF) throw new ArgumentOutOfRangeException(nameof(address));
        if (length < 1 || length > MaxChunk || address + length > 0x10000) throw new ArgumentOutOfRangeException(nameof(length));
    }

    /// <summary>
    /// A reply answers a request when it carries the same command. Read replies
    /// must also echo the address and length, so a late reply to an earlier
    /// read is never taken as the answer to the next one. Command 10 is an
    /// unsolicited event and never matches.
    /// </summary>
    public static bool Matches(byte[] request, byte[] reply)
    {
        if (request.Length != BodySize || reply.Length != BodySize) return false;
        if (reply[0] == CmdEvent || reply[0] != request[0]) return false;
        return request[0] != CmdRead
            || (request[2] == reply[2] && request[3] == reply[3] && request[4] == reply[4]);
    }

    public static byte[]? DecodeRead(byte[] request, byte[] reply)
    {
        if (request[0] != CmdRead || !Matches(request, reply)) return null;
        int length = request[4];
        if (length < 1 || length > MaxChunk) return null;
        return reply[5..(5 + length)];
    }

    public static bool IsValidDpi(int dpi) => dpi >= DpiMin && dpi <= DpiMax && dpi % DpiStep == 0;

    public static int ClampDpi(int dpi) =>
        Math.Clamp((int)Math.Round(dpi / (double)DpiStep) * DpiStep, DpiMin, DpiMax);

    /// <summary>DPI to the vendor's 16-bit id: index = dpi/50 - 1, with the high byte marking the 256-wide bank.</summary>
    public static int? DpiId(int dpi)
    {
        if (!IsValidDpi(dpi)) return null;
        var index = dpi / DpiStep - 1;
        return index < 256 ? index : index < 512 ? (index - 256) | 0x4400 : (index - 512) | 0x8800;
    }

    public static int? DpiFromId(int id)
    {
        if (id < 0 || id > 0xFFFF) return null;
        var high = id >> 8;
        var index = high switch
        {
            0x00 => id,
            0x44 => 256 + (id & 0xFF),
            0x88 => 512 + (id & 0xFF),
            _ => -1,
        };
        var dpi = DpiStep * (index + 1);
        return IsValidDpi(dpi) ? dpi : null;
    }

    /// <summary>A DPI stage record: [x low, y low, shared high byte, checksum].</summary>
    public static byte[] EncodeStage(int dpi)
    {
        var id = DpiId(dpi) ?? throw new ArgumentOutOfRangeException(nameof(dpi));
        return WithChecksum((byte)(id & 0xFF), (byte)(id & 0xFF), (byte)(id >> 8));
    }

    public static int? DecodeStage(ReadOnlySpan<byte> record)
    {
        if (record.Length != 4 || !HasValidChecksum(record)) return null;
        // The A950 uses one DPI for both axes; the X byte is the one that counts.
        return DpiFromId(record[0] | record[2] << 8);
    }
}
