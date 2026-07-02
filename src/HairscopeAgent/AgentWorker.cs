using Hairscope.Agent.Buttons;
using Hairscope.Agent.Config;
using Hairscope.Agent.Devices;
using Hairscope.Agent.Server;
using Microsoft.Extensions.Hosting;

namespace Hairscope.Agent;

/// <summary>
/// Core agent loop. Runs identically as a console app (dev) or a Windows Service
/// (prod). Keeps a lightweight localhost WebSocket listener open, and only starts
/// the (heavier) USBPcap button capture while a web client is actually connected.
/// </summary>
public sealed class AgentWorker : BackgroundService
{
    private readonly object _gate = new();
    private SnapWebSocketServer? _server;
    private IButtonWatcher? _watcher;
    private int _clients;

    // Button signatures for all enabled, button-capable devices.
    private readonly List<ButtonSignature> _signatures = new();

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Console.WriteLine($"Hairscope Agent  (started {DateTime.Now:yyyy-MM-dd HH:mm:ss})");
        Console.WriteLine("===============");

        var config = LoadConfig();
        var ws = config.Agent.WebSocket;

        _server = new SnapWebSocketServer(ws.Host, ws.Port, ws.AllowedOrigins);
        _server.ClientConnected += OnClientConnected;
        _server.ClientDisconnected += OnClientDisconnected;
        if (!_server.Start())
        {
            Console.Error.WriteLine("Agent: WebSocket server failed to start; nothing to do.");
            return Task.CompletedTask;
        }

        Console.WriteLine("Agent ready. USB capture starts only while the web app is connected.");
        // Idle until shutdown; work happens in the connect/disconnect callbacks.
        return Task.Delay(Timeout.Infinite, stoppingToken).ContinueWith(_ => { }, TaskScheduler.Default);
    }

    private AgentConfig LoadConfig()
    {
        var config = new AgentConfig();
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Config", "devices.json");
            config = AgentConfig.Load(path);

            _signatures.Clear();
            foreach (var (brand, series, device) in config.AllDevices())
            {
                if (!device.Enabled) continue;
                var btn = device.Capabilities?.Button;
                if (btn == null) continue;
                var vid = device.NormalizedVid ?? "0000";
                var pid = device.NormalizedPid ?? "0000";
                _signatures.Add(new ButtonSignature(btn.EndpointByte, btn.PressBytes, device.Model, vid, pid));
                Console.WriteLine($"Configured device: {brand.Name} {series.Name} {device.Model} " +
                                  $"(VID_{vid}&PID_{pid}, endpoint 0x{btn.EndpointByte:X2}, press {btn.PressPayload})");
            }
            if (_signatures.Count == 0)
                Console.Error.WriteLine("No enabled, button-capable devices in config.");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Agent: failed to load config: {ex.Message} (using defaults)");
        }
        return config;
    }

    private void OnClientConnected()
    {
        lock (_gate)
        {
            _clients++;
            if (_clients == 1) StartWatcher();
        }
    }

    private void OnClientDisconnected()
    {
        lock (_gate)
        {
            _clients--;
            if (_clients <= 0)
            {
                _clients = 0;
                StopWatcher();
            }
        }
    }

    private void StartWatcher()
    {
        if (_watcher != null) return;
        if (_signatures.Count == 0)
        {
            Console.Error.WriteLine("[agent] no button-capable devices configured; not starting capture.");
            return;
        }
        Console.WriteLine("[agent] web client connected -> starting button capture");
        foreach (var sig in _signatures)
            Console.WriteLine($"[agent] device {sig.Model} connected: {UsbDeviceScanner.IsPresent(sig.Vid, sig.Pid)}");

        var watcher = new UsbStatusButtonWatcher(_signatures);
        watcher.ButtonPressed += OnPress;
        if (!watcher.Start())
        {
            Console.Error.WriteLine("[agent] button capture failed to start.");
            watcher.ButtonPressed -= OnPress;
            watcher.Dispose();
            return;
        }
        _watcher = watcher;
    }

    private void StopWatcher()
    {
        if (_watcher == null) return;
        Console.WriteLine("[agent] no web clients -> stopping button capture");
        _watcher.ButtonPressed -= OnPress;
        _watcher.Dispose();
        _watcher = null;
    }

    private void OnPress(ButtonSignature sig)
    {
        Console.WriteLine($">>> BUTTON PRESSED [{sig.Model}]  {DateTime.Now:HH:mm:ss.fff}  -> broadcasting snap");
        _server?.BroadcastSnap(sig.Model, sig.Vid, sig.Pid);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        lock (_gate) { StopWatcher(); }
        if (_server != null) { await _server.DisposeAsync(); _server = null; }
        await base.StopAsync(cancellationToken);
    }
}
