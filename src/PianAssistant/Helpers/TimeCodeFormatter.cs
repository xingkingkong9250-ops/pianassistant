using System.Globalization;

namespace PianAssistant.Helpers;

/// <summary>
/// Formats <see cref="TimeSpan"/> into standard timecodes used by the player.
/// Three display modes:
///  - <see cref="FormatSmtc"/>: HH:MM:SS.fff (sub-second millisecond precision)
///  - <see cref="FormatFrames"/>: HH:MM:SS:FF (frame-precise, requires fps)
///  - <see cref="ToFileNameFragment"/>: HHMMSSfff (file-name safe, no separators)
/// </summary>
public static class TimeCodeFormatter
{
    public static string FormatSmtc(TimeSpan ts)
    {
        ts = Sanitize(ts);
        return string.Format(CultureInfo.InvariantCulture,
            "{0:D2}:{1:D2}:{2:D2}.{3:D3}",
            (int)ts.TotalHours, ts.Minutes, ts.Seconds, ts.Milliseconds);
    }

    public static string FormatFrames(TimeSpan ts, double fps)
    {
        if (fps <= 0) fps = 25.0;
        ts = Sanitize(ts);
        var totalFrames = (int)Math.Round(ts.TotalSeconds * fps);
        var frames = totalFrames % (int)Math.Round(fps);
        var seconds = totalFrames / (int)Math.Round(fps);
        var hh = seconds / 3600;
        var mm = (seconds / 60) % 60;
        var ss = seconds % 60;
        return string.Format(CultureInfo.InvariantCulture,
            "{0:D2}:{1:D2}:{2:D2}:{3:D2}", hh, mm, ss, frames);
    }

    public static string ToFileNameFragment(TimeSpan ts)
    {
        ts = Sanitize(ts);
        return string.Format(CultureInfo.InvariantCulture,
            "{0:D2}{1:D2}{2:D2}{3:D3}",
            (int)ts.TotalHours, ts.Minutes, ts.Seconds, ts.Milliseconds);
    }

    private static TimeSpan Sanitize(TimeSpan ts) =>
        ts < TimeSpan.Zero ? TimeSpan.Zero : ts;
}
