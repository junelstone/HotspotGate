using System.Globalization;
using System.Runtime.CompilerServices;

namespace StonesHotspot.Tests.Infrastructure;

/// <summary>
/// Texts the tests compare are the neutral (French) resources: without this, a PC whose Windows is in English
/// would give English texts wherever no request sets the language (SMS messages, report titles).
/// </summary>
internal static class TestCulture
{
    [ModuleInitializer]
    internal static void UseNeutralLanguage()
    {
        var french = CultureInfo.GetCultureInfo("fr");
        CultureInfo.DefaultThreadCurrentUICulture = french;
        CultureInfo.CurrentUICulture = french;
    }
}
