using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Hairscope.Agent.Buttons;

/// <summary>
/// Out-of-band button detector. Drives the USBPcap kernel driver (via
/// USBPcapCMD.exe) to observe the UVC VideoControl status-interrupt endpoint
/// (0x87) and reports the standard UVC still-image button event:
///     02 01 00 01  = pressed
///     02 01 00 00  = released
///
/// It only observes USB traffic — it never opens the camera — so it coexists
/// with the browser, which holds the exclusive camera stream. The button events
/// only appear on 0x87 while some app is streaming the probe.
///
/// It captures on every USBPcap interface (each as a hidden child process) and
/// filters for the specific status payload, so no manual hub selection is needed.
///
/// Requirements: USBPcap installed, process run ELEVATED (USBPcap needs admin).
/// </summary>
public sealed class UsbStatusButtonWatcher : IButtonWatcher
{
    private readonly string _usbpcapCmd;
    private readonly IReadOnlyList<ButtonSignature> _signatures;
    private readonly List<Process> _procs = new();
    private readonly List<Thread> _readers = new();
    private volatile bool _stop;
    private long _lastPressTicks;
    private bool _sawStatusPacket;

    // Learned USB (bus,address) -> (vid,pid), from injected/enumerated device descriptors.
    private readonly ConcurrentDictionary<int, (int vid, int pid)> _devByKey = new();

    public event Action<ButtonSignature>? ButtonPressed;

    public UsbStatusButtonWatcher(IReadOnlyList<ButtonSignature> signatures, string? usbpcapCmd = null)
    {
        _signatures = signatures;
        _usbpcapCmd = usbpcapCmd ?? @"C:\Program Files\USBPcap\USBPcapCMD.exe";
    }

    public bool Start()
    {
        if (!File.Exists(_usbpcapCmd))
        {
            Console.Error.WriteLine($"[usbtap] USBPcapCMD not found at {_usbpcapCmd}. Install USBPcap.");
            return false;
        }

        var interfaces = EnumerateInterfaces();
        if (interfaces.Count == 0)
        {
            Console.Error.WriteLine("[usbtap] no USBPcap interfaces found. Is USBPcap installed? (a reboot after install may be required)");
            return false;
        }
        Console.WriteLine($"[usbtap] capturing on: {string.Join(", ", interfaces)}");

        _stop = false;
        foreach (var iface in interfaces)
            StartCapture(iface);

        if (_procs.Count == 0)
        {
            Console.Error.WriteLine("[usbtap] could not start any capture process.");
            return false;
        }

        var eps = string.Join(", ", _signatures.Select(s => $"0x{s.Endpoint:X2}").Distinct());
        Console.WriteLine($"[usbtap] listening for button events on endpoint(s) {eps} (needs an app streaming the camera).");
        return true;
    }

