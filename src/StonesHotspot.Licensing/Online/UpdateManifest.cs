using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using StonesHotspot.Licensing.Codes;

namespace StonesHotspot.Licensing.Online;

/// <summary>The latest version published by the owner on the licence server.</summary>
/// <param name="Url">HTTPS address of <c>StonesHotspot-Setup.exe</c>.</param>
/// <param name="Sha256">SHA-256 of the installer (hex): the tray app checks the downloaded file against it.</param>
public sealed record UpdateManifest(string Version, string Url, string Sha256, string? Notes, DateTimeOffset PublishedAt)
{
    /// <summary>True when the manifest's fields are usable (HTTPS address, 64 hex characters, version number).</summary>
    public bool IsWellFormed =>
        Uri.TryCreate(Url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
        && Sha256.Length == 64 && Sha256.All(Uri.IsHexDigit)
        && AppVersion.TryParse(Version, out _);

    /// <summary>True when this manifest is newer than the running version.</summary>
    public bool IsNewerThan(string current) =>
        AppVersion.TryParse(Version, out var offered) && (!AppVersion.TryParse(current, out var running) || offered > running);
}

/// <summary>Version numbers like <c>1.2.3</c>; anything after <c>+</c> or <c>-</c> (build info) is ignored.</summary>
public static class AppVersion
{
    public static bool TryParse(string? text, out Version version)
    {
        version = new Version(0, 0);
        if (string.IsNullOrWhiteSpace(text))
            return false;
        var core = text.Trim().TrimStart('v', 'V').Split('+', '-')[0];
        if (!System.Version.TryParse(core, out var parsed))
            return false;
        // 1.2 and 1.2.0 are the same version
        version = new Version(parsed.Major, parsed.Minor, Math.Max(parsed.Build, 0), Math.Max(parsed.Revision, 0));
        return true;
    }

    public static string Display(string text) => TryParse(text, out var v)
        ? v.Revision > 0 ? v.ToString() : v.ToString(3)
        : text;
}

/// <summary>
/// A manifest signed with the server key. The signed bytes start with a fixed label so a signed check-in answer
/// can never pass for a manifest (or the other way round).
/// </summary>
public sealed record SignedUpdate(
    [property: JsonPropertyName("payload")] string Payload,
    [property: JsonPropertyName("signature")] string Signature)
{
    private static readonly byte[] Label = "StonesHotspot.Update.v1\n"u8.ToArray();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static SignedUpdate Sign(UpdateManifest manifest, byte[] serverPrivateKey)
    {
        var payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(manifest, Json));
        return new SignedUpdate(Convert.ToBase64String(payload), Convert.ToBase64String(Ed25519.Sign(serverPrivateKey, [.. Label, .. payload])));
    }

    /// <summary>The manifest when the signature is the server's and its fields are usable; null otherwise.</summary>
    public UpdateManifest? Verify(byte[] serverPublicKey)
    {
        try
        {
            var payload = Convert.FromBase64String(Payload);
            if (!Ed25519.Verify(serverPublicKey, [.. Label, .. payload], Convert.FromBase64String(Signature)))
                return null;
            var manifest = JsonSerializer.Deserialize<UpdateManifest>(payload, Json);
            return manifest is { IsWellFormed: true } ? manifest : null;
        }
        catch (Exception ex) when (ex is FormatException or JsonException or ArgumentException)
        {
            return null;
        }
    }
}

/// <summary>The newest update the hotspot knows about (checked daily by the service), shown to admins and the tray.</summary>
public sealed class UpdateStatus
{
    private volatile UpdateManifest? _available;

    /// <summary>A newer, verified version; null when the hotspot is up to date or has not checked.</summary>
    public UpdateManifest? Available => _available;

    public DateTimeOffset? LastCheckedAt { get; private set; }

    public void Set(UpdateManifest? manifest, string currentVersion, DateTimeOffset now)
    {
        _available = manifest is not null && manifest.IsNewerThan(currentVersion) ? manifest : null;
        LastCheckedAt = now;
    }
}
