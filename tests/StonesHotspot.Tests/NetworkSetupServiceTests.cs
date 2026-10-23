using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using StonesHotspot.Network;
using StonesHotspot.Network.Adapters;
using StonesHotspot.Network.Gate;
using StonesHotspot.Network.Ics;
using StonesHotspot.Network.Setup;
using StonesHotspot.Tests.Infrastructure;
using static StonesHotspot.Tests.Infrastructure.FakeAdapters;

namespace StonesHotspot.Tests;

public sealed class NetworkSetupServiceTests
{
    private readonly List<string> _calls = [];
    private readonly FakeIcsController _ics;
    private readonly FakePacketGate _gate;
    private readonly FakeAdapterProvider _adapters = new(TypicalPc());
    private readonly FakePrivileges _privileges = new();

    public NetworkSetupServiceTests()
    {
        _ics = new FakeIcsController(_calls);
        _gate = new FakePacketGate(new PacketProcessor(new FakeTimeProvider()), _calls);
    }

    private Task<TestHost> HostAsync() => TestHost.CreateAsync(s => s
        .AddHotspotNetwork()
        .AddSingleton<IIcsController>(_ics)
        .AddSingleton<IPacketGate>(_gate)
        .AddSingleton<INetworkAdapterProvider>(_adapters)
        .AddSingleton<IPrivilegeChecker>(_privileges));

    private static Task<NetworkConfiguration> ConfigAsync(TestHost host) =>
        host.Run<NetworkSetupService, NetworkConfiguration>(s => s.GetConfigurationAsync());

    [Fact]
    public async Task Applying_starts_the_gate_before_sharing_and_remembers_the_choice()
    {
        await using var host = await HostAsync();

        var result = await host.Run<NetworkSetupService, NetworkApplyResult>(s => s.ApplyAsync(EthernetId, LanId, null));

        Assert.True(result.Succeeded);
        Assert.Equal(["gate-start", "ics-enable"], _calls);
        Assert.True(_ics.GetStatus().IsSharing(EthernetId, LanId));
        // The LAN card has no address yet (169.254.x): the gate uses the address ICS will give it.
        Assert.Equal(NetworkSetupService.DefaultIcsLan, _gate.Status.Config?.Lan);
        Assert.Equal(7, _gate.Status.Config?.LanInterfaceIndex);
        Assert.Equal(new NetworkConfiguration(NetworkMode.Ics, EthernetId, LanId), await ConfigAsync(host));
        Assert.Equal(1, await host.Query(db => db.AuditLogs.CountAsync(a => a.Action == "Network.SharingEnabled")));
    }

