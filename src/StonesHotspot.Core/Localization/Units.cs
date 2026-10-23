using System.Globalization;

namespace StonesHotspot.Core.Localization;

/// <summary>Durations, data sizes and speeds as customers read them, in French or English.</summary>
public static class Units
{
    private static bool IsFrench(CultureInfo culture) => culture.TwoLetterISOLanguageName == Languages.French;

    /// <summary>"2 j 3 h", "1 h 05 min", "4 min 09 s", "35 s" (English: "2 d 3 h"…).</summary>
    public static string Duration(long seconds, CultureInfo culture)
    {
        seconds = Math.Max(0, seconds);
        var days = seconds / 86400;
        var hours = seconds % 86400 / 3600;
        var minutes = seconds % 3600 / 60;
        var secs = seconds % 60;
        var d = IsFrench(culture) ? "j" : "d";

        if (days > 0)
            return hours > 0 ? $"{days} {d} {hours} h" : $"{days} {d}";
        if (hours > 0)
            return $"{hours} h {minutes:00} min";
        if (minutes > 0)
            return $"{minutes} min {secs:00} s";
        return $"{secs} s";
    }

    /// <summary>"850 Ko", "12,5 Mo", "1,2 Go" (English: KB, MB, GB). 1 MB = 1 048 576 bytes, as for quotas.</summary>
    public static string Bytes(long bytes, CultureInfo culture)
    {
        var french = IsFrench(culture);
        string[] units = french ? ["o", "Ko", "Mo", "Go", "To"] : ["B", "KB", "MB", "GB", "TB"];
        double value = Math.Max(0, bytes);
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        var format = unit == 0 || value >= 100 ? "0" : "0.#";
        return $"{value.ToString(format, culture)} {units[unit]}";
    }

    /// <summary>Speed from bits per second: "750 kbit/s", "1,5 Mbit/s".</summary>
    public static string Speed(double bitsPerSecond, CultureInfo culture)
    {
        var kbps = Math.Max(0, bitsPerSecond) / 1000;
        return kbps >= 1000
            ? $"{(kbps / 1000).ToString(kbps >= 10_000 ? "0" : "0.#", culture)} Mbit/s"
            : $"{kbps.ToString("0", culture)} kbit/s";
    }

    /// <summary>A tariff limit in kbit/s.</summary>
    public static string SpeedLimit(int kbps, CultureInfo culture) => Speed(kbps * 1000.0, culture);
}
