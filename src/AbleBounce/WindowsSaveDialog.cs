using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace AbleBounce;

// Deliberately avoid UI Automation: shell/OneDrive providers can block its
// descendant traversal indefinitely. Only verified Win32 common dialogs accept input.
public static class WindowsSaveDialog
{
    private delegate bool Callback(nint window, nint parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(Callback callback, nint parameter);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(nint parent, Callback callback, nint parameter);
    [DllImport("user32.dll")] private static extern int GetDlgCtrlID(nint window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint window, StringBuilder name, int count);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] private static extern bool IsWindowEnabled(nint window);
    [DllImport("user32.dll")] private static extern bool IsChild(nint parent, nint child);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint SendMessageTimeout(nint window, uint message, nint wParam, StringBuilder buffer, uint flags, uint timeout, out nint result);
    [StructLayout(LayoutKind.Sequential)] private struct GuiInfo
    {
        public uint Size, Flags;
        public nint Active, Focus, Capture, MenuOwner, MoveSize, Caret;
        public Native.Rect CaretRect;
    }
    [DllImport("user32.dll")] private static extern bool GetGUIThreadInfo(uint thread, ref GuiInfo info);
    private static string Class(nint window)
    { var name = new StringBuilder(128); GetClassName(window, name, name.Capacity); return name.ToString(); }
    private static nint Child(nint parent, int id, string className)
    {
        nint found = 0;
        EnumChildWindows(parent, (window, _) =>
        {
            if (GetDlgCtrlID(window) == id && Class(window) == className && IsWindowVisible(window))
            { found = window; return false; }
            return true;
        }, 0);
        return found;
    }
    private static nint Filename(nint dialog)
    {
        var combo = Child(dialog, 1148, "ComboBox");
        if (combo != 0)
        {
            nint edit = 0;
            EnumChildWindows(combo, (window, _) => { if (Class(window) == "Edit") { edit = window; return false; } return true; }, 0);
            if (edit != 0) return edit;
        }
        // edt1 is the documented legacy filename control. Require the file type
        // combo before accepting a numeric edit ID in any dialog.
        if (Child(dialog, 1136, "ComboBox") != 0) return Child(dialog, 1152, "Edit");
        return 0;
    }
    private static bool Verified(nint dialog, int process)
    {
        Native.GetWindowThreadProcessId(dialog, out var owner);
        return dialog != 0 && owner == process && Class(dialog) == "#32770"
            && IsWindowVisible(dialog) && IsWindowEnabled(dialog)
            && Child(dialog, 1, "Button") != 0 && Filename(dialog) != 0;
    }
    public static nint Find(int process)
    {
        nint found = 0; int count = 0;
        EnumWindows((window, _) => { if (Verified(window, process)) { found = window; count++; } return true; }, 0);
        return count == 1 ? found : 0;
    }
    private static string Read(nint edit)
    {
        var buffer = new StringBuilder(32768);
        if (SendMessageTimeout(edit, 0x000D, buffer.Capacity, buffer, 2, 500, out _) == 0)
            throw new IOException("The Windows filename field did not respond within 500 ms.");
        return buffer.ToString();
    }
    public static async Task Submit(nint dialog, int process, string path, CancellationToken cancellation = default)
    {
        if (!Path.IsPathFullyQualified(path)) throw new IOException("A full Save path is required.");
        if (File.Exists(path)) throw new IOException("Refusing to overwrite an existing file: " + path);
        if (!Verified(dialog, process)) throw new IOException("The verified Windows Save dialog is unavailable.");
        var field = Filename(dialog);
        Native.SetForegroundWindow(dialog);
        Native.RequireForeground(process);
        if (!Native.GetWindowRect(field, out var rect)) throw new IOException("Cannot locate the filename field.");
        Native.Click(rect.Left + rect.Width / 2, rect.Top + rect.Height / 2);
        await Task.Delay(120, cancellation);
        var thread = Native.GetWindowThreadProcessId(dialog, out _);
        var info = new GuiInfo { Size = (uint)Marshal.SizeOf<GuiInfo>() };
        if (!GetGUIThreadInfo(thread, ref info) || (info.Focus != field && !IsChild(field, info.Focus)))
            throw new IOException("The verified filename field did not receive focus. No filename was entered.");
        Native.RequireForeground(process);
        Native.Chord(Native.Ctrl, (ushort)'A'); Native.Text(path);
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (!string.Equals(Read(field), path, StringComparison.OrdinalIgnoreCase))
        {
            cancellation.ThrowIfCancellationRequested(); Native.RequireForeground(process);
            if (DateTime.UtcNow >= deadline) throw new IOException("The Save filename readback did not match.");
            await Task.Delay(60, cancellation);
        }
        cancellation.ThrowIfCancellationRequested();
        if (File.Exists(path)) throw new IOException("Refusing to overwrite an existing file: " + path);
        Native.RequireForeground(process);
        // Enter commits the real edit and invokes Save asynchronously. Never
        // synchronously invoke a shell button while Live may be rendering.
        Native.Key(Native.Enter);
    }
}
