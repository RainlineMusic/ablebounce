using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows.Automation;
using AbleBounce;

int checks = 0;
void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
void Reject(Action action, string message)
{ try { action(); } catch (IOException) { checks++; return; } throw new Exception(message); }
string temp = Path.Combine(Path.GetTempPath(), "AbleBounce-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temp);
try
{
    var wire = BridgeClient.EncodeRequest("request-123", "test-token", "scan",
        new { nested = new { label = "Lead\nVocal", unicode = "Бэки" } });
    Check(wire.Count(b => b == (byte)'\n') == 1 && wire[^1] == (byte)'\n',
        "Bridge request must contain exactly one physical newline, at its end");
    using (var request = JsonDocument.Parse(wire.AsMemory(0, wire.Length - 1)))
    {
        Check(request.RootElement.GetProperty("id").GetString() == "request-123", "Request ID changed");
        Check(request.RootElement.GetProperty("args").GetProperty("nested").GetProperty("label").GetString() == "Lead\nVocal",
            "Escaped newline did not round-trip");
    }
    Check(new TrackRow(new TrackInfo { Parent = "group", SuggestedRole = "Lead" }, 1, 1).Role == "Follow Group", "Grouped tracks must default to Follow Group");
    Check(new TrackRow(new TrackInfo { Parent = "group" }, 1, 1, "Backing").Role == "Backing", "Explicit child roles must be preserved");
    Check(Storage.Json.WriteIndented, "Wire encoding must not change settings serialization");
    foreach (var version in new[] { "12.4.0", "12.4.5", "12.4.6", "12.4.99" })
        Check(LiveCompatibility.IsSupported(version), $"Supported patch rejected: {version}");
    foreach (var version in new[] { "", "12.4", "12.4.5.1", "12.4.5b1", "12.5.0", "12.3.9", "11.4.5", "13.4.5" })
        Check(!LiveCompatibility.IsSupported(version), $"Unsupported version accepted: {version}");
    Check(!LiveCompatibility.IsSupported(null), "Null version accepted");
    var calibrationRejected = false;
    try { new ExportProfile { Version = "12.4.6" }.Validate(new SessionInfo { Version = "12.4.5" }); }
    catch (InvalidOperationException) { calibrationRejected = true; }
    Check(calibrationRejected, "Calibration must remain specific to the exact Live patch");
    Check(ExportProfile.Fields.Length == 2, "Calibration must require only three user captures");
    Check(ExportProfile.Fields.Select(f => f.Key).SequenceEqual(new[] { "source", "export" }),
        "Unexpected calibration controls");
    Check(ExportProfile.RequiredReferences.Length == 3, "Automatic calibration must retain six verification states");
    var shown = new TaskCompletionSource<System.Windows.Window>(TaskCreationOptions.RunContinuationsAsynchronously);
    var windowThread = new Thread(() =>
    {
        var window = new System.Windows.Window { Title = "Export Audio/Video", Width = 360, Height = 720, Left = 30, Top = 30 };
        window.Show(); shown.TrySetResult(window);
        System.Windows.Threading.Dispatcher.Run();
    }) { IsBackground = true };
    windowThread.SetApartmentState(ApartmentState.STA); windowThread.Start();
    var exportWindow = await shown.Task.WaitAsync(TimeSpan.FromSeconds(30));
    try
    {
        Check(Native.TryExportWindow(Environment.ProcessId, out var first), "Export dialog not located by title/process");
        Check(!Native.TryExportWindow(int.MaxValue, out _), "Export dialog matched a different process");
        await exportWindow.Dispatcher.InvokeAsync(() => { exportWindow.Left += 100; exportWindow.Top += 50; });
        Check(Native.TryExportWindow(Environment.ProcessId, out var moved), "Moved export dialog was lost");
        Check(moved.Left != first.Left && moved.Top != first.Top && moved.Width == first.Width && moved.Height == first.Height,
            "Moved dialog coordinates were not refreshed");
        await exportWindow.Dispatcher.InvokeAsync(() =>
        {
            var duplicate = new System.Windows.Window { Title = "Export Audio/Video", Width = 360, Height = 720 };
            duplicate.Show();
            Check(!Native.TryExportWindow(Environment.ProcessId, out _), "Ambiguous export windows must be rejected");
            duplicate.Close();
        });
    }
    finally { await exportWindow.Dispatcher.InvokeAsync(() => { exportWindow.Close(); exportWindow.Dispatcher.InvokeShutdown(); }); }
    async Task CheckNativeSaveDialog(string directory, string filename)
    {
        Directory.CreateDirectory(directory);
        var expected = Path.Combine(directory, filename);
        var finished = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var prompt = new Microsoft.Win32.SaveFileDialog
                {
                    InitialDirectory = directory, FileName = "initial.als", DefaultExt = ".als",
                    Filter = "Ableton Live Set|*.als", CheckPathExists = true,
                    Title = "AbleBounce save regression"
                };
                finished.TrySetResult(prompt.ShowDialog() == true ? prompt.FileName : "");
            }
            catch (Exception ex) { finished.TrySetException(ex); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        var process = Environment.ProcessId;
        nint dialog = 0;
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline && dialog == 0 && !finished.Task.IsCompleted)
        { dialog = WindowsSaveDialog.Find(process); if (dialog == 0) await Task.Delay(100); }
        if (dialog == 0) throw new Exception("Native Windows Save dialog was not recognised: " + WindowsSaveDialog.Describe(process));
        var sentinel = Path.Combine(directory, "existing.als"); File.WriteAllText(sentinel, "original");
        Reject(() => WindowsSaveDialog.Submit(dialog!, process, sentinel).GetAwaiter().GetResult(), "Save helper accepted an overwrite");
        Check(File.ReadAllText(sentinel) == "original", "Existing source was modified");
        await WindowsSaveDialog.Submit(dialog!, process, expected);
        var selected = await finished.Task.WaitAsync(TimeSpan.FromSeconds(20));
        Check(string.Equals(selected, expected, StringComparison.OrdinalIgnoreCase), $"Native Save path did not match. Expected: {expected}; selected: {selected}");
    }
    await CheckNativeSaveDialog(Path.Combine(temp, "Native save"), "Working copy.als");
    await CheckNativeSaveDialog(Path.Combine(temp, "Проект с пробелами"), "Вокал__AbleBounce_тест.als");
    string Wav(int bits, int format = 1, int channels = 2, int frames = 100)
    {
        var path = Path.Combine(temp, Guid.NewGuid() + ".wav");
        using var f = File.Create(path); using var w = new BinaryWriter(f, Encoding.ASCII);
        int block = channels * bits / 8, data = frames * block;
        w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + data); w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        w.Write(16); w.Write((ushort)format); w.Write((ushort)channels); w.Write(48000); w.Write(48000 * block);
        w.Write((ushort)block); w.Write((ushort)bits); w.Write(Encoding.ASCII.GetBytes("data")); w.Write(data); w.Write(new byte[data]);
        return path;
    }
    foreach (int bits in new[] { 16, 24, 32 })
    {
        var wave = WaveFile.Inspect(Wav(bits, bits == 32 ? 3 : 1), bits);
        Check(WaveFile.Inspect(Wav(bits, bits == 32 ? 3 : 1), 0).Bits == bits, "Live-selected bit depth rejected");
        Check(wave.Frames == 100 && wave.SampleRate == 48000 && wave.Bits == bits, "Valid WAV rejected");
    }
    Reject(() => WaveFile.Inspect(Wav(16), 24), "Wrong bit depth accepted");
    Reject(() => WaveFile.Inspect(Wav(32, 1), 32), "Integer 32-bit accepted instead of float");
    Reject(() => WaveFile.Inspect(Wav(16, channels: 1), 16), "Unexpected mono accepted");
    Reject(() => WaveFile.Inspect(Wav(16, frames: 0), 16), "Empty render accepted");
    var truncated = Wav(24); using (var f = File.OpenWrite(truncated)) f.SetLength(47);
    Reject(() => WaveFile.Inspect(truncated, 24), "Truncated render accepted");
    var unfinished = Wav(16); using (var f = File.OpenWrite(unfinished)) { f.Position = 4; f.Write(new byte[4]); }
    Reject(() => WaveFile.Inspect(unfinished, 16), "Unfinished RIFF accepted");
    var safe = Storage.SafeName("A<>:\"/\\|?*Song. ");
    Check(safe.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && !safe.EndsWith('.'), "Unsafe export filename");
    Check(Storage.SafeName(new string('x', 200)).Length <= 90, "Export filename was not bounded");
    Check(DeliveryRow.Create().Select(d => d.Id).SequenceEqual(new[] { 1, 4, 2, 3, 6, 5, 8, 9 }), "Delivery order changed");
    Check(DeliveryRow.Create().Select(d => d.Title).SequenceEqual(new[] { "Master", "Mix", "Instrumental Master", "Instrumental Mix", "Backing Master", "Backing Mix", "Multitrack / Stems", "Selected Tracks (Dry)" }), "Delivery labels changed");
    var library = Path.Combine(temp, "User Library"); Directory.CreateDirectory(library);
    BridgeClient.Install(library);
    var script = Path.Combine(library, "Remote Scripts", "AbleBounce");
    Check(Directory.GetFiles(script, "*.py").Length == 3, "Packaged bridge file count is wrong");
    Check(File.ReadAllText(Path.Combine(script, "surface.py")).Contains("class AbleBounce"), "Bridge surface resource is wrong");
    Check(File.ReadAllText(Path.Combine(script, "engine.py")).Contains("class Engine"), "Bridge engine resource is wrong");
    Console.WriteLine($"PASS: {checks} desktop core regression checks");
}
finally { Directory.Delete(temp, true); }

