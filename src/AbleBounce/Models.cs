using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;

namespace AbleBounce;

public class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Changed(name);
        return true;
    }
    protected void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}

public sealed class TrackInfo
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Parent { get; set; }
    public bool Group { get; set; }
    public bool Return { get; set; }
    public bool Master { get; set; }
    public bool Audio { get; set; }
    public bool Frozen { get; set; }
    public bool Mute { get; set; }
    public bool Solo { get; set; }
    public int Color { get; set; }
    public string SuggestedRole { get; set; } = "Unassigned";
    public string Volume { get; set; } = "";
    public int VolumeAutomation { get; set; }
    public string Output { get; set; } = "";
    public string[] Devices { get; set; } = [];
}

public sealed class SessionInfo
{
    public int Protocol { get; set; }
    public string Version { get; set; } = "";
    public string Path { get; set; } = "";
    public string Name { get; set; } = "";
    public List<TrackInfo> Tracks { get; set; } = [];
    public double LoopStart { get; set; }
    public double LoopLength { get; set; }
    public string? Transaction { get; set; }
    public int PendingChanges { get; set; }
}

public sealed class TrackRow : Observable
{
    public static string[] RoleOptions { get; } = ["Unassigned", "Inherit", "Lead", "Backing", "Instrument"];
    public TrackInfo Info { get; }
    private bool _dry;
    public bool Dry { get => _dry; set => Set(ref _dry, value); }
    public bool CanDry => Info.Audio && !Info.Frozen;
    private string _role;
    public string Role { get => _role; set => Set(ref _role, value); }
    public string Name => Info.Name;
    public string Detail => Info.Group ? "GROUP" : Info.Frozen ? "Frozen" : Info.Mute ? "Muted" : Info.Volume;
    public string Number { get; }
    public Thickness Indent { get; }
    public TrackRow(TrackInfo info, int number, int depth, string? role = null)
    {
        Info = info; _role = role ?? info.SuggestedRole;
        Number = number.ToString("D2"); Indent = new Thickness(depth * 13, 0, 0, 0);
    }
}

public sealed class DeliveryRow : Observable
{
    private bool _selected = true;
    private string _state = "Ready";
    public int Id { get; }
    public string Title { get; }
    public string Description { get; }
    public string Vocals { get; }
    public string Master { get; }
    public bool Selected { get => _selected; set => Set(ref _selected, value); }
    public string State { get => _state; set => Set(ref _state, value); }
    public DeliveryRow(int id, string title, string description, string vocals, string master)
    { Id = id; Title = title; Description = description; Vocals = vocals; Master = master; }
    public static DeliveryRow[] Create() =>
    [
        new(1, "Full mix", "Your original mix, with its current master chain.", "All vocals", "Original"),
        new(2, "Instrumental", "Lead and backing vocals removed.", "No vocals", "Original"),
        new(3, "Instrumental · no master", "No vocals and no master devices.", "No vocals", "Bypassed"),
        new(4, "Full mix · no master", "The complete mix before master processing.", "All vocals", "Bypassed"),
        new(5, "Backing mix · no master", "Backing vocals stay; lead vocals are removed.", "Backing", "Bypassed"),
        new(6, "Backing mix", "Backing vocals with the original master chain.", "Backing", "Original"),
        new(7, "Full mix · lead −6 dB", "Temporary final Utility trim; fader automation stays.", "Lead −6 dB", "Original"),
        new(8, "Mixed multitrack", "Post-fader tracks, groups and returns in separate folders.", "All tracks", "Excluded"),
        new(9, "Selected tracks (DRY)", "Checked tracks without audio effects, sends, parent or master processing.", "Selected", "Bypassed"),
    ];
}

public sealed class ViewModel : Observable
{
    private string _project = "Connect your Live Set";
    private string _connection = "Not connected";
    private string _status = "Open Ableton Live, enable the AbleBounce control surface, then connect.";
    private bool _busy;
    private bool _connected;
    private bool _exportRunning;
    private string _output = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "glucose Exports");
    private string _trackTitle = "";
    public string TrackTitle { get => _trackTitle; set => Set(ref _trackTitle, value); }
    private string _bitDepth = "24-bit";
    private int _completed;
    public ObservableCollection<TrackRow> Tracks { get; } = [];
    public ObservableCollection<DeliveryRow> Deliveries { get; } = new(DeliveryRow.Create());
    public ObservableCollection<string> Log { get; } = [];
    public string Project { get => _project; set => Set(ref _project, value); }
    public string Connection { get => _connection; set => Set(ref _connection, value); }
    public string Status { get => _status; set => Set(ref _status, value); }
    public bool Busy { get => _busy; set { Set(ref _busy, value); Changed(nameof(Idle)); Changed(nameof(CanExport)); } }
    public bool Idle => !Busy;
    public bool ExportRunning { get => _exportRunning; set => Set(ref _exportRunning, value); }
    public bool Connected { get => _connected; set { Set(ref _connected, value); Changed(nameof(CanExport)); } }
    public bool CanExport => Connected && !Busy;
    public string Output { get => _output; set => Set(ref _output, value); }
    public string BitDepth { get => _bitDepth; set => Set(ref _bitDepth, value); }
    public string[] BitDepths { get; } = ["16-bit", "24-bit", "32-bit float"];
    public int Bits => BitDepth.StartsWith("16") ? 16 : BitDepth.StartsWith("24") ? 24 : 32;
    public int Completed { get => _completed; set => Set(ref _completed, value); }
    public string Summary => $"{Deliveries.Count(d => d.Selected)} delivery variants selected";
    public string TrackSummary => Tracks.Count == 0 ? "Waiting for Live" : $"{Tracks.Count(t => !t.Info.Group)} tracks · {Tracks.Count(t => t.Info.Group)} groups";
    public ViewModel()
    {
        foreach (var d in Deliveries) d.PropertyChanged += (_, _) => Changed(nameof(Summary));
        Tracks.CollectionChanged += (_, _) => Changed(nameof(TrackSummary));
    }
    public void AddLog(string message)
    {
        Status = message;
        Log.Insert(0, $"{DateTime.Now:HH:mm:ss}  {message}");
        while (Log.Count > 200) Log.RemoveAt(Log.Count - 1);
    }
}

public static class Storage
{
    public static readonly string Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AbleBounce");
    public static readonly JsonSerializerOptions Json = new()
    { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, PropertyNameCaseInsensitive = true, WriteIndented = true };
    public static T? Read<T>(string name)
    {
        var file = Path.Combine(Root, name);
        return File.Exists(file) ? JsonSerializer.Deserialize<T>(File.ReadAllText(file), Json) : default;
    }
    public static void Write<T>(string name, T value)
    {
        Directory.CreateDirectory(Root);
        var file = Path.Combine(Root, name);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file + ".tmp", JsonSerializer.Serialize(value, Json));
        File.Move(file + ".tmp", file, true);
    }
    public static string SafeName(string name)
    {
        var bad = Path.GetInvalidFileNameChars();
        var result = string.Concat(name.Select(c => bad.Contains(c) || char.IsControl(c) ? '_' : c)).Trim().TrimEnd('.');
        if (result.Length > 90) result = result[..90];
        return string.IsNullOrWhiteSpace(result) ? "Untitled" : result;
    }
}

