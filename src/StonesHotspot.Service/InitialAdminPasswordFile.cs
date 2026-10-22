using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using StonesHotspot.Core;

namespace StonesHotspot.Service;

/// <summary>
/// Hands the random first-run admin password to the owner through a file in the data folder that only
/// Administrators and SYSTEM can read (on Windows). It should be deleted once the password is changed.
/// </summary>
public static class InitialAdminPasswordFile
{
    public const string FileName = "initial-admin-password.txt";

    public static string Write(AppPaths paths, string username, string password)
    {
        var path = Path.Combine(paths.Root, FileName);
        var text = $"""
            Stone's Hotspot — premier mot de passe administrateur / first administrator password

            Utilisateur / Username : {username}
            Mot de passe / Password : {password}

            Vous devrez le changer à la première connexion. Supprimez ensuite ce fichier.
            You must change it at first login. Then delete this file.
            """;

        if (OperatingSystem.IsWindows())
            WriteRestricted(path, text);
        else
            File.WriteAllText(path, text);
        return path;
    }

    [SupportedOSPlatform("windows")]
    private static void WriteRestricted(string path, string text)
    {
        var security = new FileSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        foreach (var sid in new[] { WellKnownSidType.BuiltinAdministratorsSid, WellKnownSidType.LocalSystemSid })
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid, null), FileSystemRights.FullControl, AccessControlType.Allow));

        File.Delete(path);
        using var stream = new FileInfo(path).Create(FileMode.CreateNew, FileSystemRights.Write, FileShare.None, 4096, FileOptions.None, security);
        using var writer = new StreamWriter(stream);
        writer.Write(text);
    }
}
