using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace AbleBounce;

public partial class MainWindow : Window
{
    private readonly ViewModel _vm = new();
    private readonly BridgeClient _bridge = new();
    private readonly bool _demo;
    private SessionInfo? _session;
    private CancellationTokenSource? _cancellation;
    public MainWindow(bool demo = false)
    {
        _demo = demo; InitializeComponent(); DataContext = _vm;
        if (demo) LoadDemo();
        else
        {
            var settings = Storage.Read<Dictionary<string, string>>("settings.json");
            if (settings?.TryGetValue("output", out var output) == true) _vm.Output = output;
            if (settings?.TryGetValue("bitDepth", out var bits) == true && _vm.BitDepths.Contains(bits)) _vm.BitDepth = bits;
        }
        Closing += OnClosing;
    }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_vm.Busy)
        { e.Cancel = true; _vm.AddLog("Use Cancel & restore before closing glucose."); return; }
        if (!_demo)
        {
            SaveRoles();
            Storage.Write("settings.json", new Dictionary<string, string> { ["output"] = _vm.Output, ["bitDepth"] = _vm.BitDepth });
        }
    }
    private static string RoleFile(string path) => "roles/" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path.ToLowerInvariant())))[..24] + ".json";
    private void SaveRoles()
    {
        if (_session == null || _demo || _session.Transaction != null || string.IsNullOrWhiteSpace(_session.Path)) return;
        Storage.Write(RoleFile(_session.Path), _vm.Tracks.ToDictionary(t => t.Info.Key, t => new SavedRole(t.Name, t.Role)));
    }
    private sealed record SavedRole(string Name, string Role);
    private Dictionary<string, string> Roles() => _vm.Tracks.ToDictionary(t => t.Info.Key, t => t.Role);
    private async Task Connect()
    {
        var session = await _bridge.Scan();
        if (session.Protocol != 1) throw new InvalidOperationException("Bridge protocol mismatch. Install the bridge included with this version.");
        if (!LiveCompatibility.IsSupported(session.Version)) throw new InvalidOperationException($"Live {session.Version} is outside the supported 12.4.x branch. Use any Live 12.4 patch release.");
        _session = session;
        var saved = string.IsNullOrWhiteSpace(session.Path) ? null : Storage.Read<Dictionary<string, SavedRole>>(RoleFile(session.Path));
        _vm.Tracks.Clear();
        var tracks = session.Tracks.Where(t => !t.Master && !t.Return).ToArray();
        for (int i = 0; i < tracks.Length; i++)
        {
            var t = tracks[i]; int depth = 0; var parent = t.Parent;
            var visited = new HashSet<string>();
            while (parent != null && visited.Add(parent))
            { depth++; parent = tracks.FirstOrDefault(p => p.Key == parent)?.Parent; }
            string? role = saved != null && saved.TryGetValue(t.Key, out var s) && s.Name == t.Name ? s.Role : null;
            _vm.Tracks.Add(new TrackRow(t, i + 1, depth, role));
        }
        _vm.Project = string.IsNullOrEmpty(session.Name) ? "Untitled Set" : session.Name;
        _vm.Connection = "Live " + session.Version + " · Connected";
        _vm.Connected = true;
        _vm.AddLog(session.Transaction != null ? "An unfinished transaction exists. Click Restore Set before exporting." : "Connected. Review track roles and calibrate export before the first batch.");
    }
    private async void Connect_Click(object sender, RoutedEventArgs e)
    {
        if (_demo) { MessageBox.Show("Demo mode uses a synthetic Set. Restart without --demo to connect to Live."); return; }
        try { _vm.Busy = true; await Connect(); }
        catch (Exception ex) { _vm.Connected = false; _vm.Connection = "Not connected"; ShowError(ex); }
        finally { _vm.Busy = false; }
    }
    private void ChooseFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Choose export destination" };
        if (Directory.Exists(_vm.Output)) dialog.InitialDirectory = _vm.Output;
        if (dialog.ShowDialog(this) == true) _vm.Output = dialog.FolderName;
    }
    private void InstallBridge_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Select your Ableton User Library (not the Live application folder)" };
        var defaultPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Ableton", "User Library");
        if (Directory.Exists(defaultPath)) dialog.InitialDirectory = defaultPath;
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            BridgeClient.Install(dialog.FolderName);
            MessageBox.Show(this, "Bridge installed. Restart Live, then open Settings → Link, Tempo & MIDI.\n\nChoose AbleBounce in a free Control Surface slot. Set its Input and Output to None.\n\nReturn here and click Connect.", "Live bridge installed", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) { ShowError(ex); }
    }
    private void Calibrate_Click(object sender, RoutedEventArgs e)
    {
        if (_demo) { MessageBox.Show("Calibration needs a real Live export dialog."); return; }
        if (_session == null) { MessageBox.Show("Connect to Live first."); return; }
        if (MessageBox.Show(this,
            "In Live, open Export Audio/Video (Ctrl+Shift+R).\n\nSet:\n• Rendered Track: All Individual Tracks\n• WAV, 24-bit, No Dither\n• Encode PCM: On\n• Normalize, Convert to Mono, Render as Loop, MP3, Create Video: Off\n• Include Return and Main Effects: Off\n\nChoose your sample rate, render start and length (including tails). Keep the export dialog size, theme and display scale unchanged.\n\nLeave that dialog open, then start calibration. Point at Rendered Track, Bit Depth and Export in Live and press F8 once for each (three times total). Point at the displayed value text, not the arrow. The remaining states are captured automatically; do not interact with Live during that step.",
            "Prepare Live's export dialog", MessageBoxButton.OKCancel, MessageBoxImage.Information) != MessageBoxResult.OK) return;
        try
        {
            var wizard = new CalibrationWindow(_session.Version) { Owner = this };
            if (wizard.ShowDialog() == true)
                _vm.AddLog("Export calibration saved. Close Live's Export dialog before starting your batch.");
        }
        catch (Exception ex) { ShowError(ex); }
    }
    private void MarkRemaining_Click(object sender, RoutedEventArgs e)
    {
        foreach (var t in _vm.Tracks.Where(t => (t.Role == "Unassigned" || t.Role == "Inherit") && !t.Info.Group))
        {
            var parent = t.Info.Parent;
            string ancestorRole = "Unassigned";
            while (parent != null)
            {
                var row = _vm.Tracks.FirstOrDefault(p => p.Info.Key == parent);
                if (row == null) break;
                if (row.Role is "Lead" or "Backing" or "Instrument") { ancestorRole = row.Role; break; }
                parent = row.Info.Parent;
            }
            t.Role = ancestorRole == "Unassigned" ? "Instrument" : "Inherit";
        }
        SaveRoles(); _vm.AddLog("Unassigned leaf tracks marked. Review vocal groups before exporting.");
    }
    private void SelectAll_Click(object sender, RoutedEventArgs e) { foreach (var d in _vm.Deliveries) d.Selected = true; }
    private void SelectNone_Click(object sender, RoutedEventArgs e) { foreach (var d in _vm.Deliveries) d.Selected = false; }
    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_demo) { MessageBox.Show("This is a labelled design preview; no files are rendered. Restart without --demo to use Live."); return; }
        if (_session == null) return;
        var jobs = _vm.Deliveries.Where(d => d.Selected).Select(d => d.Id).ToArray();
        if (jobs.Length == 0) { MessageBox.Show("Choose at least one delivery variant."); return; }
        var profile = Storage.Read<ExportProfile>("export-profile.json");
        if (profile == null) { MessageBox.Show("Calibrate Live's export dialog first."); return; }
        if (MessageBox.Show(this,
            "glucose exports from the current open Set without saving it or creating a copy. Temporary track and device changes are restored after export.\n\nClose Live's Export dialog before continuing. Keep the desktop unlocked and let glucose control Live. All passes reuse the export range and sample rate configured in Live.\n\nManually set WAV/stereo, PCM On, No Dither. Turn Normalize, Mono, Loop, MP3 and Include Return and Main Effects Off. glucose does not verify these switches.",
            "Start batch export", MessageBoxButton.OKCancel, MessageBoxImage.Information) != MessageBoxResult.OK) return;
        _vm.Busy = true; _vm.ExportRunning = true; _cancellation = new();
        SaveRoles();
        foreach (var d in _vm.Deliveries) d.State = d.Selected ? "Queued" : "Skipped";
        string? finished = null;
        string? exportError = null;
        try
        {
            var queue = new BounceQueue(_bridge, profile,
                message => Dispatcher.Invoke(() => _vm.AddLog(message)),
                (id, state) => Dispatcher.Invoke(() => _vm.Deliveries.Single(d => d.Id == id).State = state));
            var source = _session;
            var roles = Roles(); var bits = _vm.Bits; var output = _vm.Output; var token = _cancellation.Token;
            // UI Automation can block while another application's provider is
            // busy. Keep it off WPF's dispatcher so Cancel remains responsive.
            finished = await Task.Run(() => queue.Run(source, roles, jobs, bits, output, token));
        }
        catch (OperationCanceledException) { _vm.AddLog("Cancelled. Completed exports have been kept; see activity for restoration status."); }
        catch (Exception ex) { exportError = ex.Message; ShowError(ex); }
        finally
        {
            _cancellation.Dispose(); _cancellation = null; _vm.ExportRunning = false;
            foreach (var row in _vm.Deliveries)
                if (row.State is "Rendering" or "Preparing") row.State = "Review";
                else if (row.State == "Queued") row.State = "Not run";
            var retained = _vm.Tracks.ToDictionary(t => t.Info.Key, t => new SavedRole(t.Name, t.Role));
            try
            {
                await Connect();
                foreach (var row in _vm.Tracks)
                    if (retained.TryGetValue(row.Info.Key, out var saved) && saved.Name == row.Name) row.Role = saved.Role;
                SaveRoles();
            }
            catch { _vm.Connected = false; }
            finally { _vm.Busy = false; }
            if (exportError != null) _vm.AddLog("Export stopped: " + exportError);
        }
        if (finished != null)
        {
            _vm.AddLog("Export complete: " + finished);
            Process.Start(new ProcessStartInfo("explorer.exe", finished) { UseShellExecute = true });
        }
    }
    private void Cancel_Click(object sender, RoutedEventArgs e)
    { _cancellation?.Cancel(); _vm.AddLog("Cancellation requested. Waiting for Live to stop before restoring…"); }
    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (_demo) return;
        try
        {
            _vm.Busy = true;
            var scan = await _bridge.Scan();
            if (scan.Transaction == null) { _vm.AddLog("No pending bridge changes in this Live Set."); return; }
            await _bridge.Call("restore", new { transaction = scan.Transaction, final = true }, timeoutSeconds: 30);
            _vm.AddLog("Current Set restored.");
            await Connect();
        }
        catch (Exception ex) { ShowError(ex); }
        finally { _vm.Busy = false; }
    }
    private void Activity_Click(object sender, RoutedEventArgs e)
    {
        var text = string.Join(Environment.NewLine + Environment.NewLine, _vm.Log);
        var box = new TextBox { Text = text.Length == 0 ? "No activity yet." : text, IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new(20) };
        new Window { Owner = this, Title = "glucose · Activity", Width = 760, Height = 500, Content = box, WindowStartupLocation = WindowStartupLocation.CenterOwner }.Show();
    }
    private void ShowError(Exception ex)
    { _vm.AddLog(ex.Message); MessageBox.Show(this, ex.Message, "glucose needs attention", MessageBoxButton.OK, MessageBoxImage.Warning); }
    private void LoadDemo()
    {
        _vm.Project = "Night Drive · Demo session"; _vm.Connection = "Demo · No Live connection"; _vm.Connected = true;
        _vm.Status = "Design preview with synthetic tracks. No connection to Ableton and no audio rendering.";
        (string Name, string Role, bool Group, int Depth, int Color)[] tracks =
        [
            ("Drums", "Instrument", true, 0, 0xC3D4A2), ("Kick", "Inherit", false, 1, 0xC3D4A2),
            ("Snare", "Inherit", false, 1, 0xC3D4A2), ("Percussion", "Inherit", false, 1, 0xC3D4A2),
            ("Bass", "Instrument", false, 0, 0xD4B78A), ("Keys & textures", "Instrument", true, 0, 0x93B9C3),
            ("Lead vocals", "Lead", true, 0, 0xDF8C75), ("Lead · verse", "Inherit", false, 1, 0xDF8C75),
            ("Lead · chorus", "Inherit", false, 1, 0xDF8C75), ("Backing vocals", "Backing", true, 0, 0x8FB5A1),
        ];
        for (int i = 0; i < tracks.Length; i++)
        {
            var t = tracks[i]; _vm.Tracks.Add(new(new TrackInfo { Key = "t:" + i, Name = t.Name, Group = t.Group, Color = t.Color, Volume = "−3.7 dB" }, i + 1, t.Depth, t.Role));
        }
    }
}

