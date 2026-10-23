using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StonesHotspot.Core;
using StonesHotspot.Core.Data;
using StonesHotspot.Core.Services;
using StonesHotspot.Core.Updates;
using StonesHotspot.LicenseIssuer;
using StonesHotspot.LicenseServer;
using StonesHotspot.Licensing.Online;
using StonesHotspot.Network.Firewall;
using StonesHotspot.Service;
using StonesHotspot.Tests.Infrastructure;

namespace StonesHotspot.Tests;

/// <summary>Phase 11 pieces that run without Windows: backups, firewall plan, HTTPS certificate, updates.</summary>
public sealed class InstallAndUpdateTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "shs-p11-" + Guid.NewGuid().ToString("N"));

    public InstallAndUpdateTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public async Task Daily_backup_keeps_the_14_latest_automatic_ones_and_never_deletes_manual_ones()
    {
        var paths = new AppPaths(_dir).EnsureCreated();
        await using var host = await TestHost.CreateAsync(s => s.AddSingleton(paths));
        var manual = await host.Run<BackupService, string>(b => b.CreateAsync(null));

        var first = await host.Run<BackupService, string?>(b => b.CreateAutomaticAsync());
        Assert.NotNull(first);
        host.Advance(TimeSpan.FromHours(5));   // service restarted the same day: no second copy
        Assert.Null(await host.Run<BackupService, string?>(b => b.CreateAutomaticAsync()));

        for (var day = 0; day < 20; day++)
        {
            host.Advance(TimeSpan.FromHours(24));
            Assert.NotNull(await host.Run<BackupService, string?>(b => b.CreateAutomaticAsync()));
        }

        var files = Directory.GetFiles(paths.Backups).Select(Path.GetFileName).ToList();
        Assert.Equal(BackupService.KeepAutomatic, files.Count(f => f!.EndsWith("-auto.db", StringComparison.Ordinal)));
        Assert.Contains(manual, files);
        Assert.DoesNotContain(first, files);   // the oldest automatic one went
    }

    [Fact]
    public void Firewall_rules_open_the_hotspot_ports_on_the_customer_adapter_only()
    {
        var rules = FirewallPlan.For("Ethernet 2", 80, 8080, 443);

        Assert.All(rules, r => Assert.Equal("Ethernet 2", r.InterfaceName));
        Assert.Equal(["TCP 80", "TCP 8080", "UDP 53", "TCP 443"],
            rules.Select(r => $"{(r.Protocol == FirewallPlan.Tcp ? "TCP" : "UDP")} {r.Port}"));
        Assert.DoesNotContain(FirewallPlan.For("Ethernet 2", 80, 8080, 0), r => r.Port == 443);   // HTTPS off
    }

    [Fact]
    public void Https_certificate_is_made_once_per_installation_and_reused()
    {
        var paths = new AppPaths(_dir).EnsureCreated();
        using var first = HttpsCertificate.LoadOrCreate(paths, TimeProvider.System, "CAISSE-PC");
        using var again = HttpsCertificate.LoadOrCreate(paths, TimeProvider.System, "CAISSE-PC");

        Assert.Equal(first.Thumbprint, again.Thumbprint);
        Assert.True(first.HasPrivateKey);
        Assert.InRange(first.NotAfter.ToUniversalTime(), DateTime.UtcNow.AddYears(5).AddDays(-2), DateTime.UtcNow.AddYears(5).AddDays(1));
        var names = first.Extensions.OfType<X509SubjectAlternativeNameExtension>().Single();
        Assert.Contains("CAISSE-PC", names.EnumerateDnsNames());
        Assert.Contains(IPAddress.Parse(HttpsCertificate.HotspotAddress), names.EnumerateIPAddresses());

        // Another installation gets its own key.
        using var other = HttpsCertificate.LoadOrCreate(new AppPaths(Path.Combine(_dir, "other")).EnsureCreated(), TimeProvider.System, "CAISSE-PC");
        Assert.NotEqual(first.GetPublicKey(), other.GetPublicKey());

        // A damaged file is replaced, not fatal.
        File.WriteAllText(Path.Combine(paths.Root, HttpsCertificate.FileName), "abîmé");
        using var replaced = HttpsCertificate.LoadOrCreate(paths, TimeProvider.System, "CAISSE-PC");
        Assert.NotEqual(first.Thumbprint, replaced.Thumbprint);
    }

    private static UpdateManifest Manifest(string version = "1.2.0", string url = "https://example.cg/StonesHotspot-Setup.exe") =>
        new(version, url, Convert.ToHexString(SHA256.HashData("installer"u8)).ToLowerInvariant(), "Corrections", DateTimeOffset.UtcNow);

    [Fact]
    public void Update_manifest_is_only_believed_when_signed_by_the_server_key()
    {
        var keys = KeyVault.Generate();
        var signed = SignedUpdate.Sign(Manifest(), keys.ServerPrivate);

        Assert.Equal("1.2.0", signed.Verify(keys.ServerPublic)?.Version);
        Assert.Null(signed.Verify(KeyVault.Generate().ServerPublic));                  // another key
        var payload = Convert.FromBase64String(signed.Payload);
        payload[^3] ^= 1;
        Assert.Null((signed with { Payload = Convert.ToBase64String(payload) }).Verify(keys.ServerPublic));   // changed
        Assert.Null(SignedUpdate.Sign(Manifest(url: "http://example.cg/setup.exe"), keys.ServerPrivate).Verify(keys.ServerPublic));   // not HTTPS

        // A signed licence answer cannot pass for a manifest.
        var answer = SignedAnswer.Sign(new ServerAnswer(ServerStatus.Valid, 1, "MID", "n", DateTimeOffset.UtcNow), keys.ServerPrivate);
        Assert.Null(new SignedUpdate(answer.Payload, answer.Signature).Verify(keys.ServerPublic));
    }

    [Theory]
    [InlineData("1.2.0", "1.1.9", true)]
    [InlineData("1.2.0", "1.2.0+82813cf", false)]
    [InlineData("1.2", "1.2.0", false)]
    [InlineData("1.10.0", "1.9.3", true)]
    [InlineData("1.1.0", "1.2.0", false)]
    public void Update_is_offered_only_for_a_newer_version(string offered, string running, bool newer)
    {
        Assert.Equal(newer, Manifest(offered).IsNewerThan(running));
        var status = new UpdateStatus();
        status.Set(Manifest(offered), running, DateTimeOffset.UtcNow);
        Assert.Equal(newer, status.Available is not null);
    }

    [Fact]
    public async Task Licence_server_publishes_the_signed_update_and_nothing_when_none_is_set()
    {
        var keys = KeyVault.Generate();
        foreach (var update in new[] { Manifest("2.0.0"), null })
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Services.AddSingleton(TimeProvider.System);
            builder.Services.AddLicenceServer(new ServerOptions(keys.ServerPrivate, keys.LicencePublic, "p", "t", update),
                new ServerStore(Path.Combine(_dir, $"server-{update is null}.db")));
            await using var server = builder.Build();
            server.MapLicenceServer();
            await server.StartAsync();
            var http = server.GetTestClient();

            var signed = await new LicenceServerClient(http, http.BaseAddress!, "1.0.0").GetUpdateAsync(CancellationToken.None);
            Assert.Equal(update?.Version, signed?.Verify(keys.ServerPublic)?.Version);
        }
    }

    private sealed class BytesHandler(byte[] body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) });
    }

    [Fact]
    public async Task Tray_keeps_the_downloaded_installer_only_when_its_hash_matches()
    {
        var installer = "MZ fake installer"u8.ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(installer));
        using var http = new HttpClient(new BytesHandler(installer));
        var folder = Path.Combine(_dir, "update");

        var file = await InstallerDownload.DownloadAndVerifyAsync(http, "https://example.cg/dl/StonesHotspot-Setup-1.2.0.exe", hash, folder);
        Assert.Equal("StonesHotspot-Setup-1.2.0.exe", Path.GetFileName(file));
        Assert.Equal(installer, File.ReadAllBytes(file));

        using var tampered = new HttpClient(new BytesHandler([.. installer, 0]));
        File.Delete(file);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            InstallerDownload.DownloadAndVerifyAsync(tampered, "https://example.cg/dl/StonesHotspot-Setup-1.2.0.exe", hash, folder));
        Assert.Empty(Directory.GetFiles(folder));   // nothing left to run

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            InstallerDownload.DownloadAndVerifyAsync(http, "http://example.cg/setup.exe", hash, folder));
    }

    [Fact]
    public async Task A_database_from_before_the_wizard_counts_as_set_up_and_a_new_one_does_not()
    {
        await using var fresh = await TestHost.CreateAsync(s => s.AddSingleton(new AppPaths(_dir)));
        Assert.False(await fresh.Run<SetupService, bool>(s => s.IsCompletedAsync()));

        // Upgrade: the setting did not exist yet, the next start marks it completed.
        await fresh.Run<HotspotDbContext>(db => db.Settings.Where(s => s.Key == SettingKeys.SetupCompleted).ExecuteDeleteAsync());
        await fresh.Run<DatabaseInitializer>(i => i.InitializeAsync());
        Assert.Equal("true", await fresh.Run<SettingsService, string?>(s => s.GetAsync(SettingKeys.SetupCompleted)));
    }

    [Fact]
    public async Task Offline_reset_gives_the_disabled_admin_a_new_temporary_password()
    {
        await using var host = await TestHost.CreateAsync();
        await host.Run<HotspotDbContext>(db => db.Staff.Where(s => s.Username == DatabaseInitializer.AdminUsername)
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.Enabled, false).SetProperty(s => s.MustChangePassword, false)));

        var password = await host.Run<StaffService, string>(s => s.ResetAdminOfflineAsync());

        var admin = await host.Run<StaffService, Core.Data.Entities.Staff?>(s => s.AuthenticateAsync(DatabaseInitializer.AdminUsername, password));
        Assert.NotNull(admin);
        Assert.True(admin.MustChangePassword);
    }
}
