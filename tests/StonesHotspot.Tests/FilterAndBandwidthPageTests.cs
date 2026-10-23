using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StonesHotspot.Core.Data;
using StonesHotspot.Core.Data.Entities;
using StonesHotspot.Core.Services;
using StonesHotspot.Network.Gate;
using StonesHotspot.Network.Gate.Packets;
using StonesHotspot.Network.Neighbors;
using StonesHotspot.Network.Setup;
using StonesHotspot.Service;
using StonesHotspot.Tests.Infrastructure;
using static StonesHotspot.Tests.Infrastructure.StaffSignIn;
using static StonesHotspot.Tests.Infrastructure.TestPackets;

namespace StonesHotspot.Tests;

public sealed class FilterAndBandwidthPageTests : IClassFixture<ServiceSmokeTests.Factory>, IAsyncLifetime
{
    private readonly ServiceSmokeTests.Factory _factory;

    public FilterAndBandwidthPageTests(ServiceSmokeTests.Factory factory) => _factory = factory;

    private HotspotEngineService Engine => _factory.Services.GetServices<IHostedService>().OfType<HotspotEngineService>().Single();
    private PacketProcessor Processor => _factory.Services.GetRequiredService<IPacketGate>().Processor;

    public async Task InitializeAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<NetworkSetupService>().ApplyAsync(FakeAdapters.EthernetId, FakeAdapters.LanId, null);
        await Engine.TickAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<T> InScope<T>(Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider);
    }

    private async Task Refresh()
    {
        await Engine.ReloadRulesAsync(CancellationToken.None);
        await Engine.TickAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Plan_limits_and_fair_share_reach_the_gate()
    {
        const string ip = "192.168.137.240", mac = "DA:A1:19:00:02:40";
        _factory.Neighbors.Add(new NeighborEntry(7, IPAddress.Parse(ip), mac, NeighborState.Reachable));
        await InScope(async sp =>
        {
            var tariff = (await sp.GetRequiredService<TariffService>().ListAsync()).Single(t => t.Name == "1 heure"); // 1024 / 512 kbit/s
            var voucher = (await sp.GetRequiredService<VoucherService>().GenerateBatchAsync(tariff.Id, 1, "bw", false, null)).Single();
            return await sp.GetRequiredService<SessionService>().LoginWithVoucherAsync(mac, ip, voucher.Code);
        });
        await Refresh();
        Assert.Equal((1024, 512), (Processor.Snapshot!.Authorized[Ip(ip)].DownKbps!.Value, Processor.Snapshot.Authorized[Ip(ip)].UpKbps!.Value));

        // Fair share: the line is 600 kbit/s down; with this one active device its share (600) is below the plan.
        var admin = await AsAsync(_factory);
        await PostFormAsync(admin.Http, "/admin/bandwidth", "/admin/bandwidth?handler=Save", new()
        {
            ["Enabled"] = "true", ["TotalDownKbps"] = "600", ["TotalUpKbps"] = "0", ["MinKbps"] = "100", ["MaxKbps"] = "0",
        });
        Processor.Process(Tcp(ip, 50000, "93.184.216.34", 443, TcpFlags.Syn), PacketSource.Forward, false); // the device is active
        await Refresh();

        var entry = Processor.Snapshot!.Authorized[Ip(ip)];
        Assert.Equal(600, entry.DownKbps);
        Assert.Equal(512, entry.UpKbps); // no upload total: the plan's limit stays

        var page = WebUtility.HtmlDecode(await admin.Http.GetStringAsync("/admin/bandwidth"));
        Assert.Contains("Partage équitable", page);
        await PostFormAsync(admin.Http, "/admin/bandwidth", "/admin/bandwidth?handler=Save", new()
        {
            ["Enabled"] = "false", ["TotalDownKbps"] = "600", ["TotalUpKbps"] = "0", ["MinKbps"] = "100", ["MaxKbps"] = "0",
        });
    }

    [Fact]
    public async Task Invalid_fair_share_values_are_refused()
    {
        var admin = await AsAsync(_factory);
        var response = await PostFormAsync(admin.Http, "/admin/bandwidth", "/admin/bandwidth?handler=Save", new()
        {
            ["Enabled"] = "true", ["TotalDownKbps"] = "0", ["TotalUpKbps"] = "0", ["MinKbps"] = "512", ["MaxKbps"] = "256",
        });
        Assert.Contains("Valeurs invalides", WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task Filter_page_blocks_a_ready_made_list_and_turns_on_family_dns()
    {
        var admin = await AsAsync(_factory);

        await PostFormAsync(admin.Http, "/admin/filter", "/admin/filter?handler=AddList", new() { ["list"] = FilterLists.Adult });
        await PostFormAsync(admin.Http, "/admin/filter", "/admin/filter?handler=FamilyDns", new() { ["enabled"] = "true" });
        await PostFormAsync(admin.Http, "/admin/filter", "/admin/filter?handler=AddRule", new() { ["pattern"] = "mtn.cg", ["kind"] = "FreeAccess" });
        await Refresh();

        var rules = Processor.Snapshot!.Rules;
        Assert.True(rules.BlockedDomains.Matches("www.pornhub.com"));
        Assert.True(rules.FreeDomains.Matches("momo.mtn.cg"));
        Assert.True(rules.FamilyDns);

        var page = WebUtility.HtmlDecode(await admin.Http.GetStringAsync("/admin/filter"));
        Assert.Contains($"Bloqués : {FilterLists.AdultDomains.Count} site(s)", page);

        await PostFormAsync(admin.Http, "/admin/filter", "/admin/filter?handler=RemoveList", new() { ["list"] = FilterLists.Adult });
        await PostFormAsync(admin.Http, "/admin/filter", "/admin/filter?handler=FamilyDns", new() { ["enabled"] = "false" });
        await Refresh();
        Assert.False(Processor.Snapshot!.Rules.BlockedDomains.Matches("www.pornhub.com"));
        Assert.False(Processor.Snapshot.Rules.FamilyDns);
    }

    [Fact]
    public async Task Mac_white_and_black_lists_are_managed_with_notes()
    {
        var admin = await AsAsync(_factory);

        await PostFormAsync(admin.Http, "/admin/filter", "/admin/filter?handler=Whitelist",
            new() { ["mac"] = "aa-bb-cc-11-22-33", ["note"] = "Téléphone caisse", ["add"] = "true" });
        await PostFormAsync(admin.Http, "/admin/filter", "/admin/filter?handler=Blacklist",
            new() { ["mac"] = "AABBCC445566", ["note"] = "Abus", ["add"] = "true" });
        var invalid = await PostFormAsync(admin.Http, "/admin/filter", "/admin/filter?handler=Blacklist", new() { ["mac"] = "pas-une-mac", ["add"] = "true" });

        var page = WebUtility.HtmlDecode(await admin.Http.GetStringAsync(invalid.Headers.Location!.OriginalString));
        Assert.Contains("Adresse MAC invalide", page);
        Assert.Contains("AA:BB:CC:11:22:33", page);
        Assert.Contains("Téléphone caisse", page);

        var devices = await InScope(sp => sp.GetRequiredService<HotspotDbContext>().Devices.AsNoTracking().ToListAsync());
        Assert.True(devices.Single(d => d.Mac == "AA:BB:CC:11:22:33").Whitelisted);
        Assert.True(devices.Single(d => d.Mac == "AA:BB:CC:44:55:66").Blocked);

        await PostFormAsync(admin.Http, "/admin/filter", "/admin/filter?handler=Blacklist", new() { ["mac"] = "AA:BB:CC:44:55:66", ["add"] = "false" });
        Assert.False(await InScope(sp => sp.GetRequiredService<HotspotDbContext>().Devices.AnyAsync(d => d.Mac == "AA:BB:CC:44:55:66" && d.Blocked)));
    }

    [Fact]
    public async Task Cashiers_cannot_open_filter_or_bandwidth_pages()
    {
        var cashier = await AsAsync(_factory, StaffRole.Cashier);
        foreach (var url in new[] { "/admin/filter", "/admin/bandwidth" })
            Assert.StartsWith("/admin/denied", (await cashier.Http.GetAsync(url)).Headers.Location!.PathAndQuery);
    }
}
