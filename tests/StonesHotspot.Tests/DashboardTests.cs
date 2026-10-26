using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StonesHotspot.Core;
using StonesHotspot.Core.Data;
using StonesHotspot.Core.Data.Entities;
using StonesHotspot.Core.Money;
using StonesHotspot.Core.Services;
using StonesHotspot.Licensing;
using StonesHotspot.Network.Neighbors;
using StonesHotspot.Network.Setup;
using StonesHotspot.Service;
using StonesHotspot.Tests.Infrastructure;
using static StonesHotspot.Tests.Infrastructure.StaffSignIn;

namespace StonesHotspot.Tests;

/// <summary>The staff dashboard end to end (sign-in, selling, shifts, plans, vouchers, accounts, staff, settings, backup).</summary>
public sealed class DashboardTests : IClassFixture<ServiceSmokeTests.Factory>, IAsyncLifetime
{
    private readonly ServiceSmokeTests.Factory _factory;

    public DashboardTests(ServiceSmokeTests.Factory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<NetworkSetupService>().ApplyAsync(FakeAdapters.EthernetId, FakeAdapters.LanId, null);
        await _factory.Services.GetServices<IHostedService>().OfType<HotspotEngineService>().Single().TickAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<T> InScope<T>(Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider);
    }

    private Task<int> TariffIdAsync(string name = "30 minutes") =>
        InScope(async sp => (await sp.GetRequiredService<TariffService>().ListAsync()).Single(t => t.Name == name).Id);

    private static async Task<string> Html(HttpResponseMessage r) => WebUtility.HtmlDecode(await r.Content.ReadAsStringAsync()).Replace(' ', ' ');

    // ---- Sign-in ----

    [Fact]
    public async Task Wrong_passwords_are_refused_then_throttled()
    {
        var http = Client(_factory, "192.168.137.201");
        for (var i = 0; i < 4; i++)
        {
            var html = await Html(await PostFormAsync(http, "/admin/login", "/admin/login", new() { ["Username"] = "admin", ["Password"] = "wrong" + i }));
            Assert.Contains("Nom d'utilisateur ou mot de passe incorrect.", html);
        }
        var locked = await Html(await PostFormAsync(http, "/admin/login", "/admin/login", new() { ["Username"] = "admin", ["Password"] = "wrong" }));
        Assert.Contains("Réessayez dans 5 minute(s)", await Html(await PostFormAsync(http, "/admin/login", "/admin/login",
            new() { ["Username"] = "admin", ["Password"] = "still-wrong" })));
        Assert.Contains("incorrect", locked); // the 5th try is still answered, then the address waits
    }

    [Fact]
    public async Task First_admin_must_replace_the_initial_password_and_the_file_is_deleted()
    {
        var file = Path.Combine(_factory.DataDirectory, "initial-admin-password.txt");
        Assert.True(File.Exists(file));
        string initial;
        try
        {
            initial = Regex.Match(await File.ReadAllTextAsync(file), @"Password : (\S+)").Groups[1].Value;
        }
        catch (UnauthorizedAccessException) when (OperatingSystem.IsWindows())
        {
            // The file is for Administrators only, as intended; without admin rights take a temporary password
            // the way the PC reset does (the account must change it at first login all the same).
            initial = await InScope(sp => sp.GetRequiredService<StaffService>().ResetAdminOfflineAsync());
        }
        var http = Client(_factory);

        var login = await PostFormAsync(http, "/admin/login", "/admin/login", new() { ["Username"] = "admin", ["Password"] = initial });
        Assert.Equal("/Admin/Password", login.Headers.Location!.OriginalString, ignoreCase: true);
        Assert.EndsWith("/admin/password", (await http.GetAsync("/admin/sell")).Headers.Location!.OriginalString, StringComparison.OrdinalIgnoreCase);

        var changed = await PostFormAsync(http, "/admin/password", "/admin/password",
            new() { ["Current"] = initial, ["New"] = "Nouveau-Admin-2026", ["Confirm"] = "Nouveau-Admin-2026" });

        Assert.Equal("/admin", changed.Headers.Location!.OriginalString);
        Assert.False(File.Exists(file));
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/admin/sell")).StatusCode);
    }

