using System.Diagnostics;

namespace StonesHotspot.Tests;

/// <summary>
/// tools/Test-ReleaseContent.ps1, the build check that keeps the owner's tools and keys out of the customer installer.
/// Needs PowerShell 7 (pwsh); without it the test does nothing (the build itself always runs the check).
/// </summary>
public sealed class ReleaseContentTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "shs-release-" + Guid.NewGuid().ToString("N"));

    public ReleaseContentTests() => Directory.CreateDirectory(Path.Combine(_dir, "StonesHotspot.Service"));

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static string? Pwsh() =>
        new[] { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet", "tools", OperatingSystem.IsWindows() ? "pwsh.exe" : "pwsh") }
            .Concat((Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator).Select(p => Path.Combine(p, OperatingSystem.IsWindows() ? "pwsh.exe" : "pwsh")))
            .FirstOrDefault(File.Exists);

    private static string Script()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "StonesHotspot.sln")))
            dir = dir.Parent;
        return Path.Combine(dir!.FullName, "tools", "Test-ReleaseContent.ps1");
    }

    private (int Exit, string Output) Check(string pwsh, params string[] extra)
    {
        var start = new ProcessStartInfo(pwsh) { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "-NoProfile", "-File", Script(), "-Path", _dir }.Concat(extra))
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output);
    }

    private void Add(string relative, string content = "x") => File.WriteAllText(Path.Combine(_dir, relative), content);

    [Fact]
    public void Customer_files_pass_and_owner_files_or_keys_fail_the_build()
    {
        if (Pwsh() is not { } pwsh)
            return;

        Add(@"StonesHotspot.Service/StonesHotspot.Service.dll");
        Add(@"StonesHotspot.Service/System.Private.CoreLib.dll");   // part of every self-contained .NET app
        Add(@"StonesHotspot.Service/appsettings.json", "{}");
        Assert.Equal(0, Check(pwsh).Exit);

        Add(@"StonesHotspot.Service/StonesHotspot.Licensing.dll", "...# DEVELOPMENT licence public key...");
        var dev = Check(pwsh);
        Assert.Equal(1, dev.Exit);
        Assert.Contains("DEVELOPMENT", dev.Output);
        Assert.Equal(0, Check(pwsh, "-AllowDevelopmentKeys").Exit);   // explicit test build only

        Add("StonesHotspot.LicenseManager.exe");
        Add("owner.shkey");
        Add(@"StonesHotspot.Service/appsettings.Local.json", "{}");
        Add("notes.txt", "-----BEGIN PRIVATE KEY-----\nabc\n-----END PRIVATE KEY-----");
        var owner = Check(pwsh, "-AllowDevelopmentKeys");
        Assert.Equal(1, owner.Exit);
        foreach (var name in new[] { "LicenseManager", "owner.shkey", "appsettings.Local.json", "notes.txt" })
            Assert.Contains(name, owner.Output);
    }
}
