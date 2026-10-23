using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using StonesHotspot.LicenseIssuer;
using StonesHotspot.LicenseServer;
using StonesHotspot.Licensing;
using StonesHotspot.Licensing.Codes;
using StonesHotspot.Licensing.Machine;
using StonesHotspot.Licensing.Online;

namespace StonesHotspot.Tests;

/// <summary>The owner's side: key vault, licence book (issue, renew, revoke), the licence server and the whole round trip.</summary>
public sealed class LicenceToolsTests : IAsyncLifetime
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "shs-tools-" + Guid.NewGuid().ToString("N"));
    private readonly LicenceServiceTests.AdjustableClock _clock = new(new DateTimeOffset(2026, 3, 2, 8, 0, 0, TimeSpan.Zero));
    private readonly OwnerKeys _keys = KeyVault.Generate();
    private WebApplication _server = null!;
    private HttpClient _http = null!;
    private const string Token = "manager-token-123";

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_dir);
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<TimeProvider>(_clock);
        builder.Services.AddLicenceServer(new ServerOptions(_keys.ServerPrivate, _keys.LicencePublic, "owner-pass", Token), new ServerStore(Path.Combine(_dir, "server.db")));
        _server = builder.Build();
        _server.MapLicenceServer();
        await _server.StartAsync();
        _http = _server.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        await _server.DisposeAsync();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_dir, recursive: true);
    }

    private LicenceBook Book() => new(Path.Combine(_dir, "licences.db"), _keys, _clock);

    private LicenseService Hotspot(MachineId machine, ITrialStore? store = null) =>
        new(new LicensingOptions(Path.Combine(_dir, "hotspot-" + machine), _keys.LicencePublic, _keys.ServerPublic),
            new FixedMachineIdentity(machine), store ?? new LicenceServiceTests.MemoryTrialStore(), _clock);

    private LicenceServerClient Client => new(_http, _http.BaseAddress!, "1.0-test");

    [Fact]
    public void Key_vault_needs_the_right_password_and_an_untouched_file()
    {
        var file = Path.Combine(_dir, "keys.shkey");
        Assert.Throws<ArgumentException>(() => KeyVault.Save(_keys, "short", file));
        KeyVault.Save(_keys, "un mot de passe long", file);

        Assert.DoesNotContain(Convert.ToBase64String(_keys.LicencePrivate), File.ReadAllText(file));
        Assert.Equal(_keys.LicencePrivate, KeyVault.Open(file, "un mot de passe long").LicencePrivate);
        Assert.Throws<WrongPasswordException>(() => KeyVault.Open(file, "un mot de passe faux"));
        Assert.Equal(Convert.ToBase64String(_keys.ServerPublic), KeyVault.PublicKeys(file).ServerPublic);

        var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(file))!;
        var bytes = Convert.FromBase64String(json["Ciphertext"]!.GetValue<string>());
        bytes[0] ^= 1;   // one bit changed in the encrypted key
        json["Ciphertext"] = Convert.ToBase64String(bytes);
        File.WriteAllText(file, json.ToJsonString());
        Assert.Throws<WrongPasswordException>(() => KeyVault.Open(file, "un mot de passe long"));
    }

    [Fact]
    public void Book_issues_renews_from_the_current_expiry_and_revokes()
    {
        using var book = Book();
        var customer = book.AddCustomer("Awa Ngoma", "06 123 45 67", "Bar Chez Ngoma");
        var pc = LicenceServiceTests.ThisPc.ToString();

        var first = book.Issue(customer.Id, pc, Edition.Pro, 365);
        Assert.Equal((1001u, new DateOnly(2027, 3, 1)), (first.Id, first.ExpiresOn));
        Assert.Equal(1002u, book.Issue(customer.Id, pc, Edition.Basic, 30).Id);
        var data = LicenceCode.Verify(first.Code, _keys.LicencePublic)!;
        Assert.Equal(("Bar Chez Ngoma", Edition.Pro), (data.Customer, data.Edition));
        Assert.Throws<ArgumentException>(() => book.Issue(customer.Id, "MID-nope", Edition.Pro, 365));

        _clock.Advance(TimeSpan.FromDays(300));   // renewing 65 days early loses nothing
        var renewed = book.Renew(first.Id, 365);
        Assert.Equal(new DateOnly(2028, 2, 29), renewed.ExpiresOn);   // 2027-03-01 + 365 days (2028 is a leap year)
        Assert.False(renewed.Synced);

        _clock.Advance(TimeSpan.FromDays(800));   // renewed after it ended: from today
        Assert.Equal(DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime).AddDays(29), book.Renew(first.Id, 30).ExpiresOn);

        var revoked = book.Revoke(first.Id, "PC vendu");
        Assert.True(revoked.Revoked);
        Assert.Throws<InvalidOperationException>(() => book.Renew(first.Id, 30));

        var csv = Encoding.UTF8.GetString(book.ExportCsv());
        Assert.Contains("1001;Awa Ngoma;06 123 45 67;", csv);
        Assert.Contains("PC vendu", csv);
        Assert.Contains("wa.me/242061234567?text=", Sharing.WhatsAppLink(revoked));
        Assert.Contains(revoked.Code, Sharing.LicFile(revoked));

        var zip = Path.Combine(_dir, "backup.zip");
        var keyFile = Path.Combine(_dir, "k.shkey");
        KeyVault.Save(_keys, "un mot de passe long", keyFile);
        book.Backup(zip, keyFile);
        using var archive = ZipFile.OpenRead(zip);
        Assert.Equal(["k.shkey", "licences.db"], archive.Entries.Select(e => e.Name).Order());
    }

    [Fact]
    public async Task Trial_is_registered_and_the_earliest_start_is_kept()
    {
        var pc = MachineId.FromRaw("a", "b", "c", "d");
        var hotspot = Hotspot(pc);
        var (answer, nonce) = await Client.RegisterTrialAsync(hotspot.MachineId, hotspot.TrialStartedAt, CancellationToken.None);
        Assert.True(hotspot.ApplyServerAnswer(answer, nonce));
        Assert.True(hotspot.TrialRegistered);

        // Reinstalled with every local trace wiped: the server remembers the first start.
        _clock.Advance(TimeSpan.FromDays(20));
        var wiped = Hotspot(pc);
        Assert.Equal(30, wiped.GetStatus().DaysLeft(_clock.GetUtcNow()));
        (answer, nonce) = await Client.RegisterTrialAsync(wiped.MachineId, wiped.TrialStartedAt, CancellationToken.None);
        wiped.ApplyServerAnswer(answer, nonce);
        Assert.Equal(10, wiped.GetStatus().DaysLeft(_clock.GetUtcNow()));
    }

    [Fact]
    public async Task Licence_round_trip_activation_renewal_and_revocation()
    {
        using var book = Book();
        var pc = LicenceServiceTests.ThisPc;
        var customer = book.AddCustomer("Awa", null, "Bar Chez Ngoma");
        var licence = book.Issue(customer.Id, pc.ToString(), Edition.Pro, 365);

        // Not synced yet: the server learns the licence when the hotspot activates it.
        var hotspot = Hotspot(pc);
        Assert.Equal(ActivationResult.Activated, hotspot.Activate(licence.Code));
        var (answer, nonce) = await Client.ActivateAsync(licence.Code, hotspot.MachineId, CancellationToken.None);
        Assert.True(hotspot.ApplyServerAnswer(answer, nonce));
        Assert.Equal(ServerStatus.Valid, answer.Verify(_keys.ServerPublic)!.Status);

        // LicenseManager syncs (with its token), renews early, syncs again: the next check-in brings the new code.
        var sync = new ManagerSync(_http, _http.BaseAddress!, Token);
        Assert.Equal(1, await sync.PushAsync(book));
        Assert.Equal(0, await sync.PushAsync(book));
        _clock.Advance(TimeSpan.FromDays(340));
        book.Renew(licence.Id, 365);
        await sync.PushAsync(book);
        (answer, nonce) = await Client.CheckInAsync(licence.Id, hotspot.MachineId, hotspot.Licence!.ExpiresOn, CancellationToken.None);
        Assert.Equal(ServerStatus.Renewed, answer.Verify(_keys.ServerPublic)!.Status);
        Assert.True(hotspot.ApplyServerAnswer(answer, nonce));
        Assert.Equal(new DateOnly(2028, 2, 29), hotspot.Licence!.ExpiresOn);

        // Revoked: grace, then stop.
        book.Revoke(licence.Id, "impayé");
        await sync.PushAsync(book);
        (answer, nonce) = await Client.CheckInAsync(licence.Id, hotspot.MachineId, hotspot.Licence.ExpiresOn, CancellationToken.None);
        hotspot.ApplyServerAnswer(answer, nonce);
        Assert.True(hotspot.GetStatus() is { InGracePeriod: true, RevokedAt: not null });
        _clock.Advance(TimeSpan.FromDays(7));
        Assert.False(hotspot.GetStatus().HotspotAllowed);

        // Another PC asking about this licence gets "unknown", which changes nothing.
        var other = MachineId.FromRaw("x", "y", "z", "w");
        (answer, _) = await Client.CheckInAsync(licence.Id, other.ToString(), new DateOnly(2027, 1, 1), CancellationToken.None);
        Assert.Equal(ServerStatus.Unknown, answer.Verify(_keys.ServerPublic)!.Status);
    }

    [Fact]
    public async Task Owner_endpoints_need_their_secret()
    {
        var upload = await _http.PostAsJsonAsync("/api/v1/manager/licences", Array.Empty<LicenceUpload>());
        Assert.Equal(HttpStatusCode.Unauthorized, upload.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await _http.GetAsync("/admin")).StatusCode);
        var request = new HttpRequestMessage(HttpMethod.Get, "/admin");
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("owner:owner-pass")));
        var page = await _http.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("Licences — Stone's Hotspot", await page.Content.ReadAsStringAsync());

        var forged = LicenceCode.Create(new LicenceData(5, Edition.Pro, new DateOnly(2026, 3, 1), new DateOnly(2030, 1, 1), LicenceServiceTests.ThisPc, "x"),
            Ed25519.GenerateKeyPair().PrivateKey);
        var activate = await _http.PostAsJsonAsync("/api/v1/activate", new ActivateRequest(forged, LicenceServiceTests.ThisPc.ToString(), "n", "v"));
        Assert.Equal(HttpStatusCode.BadRequest, activate.StatusCode);
    }
}