    [Fact]
    public async Task Disabling_a_staff_member_signs_them_out_at_their_next_click()
    {
        var cashier = await AsAsync(_factory, StaffRole.Cashier);
        Assert.Equal(HttpStatusCode.OK, (await cashier.Http.GetAsync("/admin")).StatusCode);

        var admin = await AsAsync(_factory);
        await InScope(async sp =>
        {
            var actor = await sp.GetRequiredService<StaffService>().FindAsync(admin.Staff.Id);
            await sp.GetRequiredService<StaffService>().SetEnabledAsync(cashier.Staff.Id, false, actor!);
            return 0;
        });

        var response = await cashier.Http.GetAsync("/admin");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/admin/login", response.Headers.Location!.PathAndQuery);
    }

    // ---- The phase goal: a cashier on a phone sells a voucher and sees it in their shift ----

    [Fact]
    public async Task Cashier_on_a_phone_opens_a_shift_sells_and_sees_the_sale_in_the_shift()
    {
        var cashier = await AsAsync(_factory, StaffRole.Cashier, remoteIp: "192.168.137.210");
        var http = cashier.Http;

        // Selling is blocked until the shift is open.
        Assert.Contains("Ouvrez d'abord votre caisse", await Html(await http.GetAsync("/admin/sell")));

        await PostFormAsync(http, "/admin/shift", "/admin/shift?handler=Open", new() { ["Amount"] = "5 000" });

        var tariffId = await TariffIdAsync();
        var sale = await PostFormAsync(http, "/admin/sell", "/admin/sell?handler=New",
            new() { ["TariffId"] = tariffId.ToString(), ["Quantity"] = "2", ["Method"] = "Cash" });
        Assert.Equal(HttpStatusCode.Redirect, sale.StatusCode);
        var ticketUrl = sale.Headers.Location!.OriginalString;
        Assert.StartsWith("/admin/print?receipt=", ticketUrl);

        var ticket = await Html(await http.GetAsync(ticketUrl));
        Assert.Equal(2, Regex.Matches(ticket, "<svg").Count);                 // one QR per code
        Assert.Matches(@"[A-Z0-9]{4}-[A-Z0-9]{4}", ticket);
        Assert.Contains("200 FCFA", ticket);                                   // total
        Assert.Contains("http://192.168.137.1", ticket);                       // portal address for phones without a QR scanner

        var shift = await Html(await http.GetAsync("/admin/shift"));
        Assert.Contains("Vente de code", shift);
        Assert.Contains("5 200 FCFA", shift);                                  // expected cash: 5 000 + 2 × 100
        Assert.Contains("2 opération(s)", shift);

        // Closing with 100 FCFA missing shows the difference.
        var closed = await PostFormAsync(http, "/admin/shift", "/admin/shift?handler=Close", new()
        {
            ["shiftId"] = (await InScope(sp => sp.GetRequiredService<HotspotDbContext>().Shifts.Where(s => s.StaffId == cashier.Staff.Id).Select(s => s.Id).SingleAsync())).ToString(),
            ["Amount"] = "5100",
        });
        Assert.Contains("-100 FCFA", await Html(await http.GetAsync(closed.Headers.Location!.OriginalString)));
    }

    [Fact]
    public async Task Pre_printed_card_can_be_sold_by_its_code()
    {
        var admin = await AsAsync(_factory);
        var voucher = await InScope(async sp => (await sp.GetRequiredService<VoucherService>()
            .GenerateBatchAsync(await TariffIdAsync(), 1, "cards", false, null)).Single());

        var response = await PostFormAsync(admin.Http, "/admin/sell", "/admin/sell?handler=Card",
            new() { ["Code"] = voucher.Code.ToLowerInvariant(), ["Method"] = "MtnMoMo" });
        Assert.Contains("vendu", await Html(await admin.Http.GetAsync(response.Headers.Location!.OriginalString)));

        Assert.Equal(VoucherStatus.Sold, await InScope(sp => sp.GetRequiredService<HotspotDbContext>().Vouchers.Where(v => v.Id == voucher.Id).Select(v => v.Status).SingleAsync()));
        var again = await Html(await PostFormAsync(admin.Http, "/admin/sell", "/admin/sell?handler=Card", new() { ["Code"] = voucher.Code, ["Method"] = "Cash" }));
        Assert.Contains("déjà été vendu", again);
    }

    // ---- Admin areas ----

