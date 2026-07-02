using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Hairscope.Agent;

/// <summary>
/// Hairscope Agent — a device agent for supported trichoscopy probes.
///
/// Runs as a console app (development) or as a Windows Service (production, via
/// UseWindowsService). It keeps a localhost WebSocket listener open and starts the
/// USBPcap-based hardware-button capture only while the web app is connected.
/// </summary>
public static class Program
{
    public static void Main(string[] args)
    {
        SetupLogging();
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            var ex = e.ExceptionObject as Exception;
            Console.Error.WriteLine($"[FATAL] Unhandled exception (terminating={e.IsTerminating}):");
            Console.Error.WriteLine(ex?.ToString() ?? e.ExceptionObject?.ToString() ?? "(unknown)");
            try { Console.Out.Flush(); } catch { }
        };

        try
        {
            var builder = Host.CreateApplicationBuilder(args);
            builder.Services.AddWindowsService(o => o.ServiceName = "HairscopeAgent");
            builder.Services.AddHostedService<AgentWorker>();
            builder.Build().Run();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("[FATAL] Host terminated:");
            Console.Error.WriteLine(ex.ToString());
        }
    }

    /// <summary>Tee console output to %LOCALAPPDATA%\Hairscope\agent.log (and, for the
    /// service which runs as LocalSystem, that resolves under the service profile).</summary>
    private static void SetupLogging()
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Hairscope");
            Directory.CreateDirectory(dir);
            var logPath = Path.Combine(dir, "agent.log");
            try
            {
                if (File.Exists(logPath) && new FileInfo(logPath).Length > 2_000_000)
                    File.Delete(logPath);
            }
            catch { }
            var fileWriter = new StreamWriter(logPath, append: true) { AutoFlush = true };
            var tee = new TeeTextWriter(Console.Out, fileWriter);
            Console.SetOut(tee);
            Console.SetError(tee);
            Console.WriteLine($"[log] writing to {logPath}");
        }
        catch { /* logging is best-effort */ }
    }
}

/// <summary>Writes to two TextWriters at once (console + log file).</summary>
internal sealed class TeeTextWriter : System.IO.TextWriter
{
    private readonly System.IO.TextWriter _a;
    private readonly System.IO.TextWriter _b;
    public TeeTextWriter(System.IO.TextWriter a, System.IO.TextWriter b) { _a = a; _b = b; }
    public override System.Text.Encoding Encoding => _b.Encoding;
    public override void Write(char value) { try { _a.Write(value); } catch { } _b.Write(value); }
    public override void Write(string? value) { try { _a.Write(value); } catch { } _b.Write(value); }
    public override void WriteLine(string? value) { try { _a.WriteLine(value); } catch { } _b.WriteLine(value); }
    public override void WriteLine() { try { _a.WriteLine(); } catch { } _b.WriteLine(); }
    public override void Flush() { try { _a.Flush(); } catch { } _b.Flush(); }
}
