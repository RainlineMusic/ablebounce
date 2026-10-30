using System.IO;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace AbleBounce;

public sealed class BridgeClient
{
    public const int Port = 16846;
    // Wire framing is independent of pretty-printed settings files.
    private static readonly JsonSerializerOptions WireJson = new(Storage.Json) { WriteIndented = false };
    public static byte[] EncodeRequest(string id, string token, string method, object? args = null) =>
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { id, token, method, args = args ?? new { } }, WireJson) + "\n");
    public async Task<JsonElement> Call(string method, object? args = null, CancellationToken cancellation = default, int timeoutSeconds = 12)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        var tokenFile = Path.Combine(Storage.Root, "bridge-token");
        if (!File.Exists(tokenFile)) throw new InvalidOperationException("Enable AbleBounce in Live → Settings → Link, Tempo & MIDI → Control Surface. Set MIDI Input and Output to None.");
        var token = (await File.ReadAllTextAsync(tokenFile, timeout.Token)).Trim();
        var id = Guid.NewGuid().ToString("N");
        using var client = new TcpClient();
        try
        {
            await client.ConnectAsync("127.0.0.1", Port, timeout.Token);
            await using var stream = client.GetStream();
            var bytes = EncodeRequest(id, token, method, args);
            await stream.WriteAsync(bytes, timeout.Token);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var line = await reader.ReadLineAsync(timeout.Token) ?? throw new IOException("Live closed the bridge connection.");
            if (line.Length > 8 * 1024 * 1024) throw new IOException("Bridge response exceeded the size limit.");
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.GetProperty("id").GetString() != id) throw new IOException("Bridge response did not match the request.");
            if (!root.GetProperty("ok").GetBoolean()) throw new InvalidOperationException(root.GetProperty("error").GetString());
            return root.GetProperty("result").Clone();
        }
        catch (SocketException ex) { throw new IOException("Cannot reach Live. Start Live and enable the AbleBounce control surface.", ex); }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        { throw new TimeoutException("Live did not respond. Close modal dialogs, or wait for its current render to finish."); }
    }
    public async Task<SessionInfo> Scan(CancellationToken ct = default) =>
        (await Call("scan", cancellation: ct)).Deserialize<SessionInfo>(Storage.Json) ?? throw new IOException("Empty Live scan.");

    public static void Install(string userLibrary)
    {
        if (!Directory.Exists(userLibrary)) throw new DirectoryNotFoundException("Select your existing Ableton User Library folder.");
        var destination = Path.Combine(userLibrary, "Remote Scripts", "AbleBounce");
        Directory.CreateDirectory(destination);
        var assembly = Assembly.GetExecutingAssembly();
        var resources = assembly.GetManifestResourceNames().Where(n => n.EndsWith(".py", StringComparison.Ordinal)).ToArray();
        if (resources.Length != 3) throw new IOException("The embedded Live bridge is incomplete. Reinstall AbleBounce.");
        foreach (var resource in resources)
        {
            // EmbeddedResource identifiers include the logical Bridge path.
            var name = resource.EndsWith("__init__.py", StringComparison.Ordinal) ? "__init__.py" :
                       resource.EndsWith("engine.py", StringComparison.Ordinal) ? "engine.py" : "surface.py";
            using var source = assembly.GetManifestResourceStream(resource)!;
            using var target = File.Create(Path.Combine(destination, name));
            source.CopyTo(target);
        }
    }
}