    [Fact]
    public async Task Plans_can_be_created_edited_and_deleted_when_unused()
    {
        var admin = await AsAsync(_factory);
        var created = await PostFormAsync(admin.Http, "/admin/tariff", "/admin/tariff", new()
        {
            ["Input.Name"] = "Nuit", ["Input.Price"] = "1 500", ["Input.Type"] = "Unlimited", ["Input.Quota"] = "0",
            ["Input.CountdownMode"] = "WallClock", ["Input.ValidityHours"] = "12", ["Input.MaxDevices"] = "2",
            ["Input.DownloadKbps"] = "2048", ["Input.IsActive"] = "true",
        });
        Assert.Equal("/admin/tariffs", created.Headers.Location!.OriginalString);
        var tariff = await InScope(sp => sp.GetRequiredService<HotspotDbContext>().Tariffs.AsNoTracking().SingleAsync(t => t.Name == "Nuit"));
        Assert.Equal((new Xaf(1500), 12, 2048), (tariff.Price, tariff.ValidityHours!.Value, tariff.DownloadKbps!.Value));

        var invalid = await Html(await PostFormAsync(admin.Http, $"/admin/tariff?id={tariff.Id}", $"/admin/tariff?id={tariff.Id}",
            new() { ["Input.Name"] = "Nuit", ["Input.Price"] = "15,50", ["Input.Type"] = "Unlimited", ["Input.MaxDevices"] = "1" }));
        Assert.Contains("Montant invalide", invalid);

        await PostFormAsync(admin.Http, "/admin/tariffs", "/admin/tariffs?handler=Delete", new() { ["id"] = tariff.Id.ToString() });
        Assert.False(await InScope(sp => sp.GetRequiredService<HotspotDbContext>().Tariffs.AnyAsync(t => t.Name == "Nuit")));
    }

    [Fact]
    public async Task Voucher_batch_prints_exports_and_disables()
    {
        var admin = await AsAsync(_factory);
        var create = await PostFormAsync(admin.Http, "/admin/vouchers", "/admin/vouchers?handler=Create",
            new() { ["TariffId"] = (await TariffIdAsync("1 heure")).ToString(), ["Count"] = "3", ["Name"] = "Lot test" });
        Assert.Equal("/admin/batch?name=Lot%20test", create.Headers.Location!.OriginalString);

        var a4 = await Html(await admin.Http.GetAsync("/admin/print?batch=Lot%20test"));
        Assert.Equal(3, Regex.Matches(a4, "<svg").Count);
        Assert.Contains("size:A4", a4);

        var csv = await admin.Http.GetAsync("/admin/batch?handler=Csv&name=Lot%20test");
        var bytes = await csv.Content.ReadAsByteArrayAsync();
        Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);
        var lines = System.Text.Encoding.UTF8.GetString(bytes[3..]).Trim().Split('\n');
        Assert.Equal(4, lines.Length);
        Assert.StartsWith("code;plan;price_xaf;status", lines[0]);
        Assert.Contains(";1 heure;200;New;", lines[1]);

        await PostFormAsync(admin.Http, "/admin/batch?name=Lot%20test", "/admin/batch?handler=Disable&name=Lot%20test", []);
        Assert.Equal(3, await InScope(sp => sp.GetRequiredService<HotspotDbContext>().Vouchers.CountAsync(v => v.BatchName == "Lot test" && v.Status == VoucherStatus.Disabled)));

