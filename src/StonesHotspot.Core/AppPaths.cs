namespace StonesHotspot.Core;

/// <summary>
/// Locations of runtime data. Default root is <c>%ProgramData%\StonesHotspot</c>; it can be overridden
/// (configuration key <c>StonesHotspot:DataDirectory</c>) for development and tests.
/// </summary>
public sealed class AppPaths
{
    public const string ProductFolderName = "StonesHotspot";

    public AppPaths(string? dataDirectory = null)
    {
        Root = string.IsNullOrWhiteSpace(dataDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), ProductFolderName)
            : Path.GetFullPath(dataDirectory);
    }

    public string Root { get; }
    public string Logs => Path.Combine(Root, "logs");
    public string Backups => Path.Combine(Root, "backups");

    /// <summary>Logo and other portal branding files.</summary>
    public string Branding => Path.Combine(Root, "branding");
    public string DatabaseFile => Path.Combine(Root, "stoneshotspot.db");

    /// <summary>File holding the random first admin password (deleted once the owner chooses one).</summary>
    public const string InitialAdminPasswordFileName = "initial-admin-password.txt";

    /// <summary>Creates the data folders if they do not exist yet.</summary>
    public AppPaths EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Logs);
        Directory.CreateDirectory(Backups);
        Directory.CreateDirectory(Branding);
        return this;
    }
}
