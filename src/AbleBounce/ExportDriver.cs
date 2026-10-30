using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows.Automation;

namespace AbleBounce;

public sealed class ExportDriver(ExportProfile profile, BridgeClient bridge)
{
    private Process? _live;
    private Native.Rect _rect;
    public bool RenderMayBeActive { get; private set; }
    private Process Live => _live ?? throw new InvalidOperationException("Live is not attached.");
    private void Attach()
    {
        _live = Native.LiveProcess();
        Native.GetWindowRect(Live.MainWindowHandle, out _rect);
    }
    private void Guard()
    {
        Native.RequireForeground(Live.Id);
        Native.GetWindowRect(Live.MainWindowHandle, out var now);
        if (now.Width != profile.Width || now.Height != profile.Height || Native.GetDpiForWindow(Live.MainWindowHandle) != profile.Dpi)
            throw new InvalidOperationException("Live's window geometry changed during export. Recalibrate before continuing.");
        _rect = now;
    }
    private (int X, int Y) Position(string key)
    { var a = profile.Anchors[key]; return (_rect.Left + a.X, _rect.Top + a.Y); }
    private bool Matches(string key)
    {
        Guard(); var (x, y) = Position(key);
        var actual = Native.Patch(x, y); var expected = profile.Anchors[key].Reference;
        if (actual.Length != expected.Length) return false;
        double error = actual.Zip(expected, (a, b) => Math.Abs(a - b)).Average();
        return error <= 8;
    }
    private async Task Click(string key, CancellationToken ct)
    {
        Guard(); var (x, y) = Position(key); Native.Click(x, y);
        await Task.Delay(140, ct);
    }
    private async Task Away(CancellationToken ct)
    { Native.SetCursorPos(_rect.Left + 20, _rect.Top + 12); await Task.Delay(180, ct); }
    private async Task Choose(string key, int index, CancellationToken ct)
    {
        await Click(key, ct); Guard(); Native.Key(Native.Home);
        for (var i = 0; i < index; i++) { Guard(); Native.Key(Native.Down); }
        Guard(); Native.Key(Native.Enter); await Away(ct);
    }
    private async Task EnsureSwitch(string key, CancellationToken ct)
    {
        await Away(ct);
        if (Matches(key)) return;
        await Click(key, ct); await Away(ct);
        if (!Matches(key)) throw new InvalidOperationException($"Cannot verify the '{key}' export setting. Check Live's dialog and recalibrate.");
    }
    private async Task WaitExportDialog(CancellationToken ct)
    {
        var end = DateTime.UtcNow.AddSeconds(12);
        while (DateTime.UtcNow < end)
        {
            ct.ThrowIfCancellationRequested();
            if (Matches("export")) return;
            await Task.Delay(200, ct);
        }
        throw new InvalidOperationException("Export Audio/Video did not match the calibrated dialog. Close other dialogs and recalibrate.");
    }
    private static AutomationElement? FileDialog(int process)
    {
        var windows = AutomationElement.RootElement.FindAll(TreeScope.Children,
            new PropertyCondition(AutomationElement.ProcessIdProperty, process));
        foreach (AutomationElement w in windows)
        {
            if (w.Current.ClassName == "#32770" && w.Current.IsEnabled && !w.Current.IsOffscreen)
            {
                var edit = w.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "1001"));
                if (edit != null) return w;
                // Modern common file dialogs expose a File name combo/edit.
                edit = w.FindFirst(TreeScope.Descendants, new AndCondition(
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit),
                    new OrCondition(new PropertyCondition(AutomationElement.NameProperty, "File name:"),
                                    new PropertyCondition(AutomationElement.NameProperty, "File name"))));
                if (edit != null) return w;
            }
        }
        return null;
    }
    private async Task SaveDialog(string path, CancellationToken ct)
    {
        var end = DateTime.UtcNow.AddSeconds(15);
        AutomationElement? dialog = null;
        while (DateTime.UtcNow < end && dialog == null)
        { ct.ThrowIfCancellationRequested(); Guard(); dialog = FileDialog(Live.Id); if (dialog == null) await Task.Delay(200, ct); }
        if (dialog == null) throw new InvalidOperationException("The Windows Save dialog was not detected. No filename was entered.");
        Guard(); Native.Chord(Native.Alt, (ushort)'N'); await Task.Delay(100, ct);
        Guard(); Native.Chord(Native.Ctrl, (ushort)'A'); Native.Text(path);
        await Task.Delay(150, ct);
        // Read back the entered path through UIA where possible, never confirm
        // a different name in an unrelated dialog.
        var focused = AutomationElement.FocusedElement;
        if (focused.Current.ProcessId != Live.Id || !focused.TryGetCurrentPattern(ValuePattern.Pattern, out var valuePattern))
            throw new InvalidOperationException("Cannot verify the Save dialog filename. Leave Live open and retry.");
        var entered = ((ValuePattern)valuePattern).Current.Value;
        if (!string.Equals(entered, path, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Save dialog filename readback did not match.");
        Guard(); Native.Key(Native.Enter);
    }
    public async Task<string> CreateWorkingCopy(SessionInfo session, CancellationToken ct)
    {
        Attach(); await Native.Focus(Live, ct);
        var directory = Path.GetDirectoryName(session.Path) ?? throw new InvalidOperationException("The source Set is not saved.");
        var path = Path.Combine(directory, $"{Storage.SafeName(Path.GetFileNameWithoutExtension(session.Path))}__AbleBounce_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid().ToString("N")[..6]}.als");
        if (File.Exists(path)) throw new IOException("Working Set already exists.");
        Guard(); Native.Chord(Native.Ctrl, Native.Shift, (ushort)'S');
        await SaveDialog(path, ct);
        var end = DateTime.UtcNow.AddSeconds(25);
        while (DateTime.UtcNow < end)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var scan = await bridge.Scan(ct);
                if (string.Equals(scan.Path, path, StringComparison.OrdinalIgnoreCase) && File.Exists(path)) return path;
            }
            catch (TimeoutException) { }
            await Task.Delay(300, ct);
        }
        throw new InvalidOperationException("Live did not confirm the working Set path. Nothing has been changed by the bridge.");
    }
    public async Task<List<string>> Render(int job, int bits, string directory, Action<string> report, CancellationToken ct)
    {
        Attach(); await Native.Focus(Live, ct);
        if (Directory.EnumerateFileSystemEntries(directory).Any()) throw new IOException("The staging directory must be empty.");
        Guard(); Native.Chord(Native.Ctrl, Native.Shift, (ushort)'R');
        await Away(ct); await WaitExportDialog(ct);
        await Choose("source", job == 8 ? 1 : 0, ct);
        await EnsureSwitch("pcm", ct);
        await Choose("file_type", 0, ct); // Live's documented WAV, AIFF, FLAC order
        await Choose("bit_depth", bits == 16 ? 0 : bits == 24 ? 1 : 2, ct);
        if (bits != 32) await Choose("dither", 0, ct);
        foreach (string key in new[] { "normalize", "mono", "loop", "mp3" }) await EnsureSwitch(key, ct);
        if (job == 8) await EnsureSwitch("include", ct);
        await Away(ct);
        if (!Matches("export")) throw new InvalidOperationException("Export dialog changed before rendering. Recalibrate.");
        var output = Path.Combine(directory, "AbleBounce.wav");
        await Click("export", ct);
        RenderMayBeActive = true; // before confirmation, so cancellation also closes Save
        await SaveDialog(output, ct);
        report("Rendering in Live. The export range and sample rate come from your Live dialog.");
        List<string> result = [];
        var timeout = DateTime.UtcNow.AddHours(2);
        while (DateTime.UtcNow < timeout)
        {
            await Task.Delay(800, ct);
            // Do not require focus during an uninterrupted render. No UI input
            // is sent until focus and process identity have been verified again.
            var files = Directory.GetFiles(directory, "*.wav").ToList();
            if (files.Count == 0 || FileDialog(Live.Id) != null) continue;
            try
            {
                foreach (var file in files) WaveFile.Inspect(file, bits, exclusive: true);
                // A valid header can appear before render completion. Require
                // Live's main thread to respond, and no modal window to remain.
                if (ModalWindowExists(Live.Id)) continue;
                await bridge.Call("ping", cancellation: ct, timeoutSeconds: 3);
                await Task.Delay(450, ct);
                var again = Directory.GetFiles(directory, "*.wav");
                if (again.Length != files.Count) continue;
                foreach (var file in again) WaveFile.Inspect(file, bits, exclusive: true);
                result = again.ToList(); break;
            }
            catch (IOException) { }
            catch (TimeoutException) { }
        }
        if (result.Count == 0) throw new TimeoutException("Export timed out. Keep Live open; use Restore after its render has stopped.");
        if (job != 8 && result.Count != 1) throw new IOException("Unexpected number of files for a single mix render.");
        var waves = result.Select(f => WaveFile.Inspect(f, bits)).ToArray();
        if (waves.Any(w => w.Frames != waves[0].Frames || w.SampleRate != waves[0].SampleRate))
            throw new IOException("The exported files have different lengths or sample rates.");
        RenderMayBeActive = false;
        return result;
    }
    private static bool ModalWindowExists(int process)
    {
        var windows = AutomationElement.RootElement.FindAll(TreeScope.Children, new PropertyCondition(AutomationElement.ProcessIdProperty, process));
        foreach (AutomationElement w in windows)
            if (w.TryGetCurrentPattern(WindowPattern.Pattern, out var pattern) && ((WindowPattern)pattern).Current.IsModal) return true;
        return false;
    }
    public async Task EnsureStopped()
    {
        Attach();
        if (!RenderMayBeActive && !ModalWindowExists(Live.Id)) return;
        await Native.Focus(Live, CancellationToken.None);
        // At most two Esc presses: Save/Export dialog or the current render.
        for (int attempt = 0; attempt < 2; attempt++)
        {
            Guard(); Native.Key(Native.Esc); await Task.Delay(700);
            try
            {
                await bridge.Call("ping", timeoutSeconds: 3);
                if (!ModalWindowExists(Live.Id)) { RenderMayBeActive = false; return; }
            }
            catch (TimeoutException) { }
        }
        throw new InvalidOperationException("Live still has a render or modal dialog open. Cancel it in Live, then click Restore working Set.");
    }
}

