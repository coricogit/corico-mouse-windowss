using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using DareuMouse.Core;
using Microsoft.Win32.SafeHandles;

namespace DareuMouse;

/// <summary>One HID collection as Windows lists it.</summary>
internal sealed record HidInfo(string Path, ushort VendorId, ushort ProductId, ushort UsagePage, ushort Usage,
    int InputLength, int OutputLength);

/// <summary>Finds and opens the Dareu command collection through SetupAPI and hid.dll. No driver is needed.</summary>
internal static class WinHid
{
    public static List<HidInfo> Enumerate()
    {
        var result = new List<HidInfo>();
        Native.HidD_GetHidGuid(out var guid);
        var set = Native.SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero, Native.DIGCF_PRESENT | Native.DIGCF_DEVICEINTERFACE);
        if (set == new IntPtr(-1)) throw new Win32Exception();
        try
        {
            var data = new Native.SP_DEVICE_INTERFACE_DATA { cbSize = Marshal.SizeOf<Native.SP_DEVICE_INTERFACE_DATA>() };
            for (uint i = 0; Native.SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, i, ref data); i++)
            {
                var path = DevicePath(set, ref data);
                if (path is null) continue;
                var info = Describe(path);
                if (info is not null) result.Add(info);
            }
        }
        finally
        {
            Native.SetupDiDestroyDeviceInfoList(set);
        }
        return result;
    }

    private static string? DevicePath(IntPtr set, ref Native.SP_DEVICE_INTERFACE_DATA data)
    {
        Native.SetupDiGetDeviceInterfaceDetail(set, ref data, IntPtr.Zero, 0, out var size, IntPtr.Zero);
        if (size == 0) return null;
        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            // cbSize of SP_DEVICE_INTERFACE_DETAIL_DATA_W: 8 on 64-bit, 6 on 32-bit.
            Marshal.WriteInt32(buffer, IntPtr.Size == 8 ? 8 : 6);
            if (!Native.SetupDiGetDeviceInterfaceDetail(set, ref data, buffer, size, out _, IntPtr.Zero)) return null;
            return Marshal.PtrToStringUni(buffer + 4);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static HidInfo? Describe(string path)
    {
        // Zero access is enough to read attributes, even for collections Windows keeps for itself.
        using var handle = Native.CreateFile(path, 0, Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE,
            IntPtr.Zero, Native.OPEN_EXISTING, 0, IntPtr.Zero);
        if (handle.IsInvalid) return null;
        var attributes = new Native.HIDD_ATTRIBUTES { Size = Marshal.SizeOf<Native.HIDD_ATTRIBUTES>() };
        if (!Native.HidD_GetAttributes(handle, ref attributes)) return null;
        if (!Native.HidD_GetPreparsedData(handle, out var preparsed)) return null;
        try
        {
            if (Native.HidP_GetCaps(preparsed, out var caps) != Native.HIDP_STATUS_SUCCESS) return null;
            return new HidInfo(path, attributes.VendorID, attributes.ProductID, caps.UsagePage, caps.Usage,
                caps.InputReportByteLength, caps.OutputReportByteLength);
        }
        finally
        {
            Native.HidD_FreePreparsedData(preparsed);
        }
    }

    public static bool IsDareuCommandChannel(HidInfo info) =>
        info.VendorId == Protocol.VendorId
        && info.ProductId is Protocol.ReceiverProductId or Protocol.WiredProductId
        && info.UsagePage == Protocol.CommandUsagePage
        && info.Usage == Protocol.CommandUsage
        && info.InputLength >= Protocol.BodySize + 1
        && info.OutputLength >= Protocol.BodySize + 1;

    /// <summary>Opens every Dareu command channel that is present. Channels that fail to open are skipped.</summary>
    public static IReadOnlyList<IHidChannel> OpenDareuChannels()
    {
        var channels = new List<IHidChannel>();
        foreach (var info in Enumerate().Where(IsDareuCommandChannel))
        {
            try { channels.Add(new WinHidChannel(info)); }
            catch (Exception) { /* busy or gone: try the others */ }
        }
        return channels;
    }

    /// <summary>A plain-text list of every HID collection, for troubleshooting.</summary>
    public static string Diagnose()
    {
        var text = new StringBuilder();
        foreach (var info in Enumerate().OrderBy(i => i.VendorId).ThenBy(i => i.ProductId))
        {
            var mark = IsDareuCommandChannel(info) ? " <- canal de comandos Dareu" : "";
            text.AppendLine($"{info.VendorId:X4}:{info.ProductId:X4} page {info.UsagePage:X4} usage {info.Usage:X2} in {info.InputLength} out {info.OutputLength}{mark}");
        }
        return text.ToString();
    }
}

