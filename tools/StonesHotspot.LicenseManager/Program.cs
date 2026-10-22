namespace StonesHotspot.LicenseManager;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        using var form = new Form
        {
            Text = "Stone's Hotspot — LicenseManager",
            Width = 520,
            Height = 220,
            StartPosition = FormStartPosition.CenterScreen,
        };
        form.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
            Text = "Outil privé de Stone's IT Consulting. La création des licences arrive dans la phase Licences.\n\n"
                 + "Private tool of Stone's IT Consulting. Licence creation arrives in the licensing phase.",
        });
        Application.Run(form);
    }
}
