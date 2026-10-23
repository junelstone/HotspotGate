using StonesHotspot.LicenseIssuer;

namespace StonesHotspot.LicenseManager;

/// <summary>
/// OWNER-ONLY licence tool of Stone's IT Consulting: creates, renews and revokes licences. Holds the private key
/// (encrypted with the owner's password). Never installed at customers', never in the installer.
/// </summary>
internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        var keys = File.Exists(ManagerFiles.KeyFile) ? SignIn.Show() : FirstRun.Show();
        if (keys is null)
            return;
        using var book = new LicenceBook(ManagerFiles.Database, keys);
        Application.Run(new MainForm(book, keys));
    }
}