public sealed record WaveInfo(int Bits, int Channels, int SampleRate, long Frames);
public static class WaveFile
{
    public static WaveInfo Inspect(string path, int expectedBits, bool exclusive = false)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, exclusive ? FileShare.None : FileShare.Read);
        using var reader = new BinaryReader(file, Encoding.ASCII);
        string Four() => Encoding.ASCII.GetString(reader.ReadBytes(4));
        if (file.Length < 44 || Four() != "RIFF") throw new IOException("Not a finished RIFF WAV file: " + path);
        long riff = reader.ReadUInt32() + 8L;
        if (Four() != "WAVE" || riff != file.Length) throw new IOException("WAV size is incomplete or unsupported (>4 GB): " + path);
        int format = 0, channels = 0, rate = 0, bits = 0, block = 0; long data = -1;
        while (file.Position + 8 <= file.Length)
        {
            var id = Four(); var size = reader.ReadUInt32(); long end = file.Position + size;
            if (end > file.Length) throw new IOException("Truncated WAV chunk.");
            if (id == "fmt ")
            {
                if (size < 16) throw new IOException("Invalid WAV format.");
                format = reader.ReadUInt16(); channels = reader.ReadUInt16(); rate = reader.ReadInt32();
                reader.ReadUInt32(); block = reader.ReadUInt16(); bits = reader.ReadUInt16();
                if (format == 0xFFFE && size >= 40) { reader.ReadUInt16(); reader.ReadUInt16(); reader.ReadUInt32(); format = reader.ReadUInt16(); }
            }
            if (id == "data") data = size;
            file.Position = end + (size & 1);
        }
        if (bits != expectedBits || channels != 2 || rate <= 0 || block <= 0 || data <= 0 || data % block != 0)
            throw new IOException("Unexpected WAV bit depth, channels or unfinished data: " + Path.GetFileName(path));
        if ((bits == 32 && format != 3) || (bits != 32 && format != 1)) throw new IOException("Unexpected PCM/float WAV encoding.");
        return new(bits, channels, rate, data / block);
    }
}
