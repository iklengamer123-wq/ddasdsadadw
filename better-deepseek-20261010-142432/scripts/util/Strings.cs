using System;
using System.Globalization;

namespace CustomRhythia.Util;

public static class Strings
{
    public static string FormatTime(double seconds)
    {
        bool negative = seconds < 0;
        int minutes = (int)Math.Floor(Math.Abs(seconds) / 60);
        int secs = (int)Math.Floor(Math.Abs(seconds) % 60);
        return $"{(negative ? "-" : "")}{minutes}:{secs:00}";
    }

    public static string Pad(double value) =>
        value.ToString("N0", CultureInfo.InvariantCulture);

    public static string SanitizeZalgo(string input, int limit = 3)
    {
        var re = new System.Text.RegularExpressions.Regex("([\\p{M}]){" + (limit + 1) + ",}");
        return re.Replace(input ?? "", "$1");
    }
}