    private List<string> EnumerateInterfaces()
    {
        var list = new List<string>();
        try
        {
            var psi = new ProcessStartInfo(_usbpcapCmd, "--extcap-interfaces")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi)!;
            var outp = p.StandardOutput.ReadToEnd();
            p.WaitForExit(5000);
            // Lines like: interface {value=\\.\USBPcap2}{display=USBPcap2}
            foreach (Match m in Regex.Matches(outp, @"value=(\\\\\.\\USBPcap\d+)", RegexOptions.IgnoreCase))
                if (!list.Contains(m.Groups[1].Value)) list.Add(m.Groups[1].Value);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[usbtap] failed to enumerate interfaces: {ex.Message}");
        }
        return list;
    }

    private void StartCapture(string iface)
    {
        var psi = new ProcessStartInfo(_usbpcapCmd)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        // -A all devices on the hub, --inject-descriptors so we learn each device's
        // VID/PID (to verify the button packet's source device), -s 256 snaplen
        // (keeps status + descriptor payloads, truncates video), -b large kernel buffer.
        foreach (var a in new[] { "-d", iface, "-A", "--inject-descriptors", "-s", "256", "-b", "67108864", "-o", "-" })
            psi.ArgumentList.Add(a);

        Process proc;
        try { proc = Process.Start(psi)!; }
        catch (Exception ex) { Console.Error.WriteLine($"[usbtap] failed to start capture on {iface}: {ex.Message}"); return; }
        if (proc == null) return;

        proc.ErrorDataReceived += (_, e) =>
        {
            if (string.IsNullOrWhiteSpace(e.Data)) return;
            if (e.Data.Contains("Couldn't open device", StringComparison.OrdinalIgnoreCase))
                Console.Error.WriteLine($"[usbtap] {iface}: cannot open — run the agent AS ADMINISTRATOR.");
        };
        proc.BeginErrorReadLine();

        var reader = new Thread(() => ParseLoop(proc.StandardOutput.BaseStream, iface))
        {
            IsBackground = true,
            Name = $"UsbPcapParse-{iface}"
        };
        _procs.Add(proc);
        _readers.Add(reader);
        reader.Start();
    }

    private void OnPress(ButtonSignature sig)
    {
        var now = DateTime.UtcNow.Ticks;
        if (now - _lastPressTicks < TimeSpan.FromMilliseconds(300).Ticks) return;
        _lastPressTicks = now;
        ButtonPressed?.Invoke(sig);
    }

    // ── pcap / USBPcap stream parsing ─────────────────────────
    private void ParseLoop(Stream s, string iface)
    {
        try
        {
            var global = new byte[24];
            if (!ReadExact(s, global, 24)) return;

            var recHdr = new byte[16];
            var packet = new byte[8192];
            while (!_stop)
            {
                if (!ReadExact(s, recHdr, 16)) break;
                int inclLen = BitConverter.ToInt32(recHdr, 8);
                if (inclLen <= 0 || inclLen > packet.Length)
                {
                    if (inclLen > 0 && !Skip(s, inclLen)) break;
                    continue;
                }
                if (!ReadExact(s, packet, inclLen)) break;
                HandleUsbPacket(packet, inclLen);
            }
        }
        catch (Exception ex)
        {
            if (!_stop) Console.Error.WriteLine($"[usbtap] {iface} parse loop ended: {ex.Message}");
        }
    }

    private void HandleUsbPacket(byte[] buf, int len)
    {
        // USBPCAP_BUFFER_PACKET_HEADER (little-endian):
        //  0  u16 headerLen | 17 u16 bus | 19 u16 device(address) | 21 u8 endpoint
        //  22 u8 transfer | 23 u32 dataLength
        if (len < 27) return;
        int headerLen = BitConverter.ToUInt16(buf, 0);
        int bus = BitConverter.ToUInt16(buf, 17);
        int address = BitConverter.ToUInt16(buf, 19);
        byte endpoint = buf[21];
        int dataLen = BitConverter.ToInt32(buf, 23);
        int devKey = (bus << 16) | address;

        // 1) Learn (bus,address) -> VID/PID from device descriptors (bLength 0x12,
        //    bDescriptorType 0x01). USBPcap injects these for connected devices.
        if (dataLen >= 18 && headerLen >= 27 && headerLen + 18 <= len &&
            buf[headerLen] == 0x12 && buf[headerLen + 1] == 0x01)
        {
            int vid = buf[headerLen + 8] | (buf[headerLen + 9] << 8);
            int pid = buf[headerLen + 10] | (buf[headerLen + 11] << 8);
            _devByKey[devKey] = (vid, pid);
            return;
        }

        // 2) Match button signatures (endpoint + payload), then verify the packet
        //    actually came from the matching device (guards common endpoints like 0x81).
        foreach (var sig in _signatures)
        {
            if (sig.Endpoint != endpoint) continue;
            _sawStatusPacket = true;

            var pl = sig.PressPayload;
            if (pl.Length == 0) continue;
            if (dataLen < pl.Length || headerLen < 27 || headerLen + pl.Length > len) continue;

            bool match = true;
            for (int i = 0; i < pl.Length; i++)
                if (buf[headerLen + i] != pl[i]) { match = false; break; }
            if (!match) continue;

            // Device-identity guard: if we know which device this address is, it must
            // match the signature. If the address maps to a *different* device, reject
            // (false positive). If unknown (descriptor not seen), accept as fallback.
            if (_devByKey.TryGetValue(devKey, out var vp))
            {
                if (vp.vid != ParseHex(sig.Vid) || vp.pid != ParseHex(sig.Pid)) continue;
            }

            OnPress(sig);
            return;
        }
    }

    private static int ParseHex(string s)
    {
        return int.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out var v) ? v : -1;
    }

    private static bool ReadExact(Stream s, byte[] buf, int count)
    {
        int off = 0;
        while (off < count)
        {
            int r = s.Read(buf, off, count - off);
            if (r <= 0) return false;
            off += r;
        }
        return true;
    }

    private static bool Skip(Stream s, int count)
    {
        var tmp = new byte[Math.Min(count, 8192)];
        int left = count;
        while (left > 0)
        {
            int r = s.Read(tmp, 0, Math.Min(left, tmp.Length));
            if (r <= 0) return false;
            left -= r;
        }
        return true;
    }

    public void Stop()
    {
        _stop = true;
        foreach (var proc in _procs)
        {
            try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { }
            try { proc.Dispose(); } catch { }
        }
        foreach (var r in _readers) { try { r.Join(1000); } catch { } }
        _procs.Clear();
        _readers.Clear();
        if (!_sawStatusPacket)
            Console.WriteLine("[usbtap] note: no 0x87 status packets seen — ensure an app was streaming the camera and the agent ran elevated.");
    }

    public void Dispose() => Stop();
}
