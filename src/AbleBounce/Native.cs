using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AbleBounce;

internal static class Native
{
    public const ushort Ctrl = 0x11, Shift = 0x10, Alt = 0x12, Enter = 0x0D, Esc = 0x1B, Home = 0x24, Down = 0x28;
    [StructLayout(LayoutKind.Sequential)] public struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct Rect
    { public int Left, Top, Right, Bottom; public int Width => Right - Left; public int Height => Bottom - Top; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseInput
    { public int X, Y; public uint Data, Flags, Time; public nint Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct KeyboardInput
    { public ushort Key, Scan; public uint Flags, Time; public nint Extra; }
    [StructLayout(LayoutKind.Explicit)] private struct InputUnion
    { [FieldOffset(0)] public MouseInput Mouse; [FieldOffset(0)] public KeyboardInput Keyboard; }
    [StructLayout(LayoutKind.Sequential)] private struct Input { public uint Type; public InputUnion Data; }
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(nint window, out Rect rect);
    [DllImport("user32.dll")] public static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(nint window, out uint process);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern bool IsIconic(nint window);
    [DllImport("user32.dll")] public static extern bool ShowWindow(nint window, int command);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(nint window);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(nint window, int id);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] input, int size);
    [DllImport("user32.dll")] private static extern nint GetDC(nint window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint window, nint dc);
    [DllImport("gdi32.dll")] private static extern uint GetPixel(nint dc, int x, int y);

    private delegate bool EnumWindowCallback(nint window, nint parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowCallback callback, nint parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint window, System.Text.StringBuilder text, int count);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    public static bool TryExportWindow(int process, out Rect rect)
    {
        nint found = 0;
        int count = 0;
        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out var owner);
            if (owner != process || !IsWindowVisible(window)) return true;
            var title = new System.Text.StringBuilder(256);
            GetWindowText(window, title, title.Capacity);
            if (title.ToString().Trim() == "Export Audio/Video") { found = window; count++; }
            return true;
        }, 0);
        rect = default;
        return count == 1 && GetWindowRect(found, out rect) && rect.Width > 100 && rect.Height > 100;
    }

    public static Process LiveProcess()
    {
        var processes = Process.GetProcesses().Where(p =>
        {
            try { return p.ProcessName.StartsWith("Ableton Live", StringComparison.OrdinalIgnoreCase) && p.MainWindowHandle != 0; }
            catch { return false; }
        }).ToArray();
        if (processes.Length != 1) throw new InvalidOperationException("Keep exactly one Ableton Live instance open.");
        return processes[0];
    }
    public static void RequireForeground(int processId)
    {
        var foreground = GetForegroundWindow();
        GetWindowThreadProcessId(foreground, out var actual);
        if (foreground == 0 || actual != processId)
            throw new InvalidOperationException("Ableton lost focus. Leave Live in front and the desktop unlocked, then retry.");
    }
    public static async Task Focus(Process live, CancellationToken ct)
    {
        if (IsIconic(live.MainWindowHandle)) ShowWindow(live.MainWindowHandle, 9);
        SetForegroundWindow(live.MainWindowHandle);
        await Task.Delay(350, ct);
        RequireForeground(live.Id);
        await EnsureEnglish(live.Id, ct);
    }
    [DllImport("user32.dll")] private static extern nint GetKeyboardLayout(uint thread);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint LoadKeyboardLayout(string name, uint flags);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool PostMessage(nint window, uint message, nint wParam, nint lParam);
    public static async Task EnsureEnglish(int process, CancellationToken cancellation)
    {
        RequireForeground(process);
        var window = GetForegroundWindow();
        var thread = GetWindowThreadProcessId(window, out _);
        bool English() => ((long)GetKeyboardLayout(thread) & 0x3ff) == 9;
        if (English()) return;
        var english = LoadKeyboardLayout("00000409", 0);
        if (english == 0 || !PostMessage(window, 0x0050, 0, english))
            throw new InvalidOperationException("Cannot select English input in Live. Select English in Windows and retry.");
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (!English())
        {
            cancellation.ThrowIfCancellationRequested(); RequireForeground(process);
            if (DateTime.UtcNow >= deadline)
                throw new InvalidOperationException("Live did not switch to English input. Select English in Windows and retry.");
            await Task.Delay(50, cancellation);
        }
    }
    private static void Send(params Input[] inputs)
    {
        if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>()) != inputs.Length)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows rejected input. Run Live and AbleBounce at the same privilege level.");
    }
    private static Input KeyInput(ushort key, bool up = false) => new()
    { Type = 1, Data = new InputUnion { Keyboard = new KeyboardInput { Key = key, Flags = up ? 2u : 0 } } };
    public static void Key(ushort key) => Send(KeyInput(key), KeyInput(key, true));
    public static void Chord(params ushort[] keys) => Send(keys.Select(k => KeyInput(k)).Concat(keys.Reverse().Select(k => KeyInput(k, true))).ToArray());
    public static void Text(string text)
    {
        var inputs = new List<Input>();
        foreach (char c in text)
        {
            inputs.Add(new() { Type = 1, Data = new() { Keyboard = new() { Scan = c, Flags = 4 } } });
            inputs.Add(new() { Type = 1, Data = new() { Keyboard = new() { Scan = c, Flags = 6 } } });
        }
        Send(inputs.ToArray());
    }
    public static void Click(int x, int y)
    {
        SetCursorPos(x, y);
        Send(new() { Type = 0, Data = new() { Mouse = new() { Flags = 2 } } },
             new() { Type = 0, Data = new() { Mouse = new() { Flags = 4 } } });
    }
    public static byte[] Patch(int x, int y)
    {
        var result = new List<byte>();
        var dc = GetDC(0);
        if (dc == 0) throw new InvalidOperationException("The desktop is unavailable.");
        try
        {
            for (int dy = -3; dy <= 3; dy++)
                for (int dx = -8; dx <= 8; dx++)
                {
                    var pixel = GetPixel(dc, x + dx, y + dy);
                    if (pixel == uint.MaxValue) throw new InvalidOperationException("Cannot read the Live export dialog.");
                    result.Add((byte)pixel); result.Add((byte)(pixel >> 8)); result.Add((byte)(pixel >> 16));
                }
        }
        finally { ReleaseDC(0, dc); }
        return result.ToArray();
    }
}

