using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StonesHotspot.Network;
using StonesHotspot.Network.Adapters;
using StonesHotspot.Network.Ics;
using StonesHotspot.Network.Setup;
using StonesHotspot.Tests.Infrastructure;
using static StonesHotspot.Tests.Infrastructure.FakeAdapters;

namespace StonesHotspot.Tests;

public sealed class NetworkSetupServiceTests
{
    private readonly FakeIcsController _ics = new();
    private readonly FakeAdapterProvider _adapters = new(TypicalPc());
    private readonly FakePrivileges _privileges = new();

    private Task<TestHost> HostAsync() => TestHost.CreateAsync(s => s
        .AddHotspotNetwork()
        .AddSingleton<IIcsController>(_ics)
        .AddSingleton<INetworkAdapterProvider>(_adapters)
        .AddSingleton<IPrivilegeChecker>(_privileges));

    [Fact]
    public async Task Applying_enables_ics_and_remembers_the_choice()
    {
        await using var host = await HostAsync();

        var result = await host.Run<NetworkSetupService, NetworkApplyResult>(s => s.ApplyAsync(EthernetId, LanId, null));

        Assert.True(result.Succeeded);
        Assert.True(_ics.GetStatus().IsSharing(EthernetId, LanId));
        var config = await host.Run<NetworkSetupService, NetworkConfiguration>(s => s.GetConfigurationAsync());
        Assert.Equal(new NetworkConfiguration(NetworkMode.Ics, EthernetId, LanId), config);
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
        Assert.Equal(0, _ics.EnableCalls);
        Assert.Equal(NetworkMode.None, (await host.Run<NetworkSetupService, NetworkConfiguration>(s => s.GetConfigurationAsync())).Mode);
    }

    [Fact]
    public async Task Windows_refusal_is_reported_with_its_message()
    {
        await using var host = await HostAsync();
        _ics.FailWith = "Windows refused (0x80040201)";

        var result = await host.Run<NetworkSetupService, NetworkApplyResult>(s => s.ApplyAsync(EthernetId, LanId, null));

        var problem = Assert.Single(result.Problems, p => p.Code == NetworkProblemCode.IcsFailed);
        Assert.Contains("0x80040201", problem.Detail);
        Assert.Equal(NetworkMode.None, (await host.Run<NetworkSetupService, NetworkConfiguration>(s => s.GetConfigurationAsync())).Mode);
    }

    [Fact]
    public async Task Mobile_hotspot_lan_is_remembered_without_touching_ics()
    {
        await using var host = await HostAsync();
        _adapters.Adapters.Add(Adapter(HotspotId, "Connexion au réseau local* 10", System.Net.NetworkInformation.NetworkInterfaceType.Wireless80211,
            cidr: "192.168.137.1/24", description: "Microsoft Wi-Fi Direct Virtual Adapter"));

        var result = await host.Run<NetworkSetupService, NetworkApplyResult>(s => s.ApplyAsync(EthernetId, HotspotId, null));

        Assert.True(result.Succeeded);
        Assert.Equal(0, _ics.EnableCalls);
        Assert.Equal(NetworkMode.MobileHotspot, (await host.Run<NetworkSetupService, NetworkConfiguration>(s => s.GetConfigurationAsync())).Mode);
    }

    [Fact]
    public async Task Start_up_check_switches_sharing_back_on_when_windows_lost_it()
    {
        await using var host = await HostAsync();
        await host.Run<NetworkSetupService, NetworkApplyResult>(s => s.ApplyAsync(EthernetId, LanId, null));

        Assert.Null(await host.Run<NetworkSetupService, NetworkApplyResult?>(s => s.EnsureAppliedAsync())); // still on: nothing to do

        _ics.Shares.Clear(); // e.g. after a Windows update
        var overview = await host.Run<NetworkSetupService, NetworkOverview>(s => s.GetOverviewAsync());
        Assert.Contains(overview.Problems, p => p.Code == NetworkProblemCode.IcsNotActive);

        var result = await host.Run<NetworkSetupService, NetworkApplyResult?>(s => s.EnsureAppliedAsync());
        Assert.True(result?.Succeeded);
        Assert.True(_ics.GetStatus().IsSharing(EthernetId, LanId));
    }

    [Fact]
    public async Task Disabling_stops_sharing_and_keeps_the_adapter_choice()
    {
        await using var host = await HostAsync();
        await host.Run<NetworkSetupService, NetworkApplyResult>(s => s.ApplyAsync(EthernetId, LanId, null));

        await host.Run<NetworkSetupService, NetworkApplyResult>(s => s.DisableAsync(null));

        Assert.Empty(_ics.Shares);
        Assert.Equal(new NetworkConfiguration(NetworkMode.None, EthernetId, LanId),
            await host.Run<NetworkSetupService, NetworkConfiguration>(s => s.GetConfigurationAsync()));
        Assert.Null(await host.Run<NetworkSetupService, NetworkApplyResult?>(s => s.EnsureAppliedAsync()));
    }
}
