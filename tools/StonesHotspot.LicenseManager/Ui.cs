using System.Globalization;

namespace StonesHotspot.LicenseManager;

/// <summary>Small helpers to build the forms in code, in French or English (Windows display language).</summary>
internal static class Ui
{
    public static bool French => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName != "en";

    public static string T(string fr, string en) => French ? fr : en;

    public static Label Label(string text, bool bold = false) => new()
    {
        Text = text, AutoSize = true, MaximumSize = new Size(640, 0), Margin = new Padding(0, 8, 0, 2),
        Font = bold ? new Font(SystemFonts.MessageBoxFont!, FontStyle.Bold) : SystemFonts.MessageBoxFont,
    };

    public static TextBox Box(bool password = false, int width = 360) => new()
    {
        Width = width, UseSystemPasswordChar = password, Margin = new Padding(0, 0, 0, 4),
    };

    public static Button Button(string text, EventHandler onClick)
    {
        var button = new Button { Text = text, AutoSize = true, Padding = new Padding(8, 2, 8, 2), Margin = new Padding(0, 8, 8, 0) };
        button.Click += onClick;
        return button;
    }

    /// <summary>A vertical layout that grows with its content.</summary>
    public static FlowLayoutPanel Column() => new()
    {
        FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
        Padding = new Padding(16), Dock = DockStyle.Fill, AutoScroll = true,
    };

    public static FlowLayoutPanel Row(params Control[] controls)
    {
        var row = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, WrapContents = true, Margin = new Padding(0) };
        row.Controls.AddRange(controls);
        return row;
    }

    public static Form Dialog(string title, Control content, int width = 560)
    {
        var form = new Form
        {
            Text = title, StartPosition = FormStartPosition.CenterParent, Width = width, AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false,
        };
        form.Controls.Add(content);
        return form;
    }

    public static void Error(IWin32Window? owner, string message) =>
        MessageBox.Show(owner, message, "Stone's Hotspot — LicenseManager", MessageBoxButtons.OK, MessageBoxIcon.Error);

    public static void Info(IWin32Window? owner, string message) =>
        MessageBox.Show(owner, message, "Stone's Hotspot — LicenseManager", MessageBoxButtons.OK, MessageBoxIcon.Information);

    public static bool Confirm(IWin32Window? owner, string message) =>
        MessageBox.Show(owner, message, "Stone's Hotspot — LicenseManager", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes;
}
