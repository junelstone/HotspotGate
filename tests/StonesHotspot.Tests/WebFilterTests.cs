using StonesHotspot.Core;
using StonesHotspot.Core.Data.Entities;
using StonesHotspot.Core.Services;
using StonesHotspot.Service;
using StonesHotspot.Tests.Infrastructure;
using static StonesHotspot.Tests.Infrastructure.TestPackets;

namespace StonesHotspot.Tests;

public class WebFilterTests
{
    [Theory]
    [InlineData("Facebook.com.", "facebook.com")]
    [InlineData("*.tiktok.com", "tiktok.com")]
    [InlineData("https://www.youtube.com/watch?v=1", "www.youtube.com")]
    [InlineData("41.202.1.1", "41.202.1.1")]
    [InlineData("41.202.0.0/16", "41.202.0.0/16")]
    [InlineData("not a domain", null)]
    [InlineData("localhost", null)]
    [InlineData("10.0.0.0/40", null)]
    [InlineData("", null)]
    public void Patterns_are_normalised_or_rejected(string input, string? expected) =>
        Assert.Equal(expected, WebFilterService.Normalize(input));

    [Fact]
    public async Task Ready_made_lists_are_added_once_and_removed_together()
    {
        await using var host = await TestHost.CreateAsync();
        await host.Run<WebFilterService, WebFilterRule>(s => s.AddAsync("tiktok.com", WebFilterKind.Block, null, null));

        var added = await host.Run<WebFilterService, int>(s => s.AddListAsync(FilterLists.SocialNetworks, null));
        Assert.Equal(FilterLists.SocialNetworkDomains.Count - 1, added);             // tiktok.com was already there
        Assert.Equal(0, await host.Run<WebFilterService, int>(s => s.AddListAsync(FilterLists.SocialNetworks, null)));
        Assert.DoesNotContain("whatsapp.com", FilterLists.SocialNetworkDomains);

        var rules = HotspotEngineService.ToFilterRules(await host.Run<WebFilterService, List<WebFilterRule>>(s => s.ListEnabledAsync()));
        Assert.True(rules.BlockedDomains.Matches("m.facebook.com"));

        Assert.Equal(added, await host.Run<WebFilterService, int>(s => s.RemoveListAsync(FilterLists.SocialNetworks, null)));
        Assert.Single(await host.Run<WebFilterService, List<WebFilterRule>>(s => s.ListAsync()));   // the hand-made rule stays
    }

    [Fact]
    public async Task Rules_are_stored_once_and_split_into_domains_and_ip_ranges()
    {
        await using var host = await TestHost.CreateAsync();
        await host.Run<WebFilterService, WebFilterRule>(s => s.AddAsync("facebook.com", WebFilterKind.Block, "social", null));
        await host.Run<WebFilterService, WebFilterRule>(s => s.AddAsync("31.13.0.0/16", WebFilterKind.Block, "social", null));
        await host.Run<WebFilterService, WebFilterRule>(s => s.AddAsync("mtn.cg", WebFilterKind.FreeAccess, null, null));
        var duplicate = await Assert.ThrowsAsync<BusinessRuleException>(() =>
            host.Run<WebFilterService, WebFilterRule>(s => s.AddAsync("FACEBOOK.COM", WebFilterKind.Block, null, null)));
        Assert.Equal(ErrorCodes.DuplicateName, duplicate.Code);

        var rules = HotspotEngineService.ToFilterRules(await host.Run<WebFilterService, List<WebFilterRule>>(s => s.ListEnabledAsync()));

        Assert.True(rules.BlockedDomains.Matches("www.facebook.com"));
        Assert.True(rules.BlockedIps.Contains(Ip("31.13.64.35")));
        Assert.True(rules.FreeDomains.Matches("momo.mtn.cg"));
        Assert.Equal(0, rules.FreeIps.Count);
    }
}
