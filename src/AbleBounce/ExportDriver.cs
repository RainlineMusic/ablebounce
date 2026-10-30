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
        if (Native.GetDpiForWindow(Live.MainWindowHandle) != profile.Dpi)
            throw new InvalidOperationException("Live’s display scale changed. Recalibrate before continuing.");
        if (Native.TryExportWindow(Live.Id, out var now))
        {
            if (now.Width != profile.Width || now.Height != profile.Height)
                throw new InvalidOperationException("Live’s Export window size changed. Run the three-point calibration again.");
            _rect = now;
        }
    }
    private (int X, int Y) Position(string key)
    { var a = profile.Anchors[key]; return (_rect.Left + a.X, _rect.Top + a.Y); }
    private bool Matches(string key)
    {
        Guard();
        if (!Native.TryExportWindow(Live.Id, out var located)) return false;
        _rect = located;
        var (x, y) = Position(key);
        var actual = Native.Patch(x, y); var expected = profile.Anchors[key].Reference;
        if (actual.Length != expected.Length) return false;
        double error = actual.Zip(expected, (a, b) => Math.Abs(a - b)).Average();
        return error <= 8;
    }
    private async Task Click(string key, CancellationToken ct)
    {
        Guard();
        if (!Native.TryExportWindow(Live.Id, out _rect)) throw new InvalidOperationException("Live’s Export window is not open.");
        var (x, y) = Position(key); Native.Click(x, y);
        await Task.Delay(140, ct);
    }
    private async Task Away(CancellationToken ct)
    {
        Guard();
        if (!Native.TryExportWindow(Live.Id, out var located))
            throw new InvalidOperationException("Export window is not open; no cursor movement was sent.");
        _rect = located;
        Native.SetCursorPos(_rect.Left + 20, _rect.Top + 12);
        await Task.Delay(180, ct);
    }
    private async Task Choose(string key, int index, CancellationToken ct)
    {
        await Click(key, ct); Guard(); Native.Key(Native.Home);
        for (var i = 0; i < index; i++) { Guard(); Native.Key(Native.Down); }
        Guard(); Native.Key(Native.Enter); await Away(ct);
    }
    private async Task WaitExportDialog(CancellationToken ct)
    {
        var end = DateTime.UtcNow.AddSeconds(12);
        while (DateTime.UtcNow < end)
        {
            ct.ThrowIfCancellationRequested();
            if (Native.TryExportWindow(Live.Id, out var located))
            {
                _rect = located;
                await Away(ct);
                if (Matches("export")) return;
            }
            await Task.Delay(200, ct);
        }
        throw new InvalidOperationException("Export Audio/Video did not match the calibrated dialog. Close other dialogs and recalibrate.");
    }
    private static nint FileDialog(int process) => WindowsSaveDialog.Find(process);
    private async Task SaveDialog(string path, CancellationToken ct)
    {
        var end = DateTime.UtcNow.AddSeconds(15);
        nint dialog = 0;
        while (DateTime.UtcNow < end && dialog == 0)
        { ct.ThrowIfCancellationRequested(); Guard(); dialog = FileDialog(Live.Id); if (dialog == 0) await Task.Delay(200, ct); }
        if (dialog == 0) throw new InvalidOperationException("The Windows Save dialog was not detected. No filename was entered.");
        Guard();
        await WindowsSaveDialog.Submit(dialog, Live.Id, path, ct);
    }
    public async Task<List<string>> Render(int job, int bits, string directory, int expectedFiles, Action<string> report, CancellationToken ct)
    {
        Attach(); await Native.Focus(Live, ct);
        if (Directory.EnumerateFileSystemEntries(directory).Any()) throw new IOException("The staging directory must be empty.");
        report("Opening Live’s Export Audio/Video window…");
        Guard();
        if (!Native.TryExportWindow(Live.Id, out _))
            Native.Chord(Native.Ctrl, Native.Shift, (ushort)'R');
        await WaitExportDialog(ct);
        report("Export window found. Selecting render source…");
        var sourceKey = job is 8 or 9 ? "source" : "source_main";
        if (!Matches(sourceKey))
        {
            await Choose("source", job is 8 or 9 ? 1 : 0, ct);
            if (!Matches(sourceKey))
                throw new InvalidOperationException("Cannot verify the selected render source. Recalibrate Main and All Individual Tracks.");
        }
        // Fixed export switches are deliberately user-managed. Only source,
        // bit depth and Export are automated and calibrated.
        await Away(ct);
        if (!Matches("export")) throw new InvalidOperationException("Export dialog changed before rendering. Recalibrate.");
        var output = Path.Combine(directory, "glucose.wav");
        report("Confirming WAV export…");
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
            if (files.Count < expectedFiles || FileDialog(Live.Id) != 0) continue;
            try
            {
                foreach (var file in files) WaveFile.Inspect(file, bits, exclusive: true);
                // A valid header can appear before render completion. Require
                // Live's main thread to respond, and no modal window to remain.
                if (ModalWindowExists(Live.Id)) continue;
                var status = await bridge.Call("status", cancellation: ct, timeoutSeconds: 3);
                if (!status.GetProperty("idle").GetBoolean()) continue;
                await Task.Delay(450, ct);
                var again = Directory.GetFiles(directory, "*.wav");
                if (again.Length != files.Count) continue;
                foreach (var file in again) WaveFile.Inspect(file, bits, exclusive: true);
                result = again.ToList(); break;
            }
            catch (IOException validationError)
            {
                // A half-written WAV is normal during rendering. Once Live
                // confirms it is idle, wrong encoding/size is a real failure,
                // not a reason to keep the user waiting for the full timeout.
                try
                {
                    var status = await bridge.Call("status", cancellation: ct, timeoutSeconds: 3);
                    if (status.GetProperty("idle").GetBoolean() && !ModalWindowExists(Live.Id))
                        throw new InvalidDataException("Live finished, but the WAV could not be validated: " + validationError.Message, validationError);
                }
                catch (TimeoutException) { }
            }
            catch (TimeoutException) { }
        }
        if (result.Count == 0) throw new TimeoutException("Export timed out. Keep Live open; use Restore after its render has stopped.");
        if (job != 8 && job != 9 && result.Count != 1) throw new IOException("Unexpected number of files for a single mix render.");
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
        try
        {
            var initial = await bridge.Call("status", timeoutSeconds: 3);
            if (initial.GetProperty("idle").GetBoolean() && !ModalWindowExists(Live.Id))
            { RenderMayBeActive = false; return; }
        }
        catch (TimeoutException) { }
        await Native.Focus(Live, CancellationToken.None);
        // At most two Esc presses: Save/Export dialog or the current render.
        for (int attempt = 0; attempt < 2; attempt++)
        {
            Guard(); Native.Key(Native.Esc); await Task.Delay(700);
            try
            {
                var status = await bridge.Call("status", timeoutSeconds: 3);
                if (status.GetProperty("idle").GetBoolean() && !ModalWindowExists(Live.Id)) { RenderMayBeActive = false; return; }
            }
            catch (TimeoutException) { }
        }
        throw new InvalidOperationException("Live still has a render or modal dialog open. Cancel it in Live, then click Restore Set.");
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
        if ((expectedBits != 0 && bits != expectedBits) || bits is not (16 or 24 or 32) || channels != 2 || rate <= 0 || block <= 0 || data <= 0 || data % block != 0)
            throw new IOException("Unexpected WAV bit depth, channels or unfinished data: " + Path.GetFileName(path));
        if ((bits == 32 && format != 3) || (bits != 32 && format != 1)) throw new IOException("Unexpected PCM/float WAV encoding.");
        return new(bits, channels, rate, data / block);
    }
}

