using System.Globalization;

namespace MovieTimestampNotes.Core;

public static class TimestampFormatter
{
    public static string Format(TimeSpan value, bool milliseconds = true)
    {
        var totalHours = (long)Math.Floor(value.TotalHours);
        return milliseconds
            ? $"{totalHours:00}:{value.Minutes:00}:{value.Seconds:00}.{value.Milliseconds:000}"
            : $"{totalHours:00}:{value.Minutes:00}:{value.Seconds:00}";
    }

    public static bool TryParse(string? text, out TimeSpan value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var pieces = text.Trim().Split(':');
        if (pieces.Length != 3 ||
            !long.TryParse(pieces[0], NumberStyles.None, CultureInfo.InvariantCulture, out var hours) ||
            !int.TryParse(pieces[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minutes) ||
            !double.TryParse(pieces[2], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var seconds) ||
            hours < 0 || minutes is < 0 or > 59 || seconds is < 0 or >= 60)
        {
            return false;
        }

        try
        {
            value = TimeSpan.FromHours(hours) + TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(seconds);
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }
}