        // A cashier cannot print a whole batch.
        var cashier = await AsAsync(_factory, StaffRole.Cashier);
        Assert.NotEqual(HttpStatusCode.OK, (await cashier.Http.GetAsync("/admin/print?batch=Lot%20test")).StatusCode);
    }

    [Fact]
    public async Task Live_view_adds_time_kicks_and_blocks()
    {
        const string ip = "192.168.137.220", mac = "DA:A1:19:00:02:20";
        _factory.Neighbors.Add(new NeighborEntry(7, IPAddress.Parse(ip), mac, NeighborState.Reachable));
        var session = await InScope(async sp =>
        {
            var voucher = (await sp.GetRequiredService<VoucherService>().GenerateBatchAsync(await TariffIdAsync(), 1, "live", false, null)).Single();
            return (await sp.GetRequiredService<SessionService>().LoginWithVoucherAsync(mac, ip, voucher.Code)).Session!;
        });
        var admin = await AsAsync(_factory);

        Assert.Contains(mac, await Html(await admin.Http.GetAsync("/admin/live")));

        await PostFormAsync(admin.Http, "/admin/live", "/admin/live?handler=AddTime", new() { ["sessionId"] = session.Id.ToString(), ["minutes"] = "60" });
        var status = await InScope(sp => sp.GetRequiredService<SessionService>().GetStatusForDeviceAsync(mac));
        Assert.InRange(status!.SecondsLeft!.Value, 90 * 60 - 5, 90 * 60);

        await PostFormAsync(admin.Http, "/admin/live", "/admin/live?handler=Kick", new() { ["sessionId"] = session.Id.ToString() });
        Assert.Equal(SessionEndReason.KickedByStaff, await InScope(sp => sp.GetRequiredService<HotspotDbContext>().Sessions.Where(s => s.Id == session.Id).Select(s => s.EndReason).SingleAsync()));

        await PostFormAsync(admin.Http, "/admin/live", "/admin/live?handler=Block", new() { ["mac"] = mac });
        Assert.True(await InScope(sp => sp.GetRequiredService<HotspotDbContext>().Devices.AnyAsync(d => d.Mac == mac && d.Blocked)));
    }

    [Fact]
    public async Task Accounts_are_created_by_admins_and_topped_up_by_cashiers()
    {
        var admin = await AsAsync(_factory);
        var created = await PostFormAsync(admin.Http, "/admin/accounts", "/admin/accounts?handler=Create", new()
        {
            ["Username"] = "fabrice", ["Password"] = "1234", ["Phone"] = "+242 06 123 45 67",
            ["TariffId"] = (await TariffIdAsync()).ToString(), ["Deposit"] = "2 000",
        });
        var accountUrl = created.Headers.Location!.OriginalString;
        Assert.Contains("2 000 FCFA", await Html(await admin.Http.GetAsync(accountUrl)));

        var cashier = await AsAsync(_factory, StaffRole.Cashier);
        await PostFormAsync(cashier.Http, "/admin/shift", "/admin/shift?handler=Open", new() { ["Amount"] = "0" });
        await PostFormAsync(cashier.Http, accountUrl, accountUrl.Replace("?", "?handler=Deposit&"), new() { ["Amount"] = "1000", ["Method"] = "AirtelMoney" });
        Assert.Equal(new Xaf(3000), await InScope(sp => sp.GetRequiredService<HotspotDbContext>().UserAccounts.Where(a => a.Username == "fabrice").Select(a => a.Balance).SingleAsync()));

        var refused = await PostFormAsync(cashier.Http, "/admin/accounts", "/admin/accounts?handler=Create",
            new() { ["Username"] = "pirate", ["Password"] = "1234", ["TariffId"] = (await TariffIdAsync()).ToString() });
        Assert.StartsWith("/admin/denied", refused.Headers.Location!.PathAndQuery);
        Assert.False(await InScope(sp => sp.GetRequiredService<HotspotDbContext>().UserAccounts.AnyAsync(a => a.Username == "pirate")));
    }

    [Fact]
    public async Task Extra_staff_accounts_are_a_pro_feature()
    {
        var admin = await AsAsync(_factory);
        var ok = await PostFormAsync(admin.Http, "/admin/staff", "/admin/staff?handler=Create",
            new() { ["Username"] = "grace", ["DisplayName"] = "Grace", ["Role"] = "Cashier", ["Password"] = "Temporaire-1" });
        Assert.Contains("grace", await Html(await admin.Http.GetAsync(ok.Headers.Location!.OriginalString)));
        Assert.True(await InScope(sp => sp.GetRequiredService<HotspotDbContext>().Staff.AnyAsync(s => s.Username == "grace" && s.MustChangePassword)));

        _factory.Licence.Edition = Edition.Basic;
        try
        {
            var page = await Html(await admin.Http.GetAsync("/admin/staff"));
            Assert.Contains("édition Pro", page);
            await PostFormAsync(admin.Http, "/admin/staff", "/admin/staff?handler=Create",
                new() { ["Username"] = "extra", ["Role"] = "Cashier", ["Password"] = "Temporaire-1" });
            Assert.False(await InScope(sp => sp.GetRequiredService<HotspotDbContext>().Staff.AnyAsync(s => s.Username == "extra")));
        }
        finally
        {
            _factory.Licence.Edition = Edition.Trial;
        }
    }

    [Fact]
    public async Task Settings_change_the_portal_and_reject_bad_values()
    {
        var admin = await AsAsync(_factory);
        Dictionary<string, string> Form(string color) => new()
        {
            ["BusinessName"] = "Cyber Café Moungali", ["WifiName"] = "Moungali-WiFi", ["AccentColor"] = color,
            ["DefaultLanguage"] = "fr", ["IdleTimeout"] = "15", ["WelcomeUrl"] = "",
        };

        Assert.Contains("Couleur invalide", await Html(await PostFormAsync(admin.Http, "/admin/settings", "/admin/settings", Form("red"))));
        await PostFormAsync(admin.Http, "/admin/settings", "/admin/settings", Form("#aa3300"));

        var portal = await Html(await Client(_factory, "192.168.137.230").GetAsync("/portal"));
        Assert.Contains("Cyber Café Moungali", portal);
        Assert.Contains("--accent:#aa3300", portal);
        Assert.Equal(15, await InScope(sp => sp.GetRequiredService<SettingsService>().GetIntAsync(SettingKeys.DefaultIdleTimeoutMinutes, 10)));
    }

    [Fact]
    public async Task Logo_upload_is_served_on_the_portal()
    {
        var admin = await AsAsync(_factory);
        var page = await admin.Http.GetStringAsync("/admin/settings");
        var form = new MultipartFormDataContent
        {
            { new StringContent(TokenRegex.Match(page).Groups[1].Value), "__RequestVerificationToken" },
            { new StringContent("Stone's Hotspot"), "BusinessName" },
            { new StringContent("#0b6e4f"), "AccentColor" },
            { new StringContent("fr"), "DefaultLanguage" },
            { new StringContent("10"), "IdleTimeout" },
            { new ByteArrayContent([0x89, 0x50, 0x4E, 0x47, 1, 2, 3]), "Logo", "mon-logo.PNG" },
        };
        Assert.Equal(HttpStatusCode.Redirect, (await admin.Http.PostAsync("/admin/settings", form)).StatusCode);

        var logo = await Client(_factory, "192.168.137.231").GetAsync("/portal/logo");
        Assert.Equal("image/png", logo.Content.Headers.ContentType?.MediaType);
        await PostFormAsync(admin.Http, "/admin/settings", "/admin/settings?handler=RemoveLogo", []);
    }

    [Fact]
    public async Task Backup_downloads_a_database_and_restore_brings_it_back()
    {
        var admin = await AsAsync(_factory);
        await InScope(async sp => { await sp.GetRequiredService<SettingsService>().SetAsync(SettingKeys.WifiName, "Avant", null); return 0; });

        var download = await PostFormAsync(admin.Http, "/admin/backup", "/admin/backup?handler=Create", []);
        var backup = await download.Content.ReadAsByteArrayAsync();
        Assert.Equal("SQLite format 3", System.Text.Encoding.ASCII.GetString(backup, 0, 15));

        await InScope(async sp => { await sp.GetRequiredService<SettingsService>().SetAsync(SettingKeys.WifiName, "Après", null); return 0; });

        var page = await admin.Http.GetStringAsync("/admin/backup");
        var restore = await admin.Http.PostAsync("/admin/backup?handler=Restore", new MultipartFormDataContent
        {
            { new StringContent(TokenRegex.Match(page).Groups[1].Value), "__RequestVerificationToken" },
            { new ByteArrayContent(backup), "Upload", "sauvegarde.db" },
        });
        Assert.Contains("Sauvegarde restaurée", await Html(await admin.Http.GetAsync(restore.Headers.Location!.OriginalString)));
        Assert.Equal("Avant", await InScope(sp => sp.GetRequiredService<SettingsService>().GetAsync(SettingKeys.WifiName)));

        var bad = await admin.Http.PostAsync("/admin/backup?handler=Restore", new MultipartFormDataContent
        {
            { new StringContent(TokenRegex.Match(await admin.Http.GetStringAsync("/admin/backup")).Groups[1].Value), "__RequestVerificationToken" },
            { new ByteArrayContent("not a database"u8.ToArray()), "Upload", "x.db" },
        });
        Assert.Contains("pas une sauvegarde Stone's Hotspot valide", await Html(await admin.Http.GetAsync(bad.Headers.Location!.OriginalString)));
    }
}