    [Fact]
    public async Task Invalid_choice_changes_nothing()
    {
        await using var host = await HostAsync();
        _privileges.IsAdministrator = false;

        var result = await host.Run<NetworkSetupService, NetworkApplyResult>(s => s.ApplyAsync(EthernetId, LanId, null));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Problems, p => p.Code == NetworkProblemCode.NotAdministrator);
        Assert.Empty(_calls);
        Assert.Equal(NetworkMode.None, (await ConfigAsync(host)).Mode);
    }

    [Fact]
    public async Task Gate_failure_never_leaves_the_network_shared()
    {
        await using var host = await HostAsync();
        _gate.FailWith = "WinDivert driver files are missing";

        var result = await host.Run<NetworkSetupService, NetworkApplyResult>(s => s.ApplyAsync(EthernetId, LanId, null));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Problems, p => p.Code == NetworkProblemCode.GateFailed && p.Detail == "WinDivert driver files are missing");
        Assert.Equal(0, _ics.EnableCalls);
        Assert.Contains("ics-disable", _calls);
        Assert.Equal(NetworkMode.None, (await ConfigAsync(host)).Mode);
    }

    [Fact]
    public async Task Windows_refusal_is_reported_with_its_message()
    {
        await using var host = await HostAsync();
        _ics.FailWith = "Windows refused (0x80040201)";

        var result = await host.Run<NetworkSetupService, NetworkApplyResult>(s => s.ApplyAsync(EthernetId, LanId, null));

        var problem = Assert.Single(result.Problems, p => p.Code == NetworkProblemCode.IcsFailed);
        Assert.Contains("0x80040201", problem.Detail);
        Assert.Equal(NetworkMode.None, (await ConfigAsync(host)).Mode);
    }

    [Fact]
    public async Task Mobile_hotspot_lan_is_filtered_without_touching_ics()
    {
        await using var host = await HostAsync();
        _adapters.Adapters.Add(Adapter(HotspotId, "Connexion au réseau local* 10", System.Net.NetworkInformation.NetworkInterfaceType.Wireless80211,
            cidr: "192.168.137.1/24", description: "Microsoft Wi-Fi Direct Virtual Adapter", index: 12));

        var result = await host.Run<NetworkSetupService, NetworkApplyResult>(s => s.ApplyAsync(EthernetId, HotspotId, null));

        Assert.True(result.Succeeded);
        Assert.Equal(["gate-start"], _calls);
        Assert.Equal(12, _gate.Status.Config?.LanInterfaceIndex);
        Assert.Equal(NetworkMode.MobileHotspot, (await ConfigAsync(host)).Mode);
    }

    [Fact]
    public async Task Reconcile_switches_sharing_back_on_and_follows_the_lan_address()
    {
        await using var host = await HostAsync();
        await host.Run<NetworkSetupService, NetworkApplyResult>(s => s.ApplyAsync(EthernetId, LanId, null));

        _ics.Shares.Clear(); // e.g. after a Windows update
        var overview = await host.Run<NetworkSetupService, NetworkOverview>(s => s.GetOverviewAsync());
        Assert.Contains(overview.Problems, p => p.Code == NetworkProblemCode.IcsNotActive);

        // ICS gave the LAN card another subnet (scope changed in the registry): the gate follows.
        var lan = _adapters.Adapters[1];
        _adapters.Adapters[1] = lan with { Addresses = [Ipv4Subnet.Parse("192.168.50.1/24")] };

        Assert.True((await host.Run<NetworkSetupService, NetworkApplyResult>(s => s.ReconcileAsync())).Succeeded);
        Assert.True(_ics.GetStatus().IsSharing(EthernetId, LanId));
        Assert.Equal(Ipv4Subnet.Parse("192.168.50.1/24"), _gate.Status.Config?.Lan);
    }

    [Fact]
    public async Task Reconcile_switches_sharing_off_when_the_gate_stops_working()
    {
        await using var host = await HostAsync();
        await host.Run<NetworkSetupService, NetworkApplyResult>(s => s.ApplyAsync(EthernetId, LanId, null));
        _gate.Stop();
        _gate.FailWith = "driver blocked";

        var result = await host.Run<NetworkSetupService, NetworkApplyResult>(s => s.ReconcileAsync());

        Assert.False(result.Succeeded);
        Assert.Empty(_ics.Shares);
        Assert.Equal(NetworkMode.Ics, (await ConfigAsync(host)).Mode); // still wanted: retried, and switched on once the gate works
        _gate.FailWith = null;
        Assert.True((await host.Run<NetworkSetupService, NetworkApplyResult>(s => s.ReconcileAsync())).Succeeded);
        Assert.True(_ics.GetStatus().IsSharing(EthernetId, LanId));
    }

    [Fact]
    public async Task Disabling_and_shutdown_switch_sharing_off_and_close_the_gate()
    {
        await using var host = await HostAsync();
        await host.Run<NetworkSetupService, NetworkApplyResult>(s => s.ApplyAsync(EthernetId, LanId, null));

        await host.Run<NetworkSetupService>(s => s.ShutdownAsync());
        Assert.Empty(_ics.Shares);
        Assert.False(_gate.Status.IsRunning);
        Assert.Equal(NetworkMode.Ics, (await ConfigAsync(host)).Mode); // comes back at the next start

        await host.Run<NetworkSetupService, NetworkApplyResult>(s => s.ReconcileAsync());
        await host.Run<NetworkSetupService, NetworkApplyResult>(s => s.DisableAsync(null));
        Assert.Empty(_ics.Shares);
        Assert.False(_gate.Status.IsRunning);
        Assert.Equal(new NetworkConfiguration(NetworkMode.None, EthernetId, LanId), await ConfigAsync(host));
    }
}
