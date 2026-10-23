using System.Reflection;

namespace StonesHotspot.Licensing;

/// <param name="Directory">Where the activated licence (<c>licence.lic</c>) and the last server answer are kept.</param>
/// <param name="LicencePublicKey">Checks licence codes (the owner's licence key).</param>
/// <param name="ServerPublicKey">Checks the licence server's answers (the owner's server key).</param>
public sealed record LicensingOptions(string Directory, byte[] LicencePublicKey, byte[] ServerPublicKey)
{
    public const int TrialDays = 30;
    public const int GraceDays = 7;
    public static readonly int[] WarningDays = [30, 14, 7, 3, 1];

    /// <summary>Public keys compiled into this build (<c>Keys/*.txt</c>, base64).</summary>
    public static LicensingOptions WithEmbeddedKeys(string directory) =>
        new(directory, EmbeddedKey("licence-public.txt"), EmbeddedKey("server-public.txt"));

    /// <summary>True while the build still carries the development keys (a release must not).</summary>
    public static bool UsesDevelopmentKeys => EmbeddedText("licence-public.txt").Contains("DEVELOPMENT", StringComparison.Ordinal);

    private static byte[] EmbeddedKey(string name) =>
        Convert.FromBase64String(EmbeddedText(name).Split('\n').Select(l => l.Trim()).First(l => l.Length > 0 && !l.StartsWith('#')));

    private static string EmbeddedText(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
                           ?? throw new InvalidOperationException($"Embedded key {name} is missing.");
        return new StreamReader(stream).ReadToEnd();
    }
}
