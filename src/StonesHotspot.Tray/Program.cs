using System.Globalization;
using StonesHotspot.Core.Localization;

namespace StonesHotspot.Tray;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // Only one tray icon per user session.
        using var mutex = new Mutex(initiallyOwned: true, @"Local\StonesHotspot.Tray", out var isFirstInstance);
        if (!isFirstInstance)
            return;

        var culture = Languages.Resolve(CultureInfo.CurrentUICulture.Name);
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayContext());
    }
}
