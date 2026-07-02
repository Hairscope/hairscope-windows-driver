using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Text;

namespace Hairscope.Agent.Server;

/// <summary>
/// Minimal localhost WebSocket server (no external dependencies) that relays a
/// "snap" message to connected web-app clients when the hardware button is pressed.
/// Matches the web app's useHardwareCaptureButton hook.
///
/// Security: binds to 127.0.0.1 only and validates the browser Origin header
/// against an allowlist, so arbitrary local pages/processes cannot connect.
///
/// Fires ClientConnected / ClientDisconnected so the host can start USB capture
/// only while a web client is actually present.
/// </summary>
public sealed class SnapWebSocketServer : IAsyncDisposable
{
    private readonly string _host;
    private readonly int _port;
    private readonly HashSet<string> _allowedOrigins;
    private readonly HttpListener _listener = new();
    private readonly ConcurrentDictionary<Guid, WebSocket> _clients = new();
    private CancellationTokenSource? _cts;
    private Task? _acceptLoop;

    /// <summary>Raised when a web client connects.</summary>
    public event Action? ClientConnected;
    /// <summary>Raised when a web client disconnects.</summary>
    public event Action? ClientDisconnected;

    public SnapWebSocketServer(string host, int port, IEnumerable<string> allowedOrigins)
    {
        _host = host;
        _port = port;
        _allowedOrigins = new HashSet<string>(allowedOrigins, StringComparer.OrdinalIgnoreCase);
        _listener.Prefixes.Add($"http://{host}:{port}/");
    }

    public bool Start()
    {
        try
        {
            _cts = new CancellationTokenSource();
            _listener.Start();
            _acceptLoop = Task.Run(() => AcceptLoop(_cts.Token));
            Console.WriteLine($"[ws] listening on ws://{_host}:{_port}/");
            if (_allowedOrigins.Count == 0)
                Console.WriteLine("[ws] WARNING: no allowed origins configured — accepting any origin (dev only).");
            else
                Console.WriteLine($"[ws] allowed origins: {string.Join(", ", _allowedOrigins)}");
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ws] failed to start: {ex.Message}");
            return false;
        }
    }

    private async Task AcceptLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try { ctx = await _listener.GetContextAsync(); }
            catch when (token.IsCancellationRequested) { break; }
            catch (Exception ex) { Console.Error.WriteLine($"[ws] accept error: {ex.Message}"); continue; }

            if (!ctx.Request.IsWebSocketRequest)
            {
                ctx.Response.StatusCode = 426; // Upgrade Required
                ctx.Response.Close();
                continue;
            }

            // Origin allowlist check.
            var origin = ctx.Request.Headers["Origin"];
            if (!IsOriginAllowed(origin))
            {
                Console.Error.WriteLine($"[ws] rejected connection from origin '{origin ?? "(none)"}'");
                ctx.Response.StatusCode = 403;
                ctx.Response.Close();
                continue;
            }

            _ = HandleClient(ctx, token);
        }
    }

    private bool IsOriginAllowed(string? origin)
    {
        if (_allowedOrigins.Count == 0) return true; // dev: allow any
        if (string.IsNullOrEmpty(origin)) return false;
        return _allowedOrigins.Contains(origin);
    }

    private async Task HandleClient(HttpListenerContext ctx, CancellationToken token)
    {
        WebSocket socket;
        try { socket = (await ctx.AcceptWebSocketAsync(null)).WebSocket; }
        catch (Exception ex) { Console.Error.WriteLine($"[ws] handshake failed: {ex.Message}"); return; }

        var id = Guid.NewGuid();
        _clients[id] = socket;
        Console.WriteLine($"[ws] web client connected ({_clients.Count} total)");
        try { ClientConnected?.Invoke(); } catch { }

        var buffer = new byte[1024];
        try
        {
            while (socket.State == WebSocketState.Open && !token.IsCancellationRequested)
            {
                var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
                    break;
                }
            }
        }
        catch { /* client dropped */ }
        finally
        {
            _clients.TryRemove(id, out _);
            try { socket.Dispose(); } catch { }
            Console.WriteLine($"[ws] web client disconnected ({_clients.Count} total)");
            try { ClientDisconnected?.Invoke(); } catch { }
        }
    }

    /// <summary>Broadcast a snap event to all connected clients.</summary>
    public void BroadcastSnap(string model, string vid, string pid)
    {
        var payload =
            $"{{\"type\":\"snap\",\"source\":\"hardware-button\",\"model\":\"{Escape(model)}\"," +
            $"\"vid\":\"0x{vid}\",\"pid\":\"0x{pid}\",\"ts\":{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}}}";
        var bytes = Encoding.UTF8.GetBytes(payload);

        foreach (var (id, socket) in _clients)
        {
            if (socket.State != WebSocketState.Open) continue;
            try { socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None); }
            catch { _clients.TryRemove(id, out _); }
        }
    }

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    public async ValueTask DisposeAsync()
    {
        try { _cts?.Cancel(); } catch { }
        foreach (var (_, socket) in _clients)
        {
            try { await socket.CloseAsync(WebSocketCloseStatus.EndpointUnavailable, "shutdown", CancellationToken.None); } catch { }
            socket.Dispose();
        }
        _clients.Clear();
        try { if (_listener.IsListening) _listener.Stop(); } catch { }
        _listener.Close();
        if (_acceptLoop != null) { try { await _acceptLoop; } catch { } }
        _cts?.Dispose();
    }
}
