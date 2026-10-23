namespace StonesHotspot.Core.Services;

/// <summary>
/// Ready-made block lists. Adding one copies its entries into the web-filter rules (category set), where they can be
/// removed one by one. A domain also blocks its subdomains. Kept short and well known; for broad adult/malware
/// blocking turn on the family DNS option, which uses a maintained service.
/// </summary>
public static class FilterLists
{
    public const string SocialNetworks = "social";
    public const string Adult = "adult";

    /// <summary>WhatsApp is left out on purpose: it is how customers communicate. Add it by hand if wanted.</summary>
    public static IReadOnlyList<string> SocialNetworkDomains { get; } =
    [
        "facebook.com", "fbcdn.net", "facebook.net", "fb.com", "messenger.com",
        "instagram.com", "cdninstagram.com", "threads.net",
        "tiktok.com", "tiktokcdn.com", "tiktokv.com", "tiktokcdn-us.com", "byteoversea.com", "musical.ly",
        "snapchat.com", "snap.com", "sc-cdn.net", "snapkit.com",
        "twitter.com", "x.com", "twimg.com", "t.co",
        "linkedin.com", "licdn.com",
        "pinterest.com", "pinimg.com",
        "reddit.com", "redd.it", "redditmedia.com",
        "telegram.org", "t.me", "telegram.me",
        "tumblr.com", "vk.com", "likee.video", "imo.im",
    ];

    public static IReadOnlyList<string> AdultDomains { get; } =
    [
        "pornhub.com", "phncdn.com", "xvideos.com", "xvideos-cdn.com", "xnxx.com", "xhamster.com", "xhcdn.com",
        "redtube.com", "youporn.com", "tube8.com", "spankbang.com", "youjizz.com", "porn.com", "eporner.com",
        "tnaflix.com", "drtuber.com", "beeg.com", "motherless.com", "hqporner.com", "txxx.com",
        "onlyfans.com", "fansly.com", "chaturbate.com", "stripchat.com", "bongacams.com", "livejasmin.com",
        "cam4.com", "myfreecams.com", "camsoda.com", "brazzers.com", "realitykings.com", "bangbros.com",
    ];

    public static IReadOnlyList<string>? Domains(string list) => list switch
    {
        SocialNetworks => SocialNetworkDomains,
        Adult => AdultDomains,
        _ => null,
    };
}
