using System.Globalization;
using System.Text.RegularExpressions;

namespace Ptlk.AlarmLogger.Configuration;

public sealed record QueryTimeZoneSetting(
    string Id,
    TimeZoneInfo? TimeZoneInfo,
    TimeSpan? FixedOffset,
    string? DatabaseTimeZoneId)
{
    public bool IsFixedOffset => FixedOffset.HasValue;

    public TimeSpan GetUtcOffset(DateTime localTime) =>
        FixedOffset ?? TimeZoneInfo!.GetUtcOffset(localTime);
}

public static class QueryTimeZoneParser
{
    private static readonly Regex OffsetRegex = new(
        @"^(?:(?:UTC|GMT)\s*)?(?<sign>[+-])(?<hours>\d{1,2})(?::?(?<minutes>\d{2}))?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Dictionary<string, string[]> TimeZoneAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Asia/Taipei"] = ["Asia/Taipei", "Taipei Standard Time"],
        ["Taipei Standard Time"] = ["Asia/Taipei", "Taipei Standard Time"],
        ["UTC"] = ["UTC", "Etc/UTC"],
        ["Etc/UTC"] = ["Etc/UTC", "UTC"]
    };

    public static bool TryParse(string? value, out QueryTimeZoneSetting? setting, out string? error)
    {
        setting = null;
        error = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            error = "time_zone is required.";
            return false;
        }

        var trimmed = value.Trim();
        if (TryParseFixedOffset(trimmed, out var fixedOffset))
        {
            setting = CreateFixedOffset(fixedOffset);
            return true;
        }

        if (TryFindTimeZone(trimmed, out var timeZone, out var databaseTimeZoneId))
        {
            setting = new QueryTimeZoneSetting(
                timeZone.Id,
                timeZone,
                null,
                databaseTimeZoneId);
            return true;
        }

        error = "time_zone must be a valid time zone id or UTC offset, for example Asia/Taipei, Taipei Standard Time, Z, +8, +08:00, UTC+08:00.";
        return false;
    }

    public static QueryTimeZoneSetting CreateFixedOffset(TimeSpan offset) =>
        new(
            FormatOffset(offset),
            null,
            offset,
            null);

    private static bool TryParseFixedOffset(string value, out TimeSpan offset)
    {
        offset = default;
        if (value.Equals("Z", StringComparison.OrdinalIgnoreCase)
            || value.Equals("UTC", StringComparison.OrdinalIgnoreCase)
            || value.Equals("GMT", StringComparison.OrdinalIgnoreCase))
        {
            offset = TimeSpan.Zero;
            return true;
        }

        var match = OffsetRegex.Match(value);
        if (!match.Success)
        {
            return false;
        }

        var sign = match.Groups["sign"].ValueSpan[0] == '-' ? -1 : 1;
        if (!int.TryParse(match.Groups["hours"].Value, CultureInfo.InvariantCulture, out var hours))
        {
            return false;
        }

        var minutes = 0;
        if (match.Groups["minutes"].Success
            && !int.TryParse(match.Groups["minutes"].Value, CultureInfo.InvariantCulture, out minutes))
        {
            return false;
        }

        if (hours > 14 || minutes > 59 || (hours == 14 && minutes != 0))
        {
            return false;
        }

        offset = new TimeSpan(hours * sign, minutes * sign, 0);
        return true;
    }

    private static bool TryFindTimeZone(
        string value,
        out TimeZoneInfo timeZone,
        out string databaseTimeZoneId)
    {
        if (TryFindTimeZoneById(value, out timeZone))
        {
            databaseTimeZoneId = ToDatabaseTimeZoneId(value);
            return true;
        }

        if (TimeZoneInfo.TryConvertWindowsIdToIanaId(value, out var ianaId)
            && TryFindTimeZoneById(ianaId, out timeZone))
        {
            databaseTimeZoneId = ianaId;
            return true;
        }

        if (TimeZoneInfo.TryConvertIanaIdToWindowsId(value, out var windowsId)
            && TryFindTimeZoneById(windowsId, out timeZone))
        {
            databaseTimeZoneId = value;
            return true;
        }

        if (TimeZoneAliases.TryGetValue(value, out var aliases))
        {
            foreach (var alias in aliases)
            {
                if (TryFindTimeZoneById(alias, out timeZone))
                {
                    databaseTimeZoneId = alias.Contains('/', StringComparison.Ordinal)
                        ? alias
                        : ToDatabaseTimeZoneId(alias);
                    return true;
                }
            }
        }

        timeZone = TimeZoneInfo.Utc;
        databaseTimeZoneId = "UTC";
        return false;
    }

    private static bool TryFindTimeZoneById(string id, out TimeZoneInfo timeZone)
    {
        try
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById(id);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
        }
        catch (InvalidTimeZoneException)
        {
        }

        timeZone = TimeZoneInfo.Utc;
        return false;
    }

    private static string ToDatabaseTimeZoneId(string id)
    {
        if (id.Equals("UTC", StringComparison.OrdinalIgnoreCase)
            || id.Equals("Etc/UTC", StringComparison.OrdinalIgnoreCase))
        {
            return "UTC";
        }

        return TimeZoneInfo.TryConvertWindowsIdToIanaId(id, out var ianaId)
            ? ianaId
            : id;
    }

    private static string FormatOffset(TimeSpan offset)
    {
        if (offset == TimeSpan.Zero)
        {
            return "Z";
        }

        var sign = offset < TimeSpan.Zero ? "-" : "+";
        var absolute = offset.Duration();
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{sign}{absolute.Hours:00}:{absolute.Minutes:00}");
    }
}
