using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace AbleBounce;

public sealed class Anchor
{
    public int X { get; set; }
    public int Y { get; set; }
    public byte[] Reference { get; set; } = [];
}
public sealed class ExportProfile
{
    public string Version { get; set; } = "";
    public int Width { get; set; }
    public int Height { get; set; }
    public uint Dpi { get; set; }
    public Dictionary<string, Anchor> Anchors { get; set; } = [];
    public DateTime Captured { get; set; }
    public static (string Key, string Label)[] Fields =>
    [
        ("source", "Rendered Track: All Individual Tracks"),
        ("bit_depth", "Bit Depth chooser (currently 24)"),
        ("export", "Export button"),
        ("bit_16", "Select Bit Depth 16, then point at its value"),
        ("bit_32", "Select Bit Depth 32, then point at its value"),
        ("source_main", "Select Rendered Track Main, then point at its value"),
    ];
    public void Validate(SessionInfo session)
    {
        if (Version != session.Version) throw new InvalidOperationException("Live version changed. Calibrate its export dialog again.");
        foreach (var (key, _) in Fields)
            if (!Anchors.TryGetValue(key, out var a) || a.Reference.Length != 17 * 7 * 3)
                throw new InvalidOperationException("Export calibration is incomplete.");
        var p = Native.LiveProcess();
        Native.GetWindowRect(p.MainWindowHandle, out var rect);
        if (rect.Width != Width || rect.Height != Height || Native.GetDpiForWindow(p.MainWindowHandle) != Dpi)
            throw new InvalidOperationException("Live's window size or display scale changed. Restore its calibrated size or calibrate again.");
    }
}

public sealed class CalibrationWindow : Window
{
    private readonly ExportProfile _profile;
    private readonly System.Diagnostics.Process _live;
    private readonly TextBlock _step = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 16, 0, 12) };
    private readonly TextBlock _detail = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 16) };
    private readonly Button _back = new() { Content = "Undo last point", HorizontalAlignment = HorizontalAlignment.Left };
    private HwndSource? _source;
    private int _index;
    private bool _capturing;
    public ExportProfile? Result { get; private set; }
    public CalibrationWindow(string version)
    {
        Title = "AbleBounce · Export calibration"; Width = 440; Height = 440;
        Background = SystemColors.ControlBrush;
        ResizeMode = ResizeMode.NoResize; Topmost = true;
        _live = Native.LiveProcess();
        Native.GetWindowRect(_live.MainWindowHandle, out var rect);
        _profile = new() { Version = version, Width = rect.Width, Height = rect.Height,
            Dpi = Native.GetDpiForWindow(_live.MainWindowHandle), Captured = DateTime.Now };
        var panel = new StackPanel { Margin = new(24) };
        panel.Children.Add(new TextBlock { Text = "Teach AbleBounce your export dialog", TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = "Move this window away from Live's Export dialog. Point at each requested control and press F8. For the last three steps, select the requested value first and close the chooser before pressing F8.", Margin = new(0, 12, 0, 0), TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(_step); panel.Children.Add(_detail);
        _back.Click += (_, _) => { if (!_capturing && _index > 0) { _index--; _profile.Anchors.Remove(ExportProfile.Fields[_index].Key); UpdateStep(); } };
        panel.Children.Add(_back);
        panel.Children.Add(new TextBlock { Text = "Keep Live's window size, theme and display scale unchanged after calibration. Press Esc to cancel.", TextWrapping = TextWrapping.Wrap, Margin = new(0, 18, 0, 0) });
        Content = panel;
        SourceInitialized += (_, _) =>
        {
            _source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            _source.AddHook(Hook);
            if (!Native.RegisterHotKey(_source.Handle, 4816, 0x4000, 0x77))
            { MessageBox.Show("F8 is in use by another program. Close that shortcut tool and try again."); Close(); }
        };
        Closed += (_, _) => { if (_source != null) { Native.UnregisterHotKey(_source.Handle, 4816); _source.RemoveHook(Hook); } };
        PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) Close(); };
        UpdateStep();
    }
    private void UpdateStep()
    {
        _step.Text = $"{_index + 1} / {ExportProfile.Fields.Length}   {ExportProfile.Fields[_index].Label}";
        _detail.Text = _index >= 3
            ? "Change this chooser to the requested value in Live, close its menu, hover over the value and press F8."
            : "Hover over this control in Live and press F8. Capture the value/switch itself, not its label.";
        _back.IsEnabled = _index > 0;
    }
    private nint Hook(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == 0x0312 && wParam == 4816)
        { handled = true; _ = CapturePoint(); }
        return 0;
    }
    private async Task CapturePoint()
    {
        if (_capturing) return;
        _capturing = true;
        try
        {
            Native.RequireForeground(_live.Id);
            Native.GetCursorPos(out var point);
            Native.GetWindowRect(_live.MainWindowHandle, out var rect);
            if (point.X < rect.Left || point.X >= rect.Right || point.Y < rect.Top || point.Y >= rect.Bottom)
                throw new InvalidOperationException("Point inside Live's Export dialog, then press F8.");
            Native.SetCursorPos(rect.Left + 20, rect.Top + 12);
            await Task.Delay(250);
            var key = ExportProfile.Fields[_index].Key;
            _profile.Anchors[key] = new() { X = point.X - rect.Left, Y = point.Y - rect.Top, Reference = Native.Patch(point.X, point.Y) };
            _index++;
            if (_index == ExportProfile.Fields.Length)
            {
                Storage.Write("export-profile.json", _profile); Result = _profile; DialogResult = true;
            }
            else UpdateStep();
        }
        catch (Exception ex) { _detail.Text = ex.Message; }
        finally { _capturing = false; }
    }
}

