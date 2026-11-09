using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace DareuMouse;

/// <summary>
/// Windows' own pointer settings, the same ones as in Settings > Bluetooth &amp; devices > Mouse.
/// They apply to every mouse and are saved to the user profile.
/// </summary>
internal static class WindowsPointer
{
    private const uint SPI_GETMOUSE = 0x0003;
    private const uint SPI_SETMOUSE = 0x0004;
    private const uint SPI_GETMOUSESPEED = 0x0070;
    private const uint SPI_SETMOUSESPEED = 0x0071;
    private const uint SPIF_UPDATEINIFILE_SENDCHANGE = 0x01 | 0x02;

    public const int SpeedMin = 1;
    public const int SpeedMax = 20;

    /// <summary>Speed 10 moves the cursor one pixel per mouse count: no scaling.</summary>
    public const int SpeedNeutral = 10;

    public static int GetSpeed()
    {
        var speed = 0;
        if (!SystemParametersInfo(SPI_GETMOUSESPEED, 0, ref speed, 0)) throw new Win32Exception();
        return speed;
    }

    public static void SetSpeed(int speed)
    {
        speed = Math.Clamp(speed, SpeedMin, SpeedMax);
        if (!SystemParametersInfo(SPI_SETMOUSESPEED, 0, (IntPtr)speed, SPIF_UPDATEINIFILE_SENDCHANGE))
            throw new Win32Exception();
    }

    /// <summary>"Enhance pointer precision" is Windows' variable acceleration.</summary>
    public static bool GetAcceleration()
    {
        var values = new int[3];
        if (!SystemParametersInfo(SPI_GETMOUSE, 0, values, 0)) throw new Win32Exception();
        return values[2] != 0;
    }

    public static void SetAcceleration(bool enabled)
    {
        // Windows' defaults are thresholds 6 and 10 with acceleration 1; all zero turns it off.
        int[] values = enabled ? [6, 10, 1] : [0, 0, 0];
        if (!SystemParametersInfo(SPI_SETMOUSE, 0, values, SPIF_UPDATEINIFILE_SENDCHANGE))
            throw new Win32Exception();
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SystemParametersInfo(uint action, uint param, ref int value, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SystemParametersInfo(uint action, uint param, IntPtr value, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SystemParametersInfo(uint action, uint param, [In, Out] int[] values, uint flags);
}

/// <summary>Start with Windows through the per-user Run key. No admin rights needed.</summary>
internal static class Startup
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "DareuMouse";

    private static string Command => $"\"{Environment.ProcessPath}\" --tray";

    public static bool Enabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string value
                && value.Equals(Command, StringComparison.OrdinalIgnoreCase);
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value) key.SetValue(ValueName, Command);
            else key.DeleteValue(ValueName, false);
        }
    }
}
