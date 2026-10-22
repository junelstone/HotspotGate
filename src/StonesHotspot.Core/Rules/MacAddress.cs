namespace StonesHotspot.Core.Rules;

public static class MacAddress
{
    /// <summary>
    /// Normalises "aa-bb-cc-dd-ee-ff", "AABB.CCDD.EEFF" or "aabbccddeeff" to "AA:BB:CC:DD:EE:FF".
    /// Returns false for anything that is not 12 hex digits.
    /// </summary>
    public static bool TryNormalize(string? text, out string mac)
    {
        mac = "";
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var hex = new string(text.Where(c => c is not (':' or '-' or '.' or ' ')).ToArray());
        if (hex.Length != 12 || !hex.All(char.IsAsciiHexDigit))
            return false;

        hex = hex.ToUpperInvariant();
        mac = string.Join(':', Enumerable.Range(0, 6).Select(i => hex.Substring(i * 2, 2)));
        return true;
    }

    public static string Normalize(string text) =>
        TryNormalize(text, out var mac) ? mac : throw new BusinessRuleException(ErrorCodes.Invalid, $"Invalid MAC address '{text}'.");

    /// <summary>
    /// True for "random/private" MACs (locally administered bit set), as used by recent phones. Such a device
    /// keeps the same MAC on this network but uses a different one elsewhere.
    /// </summary>
    public static bool IsRandomized(string normalizedMac) =>
        (Convert.ToByte(normalizedMac[..2], 16) & 0x02) != 0;
}
