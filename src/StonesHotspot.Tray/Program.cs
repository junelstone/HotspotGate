using System.Globalization;
using StonesHotspot.Core.Localization;

namespace StonesHotspot.Tray;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // "--dashboard": the desktop shortcut. Opens the dashboard, and starts the tray icon if it is not running yet.
        var openDashboard = args.Contains("--dashboard", StringComparer.OrdinalIgnoreCase);

        // Only one tray icon per user session.
        using var mutex = new Mutex(initiallyOwned: true, @"Local\StonesHotspot.Tray", out var isFirstInstance);
        if (openDashboard)
            TrayContext.OpenDashboard();
        if (!isFirstInstance)
            return;

        var culture = Languages.Resolve(CultureInfo.CurrentUICulture.Name);
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayContext());
    }
}
