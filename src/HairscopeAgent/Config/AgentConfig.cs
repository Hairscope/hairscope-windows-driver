using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hairscope.Agent.Config;

/// <summary>Root of devices.json. Generic across brands / series / devices.</summary>
public sealed class AgentConfig
{
    [JsonPropertyName("agent")] public AgentSettings Agent { get; set; } = new();
    [JsonPropertyName("brands")] public List<Brand> Brands { get; set; } = new();

    /// <summary>
    /// Load the configuration compiled into the assembly as an embedded resource.
    /// This is the authoritative source — there is no loose plaintext config file on
    /// disk to read, expose, or tamper with.
    /// </summary>
    public static AgentConfig LoadEmbedded()
    {
        var asm = typeof(AgentConfig).Assembly;
        var resourceName = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("devices.json", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Embedded devices.json resource not found.");

        using var stream = asm.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException("Embedded devices.json stream could not be opened.");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    /// <summary>Load configuration from an external file (dev/testing only).</summary>
    public static AgentConfig Load(string path) => Parse(File.ReadAllText(path));

    private static AgentConfig Parse(string json)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };
        return JsonSerializer.Deserialize<AgentConfig>(json, options)
               ?? throw new InvalidOperationException("devices.json could not be parsed.");
    }

    public IEnumerable<(Brand Brand, Series Series, Device Device)> AllDevices()
    {
        foreach (var b in Brands)
            foreach (var s in b.Series)
                foreach (var d in s.Devices)
                    yield return (b, s, d);
    }
}

public sealed class AgentSettings
{
    [JsonPropertyName("webSocket")] public WebSocketSettings WebSocket { get; set; } = new();
}

public sealed class WebSocketSettings
{
    [JsonPropertyName("host")] public string Host { get; set; } = "127.0.0.1";
    [JsonPropertyName("port")] public int Port { get; set; } = 8891;
    /// <summary>Allowed browser Origin headers. Empty = allow any (dev only).</summary>
    [JsonPropertyName("allowedOrigins")] public List<string> AllowedOrigins { get; set; } = new();
}

public sealed class Brand
{
    [JsonPropertyName("brand")] public string Name { get; set; } = "";
    [JsonPropertyName("vendorName")] public string? VendorName { get; set; }
    [JsonPropertyName("series")] public List<Series> Series { get; set; } = new();
}

public sealed class Series
{
    [JsonPropertyName("series")] public string Name { get; set; } = "";
    [JsonPropertyName("connection")] public string Connection { get; set; } = "USB";
    [JsonPropertyName("devices")] public List<Device> Devices { get; set; } = new();
}

public sealed class Device
{
    [JsonPropertyName("model")] public string Model { get; set; } = "";
    [JsonPropertyName("productString")] public string? ProductString { get; set; }
    [JsonPropertyName("vid")] public string? Vid { get; set; }
    [JsonPropertyName("pid")] public string? Pid { get; set; }
    [JsonPropertyName("maxResolution")] public string? MaxResolution { get; set; }
    [JsonPropertyName("enabled")] public bool Enabled { get; set; }
    [JsonPropertyName("capabilities")] public Capabilities? Capabilities { get; set; }

    /// <summary>Normalised 4-hex VID, e.g. "21CD".</summary>
    public string? NormalizedVid => Normalize(Vid);
    /// <summary>Normalised 4-hex PID, e.g. "0834".</summary>
    public string? NormalizedPid => Normalize(Pid);

    private static string? Normalize(string? v)
    {
        if (string.IsNullOrWhiteSpace(v)) return null;
        v = v.Trim();
        if (v.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) v = v[2..];
        return v.ToUpperInvariant().PadLeft(4, '0');
    }
}

public sealed class Capabilities
{
    [JsonPropertyName("button")] public ButtonCapability? Button { get; set; }
}

public sealed class ButtonCapability
{
    /// <summary>Detection method, e.g. "uvc-still-trigger".</summary>
    [JsonPropertyName("method")] public string Method { get; set; } = "uvc-still-trigger";
    /// <summary>USB status endpoint the button event arrives on (default 0x87).</summary>
    [JsonPropertyName("endpoint")] public string Endpoint { get; set; } = "0x87";
    /// <summary>Status-packet payload (hex) that signals a press (default 02010001).</summary>
    [JsonPropertyName("pressPayload")] public string PressPayload { get; set; } = "02010001";

    public byte EndpointByte => ParseByte(Endpoint, 0x87);
    public byte[] PressBytes => ParseHex(PressPayload);

    private static byte ParseByte(string s, byte fallback)
    {
        s = s.Trim();
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s[2..];
        return byte.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out var b) ? b : fallback;
    }

    private static byte[] ParseHex(string s)
    {
        var hex = new string(s.Where(Uri.IsHexDigit).ToArray());
        if (hex.Length % 2 != 0 || hex.Length == 0) return Array.Empty<byte>();
        var bytes = new byte[hex.Length / 2];
        for (int i = 0; i < bytes.Length; i++)
            bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        return bytes;
    }
}