/// <summary>
/// Overlapped I/O on one handle. A read that is still waiting when its timeout
/// passes stays pending and is picked up by the next Read, so no report is lost.
/// </summary>
internal sealed class WinHidChannel : IHidChannel
{
    private readonly SafeFileHandle _handle;
    private readonly int _inputLength;
    private readonly int _outputLength;
    private readonly IntPtr _readBuffer;
    private readonly IntPtr _readOverlapped;
    private readonly ManualResetEvent _readEvent = new(false);
    private readonly IntPtr _writeBuffer;
    private readonly IntPtr _writeOverlapped;
    private readonly ManualResetEvent _writeEvent = new(false);
    private bool _readPending;
    private bool _disposed;

    public ushort ProductId { get; }

    public WinHidChannel(HidInfo info)
    {
        ProductId = info.ProductId;
        _inputLength = info.InputLength;
        _outputLength = info.OutputLength;
        _handle = Native.CreateFile(info.Path, Native.GENERIC_READ | Native.GENERIC_WRITE,
            Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE, IntPtr.Zero, Native.OPEN_EXISTING,
            Native.FILE_FLAG_OVERLAPPED, IntPtr.Zero);
        if (_handle.IsInvalid) throw new Win32Exception();
        _readBuffer = Marshal.AllocHGlobal(_inputLength);
        _writeBuffer = Marshal.AllocHGlobal(_outputLength);
        _readOverlapped = Marshal.AllocHGlobal(Marshal.SizeOf<NativeOverlapped>());
        _writeOverlapped = Marshal.AllocHGlobal(Marshal.SizeOf<NativeOverlapped>());
    }

    private static void Prepare(IntPtr overlapped, ManualResetEvent evt)
    {
        evt.Reset();
        Marshal.StructureToPtr(new NativeOverlapped { EventHandle = evt.SafeWaitHandle.DangerousGetHandle() }, overlapped, false);
    }

    public void Write(byte[] body)
    {
        var report = new byte[_outputLength];
        report[0] = Protocol.ReportId;
        body.CopyTo(report, 1);
        Marshal.Copy(report, 0, _writeBuffer, report.Length);
        Prepare(_writeOverlapped, _writeEvent);
        if (!Native.WriteFile(_handle, _writeBuffer, report.Length, IntPtr.Zero, _writeOverlapped)
            && Marshal.GetLastWin32Error() != Native.ERROR_IO_PENDING)
            throw new IOException("Could not send to the mouse.", new Win32Exception());
        if (!_writeEvent.WaitOne(1000))
        {
            Native.CancelIoEx(_handle, _writeOverlapped);
            Native.GetOverlappedResult(_handle, _writeOverlapped, out _, true);
            throw new IOException("Sending to the mouse timed out.");
        }
        if (!Native.GetOverlappedResult(_handle, _writeOverlapped, out _, false))
            throw new IOException("Could not send to the mouse.", new Win32Exception());
    }

