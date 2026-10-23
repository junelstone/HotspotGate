using System.Security.Cryptography;

namespace StonesHotspot.Core.Updates;

/// <summary>
/// Downloads an update installer (tray app) and keeps it only when its SHA-256 matches the one in the signed manifest
/// the service checked. A file that does not match is deleted, never run.
/// </summary>
public static class InstallerDownload
{
    public static async Task<string> DownloadAndVerifyAsync(HttpClient http, string url, string expectedSha256, string folder, CancellationToken ct = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidDataException("The update address must be https://.");
        if (expectedSha256.Length != 64 || !expectedSha256.All(Uri.IsHexDigit))
            throw new InvalidDataException("The update has no valid SHA-256.");

        Directory.CreateDirectory(folder);
        var name = Path.GetFileName(uri.LocalPath);
        if (string.IsNullOrWhiteSpace(name) || !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            name = "StonesHotspot-Setup.exe";
        var target = Path.Combine(folder, name);
        var partial = target + ".part";

        try
        {
            using (var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                response.EnsureSuccessStatusCode();
                await using var source = await response.Content.ReadAsStreamAsync(ct);
                await using var file = File.Create(partial);
                await source.CopyToAsync(file, ct);
            }

            byte[] hash;
            await using (var file = File.OpenRead(partial))
                hash = await SHA256.HashDataAsync(file, ct);
            if (!CryptographicOperations.FixedTimeEquals(hash, Convert.FromHexString(expectedSha256)))
                throw new InvalidDataException("The downloaded installer does not match the published SHA-256: it was deleted.");

            File.Move(partial, target, overwrite: true);
            return target;
        }
        finally
        {
            File.Delete(partial);
        }
    }
}
