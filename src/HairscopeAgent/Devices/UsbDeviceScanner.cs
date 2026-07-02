using System.Management;
using System.Text.RegularExpressions;

namespace Hairscope.Agent.Devices;

public sealed record PresentUsbDevice(string Vid, string Pid, string PnpDeviceId, string? Name);

/// <summary>Enumerates connected USB devices via WMI and extracts VID/PID. Windows-only.</summary>
public static class UsbDeviceScanner
{
    private static readonly Regex VidPid =
        new(@"VID_(?<vid>[0-9A-Fa-f]{4})&PID_(?<pid>[0-9A-Fa-f]{4})", RegexOptions.Compiled);

    public static IReadOnlyList<PresentUsbDevice> Enumerate()
    {
        var list = new List<PresentUsbDevice>();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT DeviceID, Name FROM Win32_PnPEntity WHERE DeviceID LIKE 'USB%VID_%'");
            foreach (var o in searcher.Get())
            {
                var id = o["DeviceID"]?.ToString();
                if (string.IsNullOrEmpty(id)) continue;
                var m = VidPid.Match(id);
                if (!m.Success) continue;
                list.Add(new PresentUsbDevice(
                    m.Groups["vid"].Value.ToUpperInvariant(),
                    m.Groups["pid"].Value.ToUpperInvariant(),
                    id,
                    o["Name"]?.ToString()));
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[scan] USB enumeration failed: {ex.Message}");
        }
        return list;
    }

    public static bool IsPresent(string vid, string pid) =>
        Enumerate().Any(d =>
            string.Equals(d.Vid, vid, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(d.Pid, pid, StringComparison.OrdinalIgnoreCase));
}