    public byte[]? Read(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            if (!_readPending)
            {
                Prepare(_readOverlapped, _readEvent);
                if (!Native.ReadFile(_handle, _readBuffer, _inputLength, IntPtr.Zero, _readOverlapped)
                    && Marshal.GetLastWin32Error() != Native.ERROR_IO_PENDING)
                    throw new IOException("Could not read from the mouse.", new Win32Exception());
                _readPending = true;
            }
            var left = deadline - DateTime.UtcNow;
            if (!_readEvent.WaitOne(left > TimeSpan.Zero ? left : TimeSpan.Zero)) return null;
            _readPending = false;
            if (!Native.GetOverlappedResult(_handle, _readOverlapped, out var count, false))
                throw new IOException("Could not read from the mouse.", new Win32Exception());
            var report = new byte[count];
            Marshal.Copy(_readBuffer, report, 0, count);
            // Other report IDs on this collection are not command replies.
            if (count >= Protocol.BodySize + 1 && report[0] == Protocol.ReportId)
                return report[1..(Protocol.BodySize + 1)];
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_readPending)
        {
            Native.CancelIoEx(_handle, _readOverlapped);
            Native.GetOverlappedResult(_handle, _readOverlapped, out _, true);
        }
        _handle.Dispose();
        Marshal.FreeHGlobal(_readBuffer);
        Marshal.FreeHGlobal(_writeBuffer);
        Marshal.FreeHGlobal(_readOverlapped);
        Marshal.FreeHGlobal(_writeOverlapped);
        _readEvent.Dispose();
        _writeEvent.Dispose();
    }
}

internal static class Native
{
    public const int DIGCF_PRESENT = 0x02;
    public const int DIGCF_DEVICEINTERFACE = 0x10;
    public const uint GENERIC_READ = 0x80000000;
    public const uint GENERIC_WRITE = 0x40000000;
    public const uint FILE_SHARE_READ = 1;
    public const uint FILE_SHARE_WRITE = 2;
    public const uint OPEN_EXISTING = 3;
    public const uint FILE_FLAG_OVERLAPPED = 0x40000000;
    public const int ERROR_IO_PENDING = 997;
    public const int HIDP_STATUS_SUCCESS = 0x00110000;

    [StructLayout(LayoutKind.Sequential)]
    public struct SP_DEVICE_INTERFACE_DATA
    {
        public int cbSize;
        public Guid InterfaceClassGuid;
        public int Flags;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct HIDD_ATTRIBUTES
    {
        public int Size;
        public ushort VendorID;
        public ushort ProductID;
        public ushort VersionNumber;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct HIDP_CAPS
    {
        public ushort Usage;
        public ushort UsagePage;
        public ushort InputReportByteLength;
        public ushort OutputReportByteLength;
        public ushort FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
        public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes;
        public ushort NumberInputButtonCaps;
        public ushort NumberInputValueCaps;
        public ushort NumberInputDataIndices;
        public ushort NumberOutputButtonCaps;
        public ushort NumberOutputValueCaps;
        public ushort NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps;
        public ushort NumberFeatureValueCaps;
        public ushort NumberFeatureDataIndices;
    }

    [DllImport("hid.dll")]
    public static extern void HidD_GetHidGuid(out Guid guid);

    [DllImport("hid.dll", SetLastError = true)]
    public static extern bool HidD_GetAttributes(SafeFileHandle device, ref HIDD_ATTRIBUTES attributes);

    [DllImport("hid.dll", SetLastError = true)]
    public static extern bool HidD_GetPreparsedData(SafeFileHandle device, out IntPtr preparsed);

    [DllImport("hid.dll")]
    public static extern bool HidD_FreePreparsedData(IntPtr preparsed);

    [DllImport("hid.dll")]
    public static extern int HidP_GetCaps(IntPtr preparsed, out HIDP_CAPS caps);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, IntPtr enumerator, IntPtr parent, int flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    public static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr info, ref Guid guid, uint index,
        ref SP_DEVICE_INTERFACE_DATA data);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SP_DEVICE_INTERFACE_DATA data,
        IntPtr detail, uint detailSize, out uint requiredSize, IntPtr info);

    [DllImport("setupapi.dll")]
    public static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security,
        uint creation, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool ReadFile(SafeFileHandle file, IntPtr buffer, int count, IntPtr read, IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool WriteFile(SafeFileHandle file, IntPtr buffer, int count, IntPtr written, IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool GetOverlappedResult(SafeFileHandle file, IntPtr overlapped, out int transferred, bool wait);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CancelIoEx(SafeFileHandle file, IntPtr overlapped);
}
