namespace StonesHotspot.Web.Portal;

/// <summary>Where to send a customer after logging in.</summary>
public static class PortalTarget
{
    /// <summary>Hosts the operating systems use to detect captive portals: never a page the customer wanted.</summary>
    private static readonly string[] ProbeHosts =
    [
        "connectivitycheck.gstatic.com", "connectivitycheck.android.com", "clients3.google.com", "clients1.google.com",
        "www.google.com/generate_204", "captive.apple.com", "www.apple.com", "www.appleiphonecell.com",
        "www.msftconnecttest.com", "www.msftncsi.com", "ipv6.msftconnecttest.com", "detectportal.firefox.com",
        "nmcheck.gnome.org", "network-test.debian.org", "connectivity-check.ubuntu.com",
    ];

    /// <summary>
    /// The configured welcome page if any, else the page originally asked for (when it is a real http/https page),
    /// else null (stay on the status page).
    /// </summary>
    public static string? Choose(string? welcomeUrl, string? originalUrl)
    {
        if (IsSafe(welcomeUrl))
            return welcomeUrl;
        if (IsSafe(originalUrl) && !IsConnectivityCheck(originalUrl!))
            return originalUrl;
        return null;
    }

    /// <summary>Only absolute http(s) URLs (never javascript:, data:, relative tricks).</summary>
    public static bool IsSafe(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    public static bool IsConnectivityCheck(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;
        var host = uri.Host.ToLowerInvariant();
        return ProbeHosts.Contains(host)
               || uri.AbsolutePath.EndsWith("/generate_204", StringComparison.OrdinalIgnoreCase)
               || uri.AbsolutePath.EndsWith("/hotspot-detect.html", StringComparison.OrdinalIgnoreCase)
               || uri.AbsolutePath.EndsWith("/connecttest.txt", StringComparison.OrdinalIgnoreCase)
               || uri.AbsolutePath.EndsWith("/ncsi.txt", StringComparison.OrdinalIgnoreCase);
    }
}
