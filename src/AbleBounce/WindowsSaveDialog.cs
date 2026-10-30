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
        if (host.TryGetCurrentPattern(ValuePattern.Pattern, out _)) return host;
        return host.FindFirst(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));
    }

    private static AutomationElement? Filename(AutomationElement dialog)
    {
        var field = Editable(ById(dialog, "FileNameControlHost"))
            ?? Editable(ById(dialog, "1148", ControlType.ComboBox))
            ?? ById(dialog, "1152", ControlType.Edit);
        if (field != null) return field;
        // 1001 alone is generic: require a file-type selector as well.
        if (ById(dialog, "FileTypeControlHost") != null || ById(dialog, "1136", ControlType.ComboBox) != null)
            return ById(dialog, "1001", ControlType.Edit);
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

    public static void Submit(AutomationElement dialog, int process, string path)
    {
        if (!Path.IsPathFullyQualified(path)) throw new IOException("A full Save path is required.");
        if (File.Exists(path)) throw new IOException("Refusing to overwrite an existing file: " + path);
        if (!IsFileDialog(dialog, process)) throw new IOException("The verified Windows file dialog is no longer available.");
        var field = Filename(dialog)!;
        if (field.Current.ProcessId != process || !field.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern))
            throw new IOException("Windows did not expose an editable filename field. No filename was entered.");
        var value = (ValuePattern)pattern;
        if (value.Current.IsReadOnly) throw new IOException("The Save filename field is read-only.");
        value.SetValue(path);
        if (!string.Equals(value.Current.Value, path, StringComparison.OrdinalIgnoreCase))
            throw new IOException("The Save filename readback did not match.");
        var save = ById(dialog, "1", ControlType.Button);
        if (save == null || !save.Current.IsEnabled || !save.TryGetCurrentPattern(InvokePattern.Pattern, out var invoke))
            throw new IOException("The Windows Save button is unavailable.");
        ((InvokePattern)invoke).Invoke();
    }
}
