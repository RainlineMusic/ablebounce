using System.IO;
using System.Windows.Automation;

namespace AbleBounce;

// Windows common file dialogs: use their control IDs, not translated labels.
// Filename IDs are cmb13/edt1; Save/Open is IDOK and file type is cmb1.
public static class WindowsSaveDialog
{
    private static AutomationElement? ById(AutomationElement parent, string id, ControlType? type = null) =>
        parent.FindFirst(TreeScope.Descendants, type == null
            ? new PropertyCondition(AutomationElement.AutomationIdProperty, id)
            : new AndCondition(new PropertyCondition(AutomationElement.AutomationIdProperty, id),
                new PropertyCondition(AutomationElement.ControlTypeProperty, type)));

    private static AutomationElement? Editable(AutomationElement? host)
    {
        if (host == null) return null;
        if (host.Current.ControlType == ControlType.Edit) return host;
        var edit = host.FindFirst(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));
        return edit ?? (host.TryGetCurrentPattern(ValuePattern.Pattern, out _) ? host : null);
    }

    private static AutomationElement? Filename(AutomationElement dialog)
    {
        var modern = Editable(ById(dialog, "FileNameControlHost"));
        if (modern != null) return modern;
        // Numeric edit IDs occur in other dialogs too. Require the file-type
        // selector before accepting a legacy filename combo/edit.
        if (ById(dialog, "FileTypeControlHost") != null || ById(dialog, "1136", ControlType.ComboBox) != null)
            return Editable(ById(dialog, "1148", ControlType.ComboBox))
                ?? ById(dialog, "1152", ControlType.Edit)
                ?? ById(dialog, "1001", ControlType.Edit);
        return null;
    }

    private static bool IsFileDialog(AutomationElement window, int process) =>
        window.Current.ProcessId == process && window.Current.ClassName == "#32770"
        && window.Current.IsEnabled && !window.Current.IsOffscreen
        && ById(window, "1", ControlType.Button) != null
        && Filename(window) != null;

    public static AutomationElement? Find(int process)
    {
        var windows = AutomationElement.RootElement.FindAll(TreeScope.Children,
            new PropertyCondition(AutomationElement.ProcessIdProperty, process));
        foreach (AutomationElement window in windows)
        {
            try { if (IsFileDialog(window, process)) return window; }
            catch (ElementNotAvailableException) { } // dialog closed during enumeration
        }
        return null;
    }

    public static async Task Submit(AutomationElement dialog, int process, string path, CancellationToken cancellation = default)
    {
        if (!Path.IsPathFullyQualified(path)) throw new IOException("A full Save path is required.");
        if (File.Exists(path)) throw new IOException("Refusing to overwrite an existing file: " + path);
        if (!IsFileDialog(dialog, process)) throw new IOException("The verified Windows file dialog is no longer available.");
        var field = Filename(dialog)!;
        if (field.Current.ProcessId != process || !field.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern))
            throw new IOException("Windows did not expose an editable filename field. No filename was entered.");
        var value = (ValuePattern)pattern;
        if (value.Current.IsReadOnly) throw new IOException("The Save filename field is read-only.");
        // Some common-dialog providers report SetValue successfully while
        // leaving the shell's actual filename unchanged. Real edit events are
        // required; send them only to the verified focused file-name field.
        field.SetFocus();
        await Task.Delay(80, cancellation);
        Native.RequireForeground(process);
        if (!field.Current.HasKeyboardFocus) throw new IOException("The filename field did not receive keyboard focus.");
        Native.Chord(Native.Ctrl, (ushort)'A');
        Native.Text(path);
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (!string.Equals(value.Current.Value, path, StringComparison.OrdinalIgnoreCase) && DateTime.UtcNow < deadline)
        {
            cancellation.ThrowIfCancellationRequested();
            Native.RequireForeground(process);
            await Task.Delay(50, cancellation);
        }
        if (!string.Equals(value.Current.Value, path, StringComparison.OrdinalIgnoreCase))
            throw new IOException("The Save filename readback did not match.");
        Native.RequireForeground(process);
        var save = ById(dialog, "1", ControlType.Button);
        if (save == null || !save.Current.IsEnabled || !save.TryGetCurrentPattern(InvokePattern.Pattern, out var invoke))
            throw new IOException("The Windows Save button is unavailable.");
        save.SetFocus(); // Commit the edit before invoking the default action.
        ((InvokePattern)invoke).Invoke();
    }
}
