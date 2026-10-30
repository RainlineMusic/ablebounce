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
    public int CoordinateVersion { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public uint Dpi { get; set; }
    public Dictionary<string, Anchor> Anchors { get; set; } = [];
    public DateTime Captured { get; set; }
    public static (string Key, string Label)[] Fields =>
    [
        ("source", "Rendered Track chooser"),
        ("export", "Export button"),
    ];
    public static string[] RequiredReferences => ["source", "export", "source_main"];
    public void Validate(SessionInfo session)
    {
        if (Version != session.Version) throw new InvalidOperationException("Live version changed. Calibrate its export dialog again.");
        if (CoordinateVersion != 3 || Width <= 0 || Height <= 0)
            throw new InvalidOperationException("Please run the new two-point calibration once.");
        foreach (var key in RequiredReferences)
            if (!Anchors.TryGetValue(key, out var a) || a.Reference.Length != 17 * 7 * 3
                || a.X < 0 || a.X >= Width || a.Y < 0 || a.Y >= Height)
                throw new InvalidOperationException("Export calibration is incomplete.");

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
        Title = "glucose · Export calibration"; Width = 440; Height = 500;
        Background = SystemColors.ControlBrush;
        ResizeMode = ResizeMode.NoResize; Topmost = true;
        _live = Native.LiveProcess();
        if (!Native.TryExportWindow(_live.Id, out var rect))
            throw new InvalidOperationException("Open Live’s Export Audio/Video window before calibration.");
        _profile = new() { Version = version, CoordinateVersion = 3, Width = rect.Width, Height = rect.Height,
            Dpi = Native.GetDpiForWindow(_live.MainWindowHandle), Captured = DateTime.Now };
        var panel = new StackPanel { Margin = new(24) };
        panel.Children.Add(new TextBlock { Text = "Teach glucose your export dialog", TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = "Move this window away from Live’s Export dialog. Point at the two requested controls (Rendered Track and Export) and press F8. glucose will capture the chooser values automatically; do not click or type during that final step.", Margin = new(0, 12, 0, 0), TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(_step); panel.Children.Add(_detail);
        _back.Click += (_, _) => { if (!_capturing && _index > 0) { _index--; _profile.Anchors.Remove(ExportProfile.Fields[_index].Key); UpdateStep(); } };
        panel.Children.Add(_back);
        panel.Children.Add(new TextBlock { Text = "You can move Live and its Export window afterwards. Recalibrate if the Export window size, theme, display scale or Live version changes. Press Esc to cancel.", TextWrapping = TextWrapping.Wrap, Margin = new(0, 18, 0, 0) });
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
        _detail.Text = "Hover over the control itself in Live and press F8. No value selection is needed.";
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
            if (!Native.TryExportWindow(_live.Id, out var rect))
                throw new InvalidOperationException("Live’s Export Audio/Video window was not found.");
            if (rect.Width != _profile.Width || rect.Height != _profile.Height)
                throw new InvalidOperationException("Export window size changed. Restart calibration.");
            if (point.X < rect.Left || point.X >= rect.Right || point.Y < rect.Top || point.Y >= rect.Bottom)
                throw new InvalidOperationException("Point inside Live's Export dialog, then press F8.");
            Native.SetCursorPos(rect.Left + 20, rect.Top + 12);
            await Task.Delay(250);
            var key = ExportProfile.Fields[_index].Key;
            _profile.Anchors[key] = new() { X = point.X - rect.Left, Y = point.Y - rect.Top, Reference = Native.Patch(point.X, point.Y) };
            _index++;
            if (_index == ExportProfile.Fields.Length)
            {
                _step.Text = "Capturing chooser values automatically…";
                _back.IsEnabled = false;
                await CaptureValues();
                Storage.Write("export-profile.json", _profile); Result = _profile; DialogResult = true;
            }
            else UpdateStep();
        }
        catch (Exception ex)
        {
            if (_index >= ExportProfile.Fields.Length) _index = 0;
            UpdateStep(); _detail.Text = ex.Message;
        }
        finally { _capturing = false; }
    }
    public async Task<bool> TryAutomatic()
    {
        var rect = ExportRect();
        double ratio = (double)rect.Width / rect.Height;
        if (ratio < .43 || ratio > .52) return false; // known Live 12.4 English export layout only
        _profile.Anchors["source"] = new() { X = (int)(rect.Width * .61), Y = (int)(rect.Height * .1164) };
        var x = (int)(rect.Width * .377); var y = (int)(rect.Height * .953);
        _profile.Anchors["export"] = new() { X = x, Y = y, Reference = Native.Patch(rect.Left + x, rect.Top + y) };
        try
        {
            await CaptureValues();
            // Verify the estimated Export button by opening the filename prompt.
            // Do not enter a filename or start a render during calibration.
            rect = ExportRect(); Native.Click(rect.Left + x, rect.Top + y);
            nint save = 0;
            for (int i = 0; i < 30 && save == 0; i++) { await Task.Delay(100); save = WindowsSaveDialog.Find(_live.Id); }
            if (save == 0) return false;
            Native.RequireForeground(_live.Id); Native.Key(Native.Esc); await Task.Delay(300);
            if (WindowsSaveDialog.Find(_live.Id) != 0) return false;
            Storage.Write("export-profile.json", _profile); Result = _profile; return true;
        }
        catch { return false; }
    }
    private Native.Rect ExportRect()
    {
        Native.RequireForeground(_live.Id);
        if (!Native.TryExportWindow(_live.Id, out var rect) || rect.Width != _profile.Width || rect.Height != _profile.Height
            || Native.GetDpiForWindow(_live.MainWindowHandle) != _profile.Dpi)
            throw new InvalidOperationException("Export window changed or closed. Restart calibration.");
        return rect;
    }
    private async Task CaptureChoice(string control, int index, string reference)
    {
        var rect = ExportRect(); var anchor = _profile.Anchors[control];
        Native.Click(rect.Left + anchor.X, rect.Top + anchor.Y);
        await Task.Delay(180);
        ExportRect(); Native.Key(Native.Home);
        for (int i = 0; i < index; i++) { ExportRect(); Native.Key(Native.Down); }
        ExportRect(); Native.Key(Native.Enter);
        Native.SetCursorPos(rect.Left + 20, rect.Top + 12);
        await Task.Delay(250);
        rect = ExportRect();
        _profile.Anchors[reference] = new() { X = anchor.X, Y = anchor.Y,
            Reference = Native.Patch(rect.Left + anchor.X, rect.Top + anchor.Y) };
    }
    private async Task CaptureValues()
    {
        // Capture both source states with one source point.
        // Leave Main selected and keep bit depth untouched.
        await Native.EnsureEnglish(_live.Id, CancellationToken.None);
        await CaptureChoice("source", 1, "source");
        await CaptureChoice("source", 0, "source_main");
        foreach (var pair in new[] { ("source", "source_main") })
        {
            double difference = _profile.Anchors[pair.Item1].Reference.Zip(_profile.Anchors[pair.Item2].Reference,
                (a, b) => Math.Abs(a - b)).Average();
            if (difference <= 8)
                throw new InvalidOperationException("Chooser values could not be distinguished. Point at the displayed text, not the arrow or empty area, and retry.");
        }
    }

}

