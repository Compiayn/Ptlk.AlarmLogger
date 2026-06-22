using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Ptlk.AlarmLogger.Configuration;
using Ptlk.AlarmLogger.Data;
using Ptlk.AlarmLogger.Models;

namespace Ptlk.AlarmLogger.Services.Query;

public sealed record AlarmHistoryItem(
    DateTimeOffset Timestamp,
    DateTimeOffset EventTime,
    string SourceName,
    string? CategoryTag,
    string ConditionName,
    string ConditionSubName,
    bool ConditionActive,
    string Quality,
    DateTimeOffset QualityTime,
    bool IsAcknowledge,
    bool NeedAck,
    object? OldValue,
    object? NewValue,
    string Message,
    DateTimeOffset ReceivedAt);

public sealed record AlarmHistoryRangeResult(
    DateTimeOffset? Begin,
    DateTimeOffset? End,
    string TimeZone,
    string Order,
    int Count,
    IReadOnlyList<AlarmHistoryItem> Data);

public sealed record AlarmHistoryPageResult(
    int Skip,
    int Take,
    int TotalCount,
    string TimeZone,
    string Order,
    IReadOnlyList<AlarmHistoryItem> Data);

public sealed class AlarmHistoryQueryService(
    IDbContextFactory<HistoryDbContext> dbFactory,
    IOptions<AlarmLoggerOptions> options)
{
    private static readonly Regex TimestampOffsetSuffixRegex = new(
        @"(?:[tT]|\s)\d{1,2}(?::?\d{2}){0,2}(?:[.,]\d+)?\s*(?<offset>[zZ]|[+-]\d{1,2}(?::?\d{2})?)\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly string[] OffsetTimestampFormats =
    [
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz",
        "yyyy-MM-dd'T'HH:mm:sszzz",
        "yyyy-MM-dd'T'HH:mmzzz",
        "yyyy-MM-dd'T'HHzzz",
        "yyyy-MM-dd HH:mm:ss.FFFFFFFzzz",
        "yyyy-MM-dd HH:mm:sszzz",
        "yyyy-MM-dd HH:mmzzz",
        "yyyy-MM-dd HHzzz",
        "yyyyMMdd'T'HHmmss.FFFFFFFzzz",
        "yyyyMMdd'T'HHmmsszzz",
        "yyyyMMdd'T'HHmmzzz",
        "yyyyMMdd'T'HHzzz"
    ];

    private static readonly string[] LocalTimestampFormats =
    [
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF",
        "yyyy-MM-dd'T'HH:mm:ss",
        "yyyy-MM-dd'T'HH:mm",
        "yyyy-MM-dd'T'HH",
        "yyyy-MM-dd HH:mm:ss.FFFFFFF",
        "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-dd HH:mm",
        "yyyy-MM-dd HH",
        "yyyy-MM-dd",
        "yyyyMMdd'T'HHmmss.FFFFFFF",
        "yyyyMMdd'T'HHmmss",
        "yyyyMMdd'T'HHmm",
        "yyyyMMdd'T'HH",
        "yyyyMMdd"
    ];

    private readonly QueryTimeZoneSetting _queryDefaultTimeZone = ParseConfiguredQueryTimeZone(options.Value.QueryDefaultTimeZone);

    public async Task<IResult> QueryRangeHttpAsync(
        string? begin,
        string? end,
        string? order,
        string? timeZone,
        string? categoryTag,
        CancellationToken cancellationToken = default)
    {
        var outcome = await QueryRangeAsync(begin, end, order, timeZone, categoryTag, cancellationToken);
        return ToHttpResult(outcome);
    }

    public async Task<IResult> QueryPageHttpAsync(
        int? skip,
        int? take,
        string? order,
        string? timeZone,
        string? categoryTag,
        CancellationToken cancellationToken = default)
    {
        var outcome = await QueryPageAsync(skip, take, order, timeZone, categoryTag, cancellationToken);
        return ToHttpResult(outcome);
    }

    public async Task<QueryOutcome<AlarmHistoryRangeResult>> QueryRangeAsync(
        string? begin,
        string? end,
        string? order,
        string? timeZone,
        string? categoryTag,
        CancellationToken cancellationToken = default)
    {
        if (!TryNormalizeOrder(order, out var normalizedOrder, out var orderError))
        {
            return QueryOutcome<AlarmHistoryRangeResult>.BadRequest(orderError);
        }

        var hasExplicitTimeZone = !string.IsNullOrWhiteSpace(timeZone);
        if (!TryResolveQueryTimeZone(timeZone, out var unqualifiedInputTimeZone, out var timeZoneError))
        {
            return QueryOutcome<AlarmHistoryRangeResult>.BadRequest(timeZoneError ?? "time_zone is invalid.");
        }

        if (!TryParseQueryTimestamp(
                begin,
                "begin",
                unqualifiedInputTimeZone,
                out var parsedBegin,
                out var beginContainedTimeZone,
                out var beginError))
        {
            return QueryOutcome<AlarmHistoryRangeResult>.BadRequest(beginError ?? "begin is invalid.");
        }

        var effectiveTimeZone = hasExplicitTimeZone
            ? unqualifiedInputTimeZone
            : beginContainedTimeZone ?? _queryDefaultTimeZone;
        if (!TryParseQueryTimestamp(
                end,
                "end",
                unqualifiedInputTimeZone,
                out var parsedEnd,
                out _,
                out var endError))
        {
            return QueryOutcome<AlarmHistoryRangeResult>.BadRequest(endError ?? "end is invalid.");
        }

        if (parsedBegin.HasValue && parsedEnd.HasValue && parsedBegin > parsedEnd)
        {
            return QueryOutcome<AlarmHistoryRangeResult>.BadRequest("begin must be less than or equal to end.");
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var query = db.AlarmHistoryRecords.AsNoTracking();
        if (parsedBegin.HasValue)
        {
            var beginUtc = parsedBegin.Value.ToUniversalTime();
            query = query.Where(x => x.Timestamp >= beginUtc);
        }
        if (parsedEnd.HasValue)
        {
            var endUtc = parsedEnd.Value.ToUniversalTime();
            query = query.Where(x => x.Timestamp <= endUtc);
        }
        query = ApplyCategoryFilter(query, categoryTag);

        query = normalizedOrder == "asc"
            ? query.OrderBy(x => x.Timestamp).ThenBy(x => x.Id)
            : query.OrderByDescending(x => x.Timestamp).ThenByDescending(x => x.Id);

        var records = await query
            .Take(options.Value.HistoryQueryMaxTake)
            .ToListAsync(cancellationToken);
        var items = records.Select(record => ToItem(record, effectiveTimeZone)).ToList();

        return QueryOutcome<AlarmHistoryRangeResult>.Ok(new AlarmHistoryRangeResult(
            parsedBegin.HasValue ? ConvertTimestampToTimeZone(parsedBegin.Value, effectiveTimeZone) : null,
            parsedEnd.HasValue ? ConvertTimestampToTimeZone(parsedEnd.Value, effectiveTimeZone) : null,
            effectiveTimeZone.Id,
            normalizedOrder,
            items.Count,
            items));
    }

    public async Task<QueryOutcome<AlarmHistoryPageResult>> QueryPageAsync(
        int? skip,
        int? take,
        string? order,
        string? timeZone,
        string? categoryTag,
        CancellationToken cancellationToken = default)
    {
        if (!TryNormalizeOrder(order, out var normalizedOrder, out var orderError))
        {
            return QueryOutcome<AlarmHistoryPageResult>.BadRequest(orderError);
        }
        if (skip is < 0)
        {
            return QueryOutcome<AlarmHistoryPageResult>.BadRequest("skip must be greater than or equal to 0.");
        }

        var effectiveSkip = skip ?? 0;
        var effectiveTake = take ?? options.Value.RecentHistoryTake;
        if (effectiveTake <= 0 || effectiveTake > options.Value.HistoryQueryMaxTake)
        {
            return QueryOutcome<AlarmHistoryPageResult>.BadRequest($"take must be between 1 and {options.Value.HistoryQueryMaxTake}.");
        }

        if (!TryResolveQueryTimeZone(timeZone, out var effectiveTimeZone, out var timeZoneError))
        {
            return QueryOutcome<AlarmHistoryPageResult>.BadRequest(timeZoneError ?? "time_zone is invalid.");
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var query = ApplyCategoryFilter(db.AlarmHistoryRecords.AsNoTracking(), categoryTag);
        var totalCount = await query.CountAsync(cancellationToken);
        query = normalizedOrder == "asc"
            ? query.OrderBy(x => x.Timestamp).ThenBy(x => x.Id)
            : query.OrderByDescending(x => x.Timestamp).ThenByDescending(x => x.Id);

        var records = await query
            .Skip(effectiveSkip)
            .Take(effectiveTake)
            .ToListAsync(cancellationToken);
        var items = records.Select(record => ToItem(record, effectiveTimeZone)).ToList();

        return QueryOutcome<AlarmHistoryPageResult>.Ok(new AlarmHistoryPageResult(
            effectiveSkip,
            effectiveTake,
            totalCount,
            effectiveTimeZone.Id,
            normalizedOrder,
            items));
    }

    private static IResult ToHttpResult<T>(QueryOutcome<T> outcome)
        where T : class
    {
        if (outcome.Result is not null)
        {
            return Results.Ok(outcome.Result);
        }

        return Results.BadRequest(new { error = outcome.Error });
    }

    private static IQueryable<AlarmHistoryRecord> ApplyCategoryFilter(
        IQueryable<AlarmHistoryRecord> query,
        string? categoryTag)
    {
        if (string.IsNullOrWhiteSpace(categoryTag))
        {
            return query;
        }

        var normalized = categoryTag.Trim();
        return query.Where(x => x.CategoryTag == normalized);
    }

    private AlarmHistoryItem ToItem(AlarmHistoryRecord record, QueryTimeZoneSetting timeZone)
    {
        return new AlarmHistoryItem(
            ConvertTimestampToTimeZone(record.Timestamp, timeZone),
            ConvertTimestampToTimeZone(record.EventTime, timeZone),
            record.SourceName,
            record.CategoryTag,
            record.ConditionName.ToString(),
            record.ConditionSubName,
            record.ConditionActive,
            record.Quality.ToString(),
            ConvertTimestampToTimeZone(record.QualityTime, timeZone),
            record.IsAcknowledge,
            record.NeedAck,
            ParseJsonValue(record.OldValueJson),
            ParseJsonValue(record.NewValueJson),
            record.Message,
            ConvertTimestampToTimeZone(record.ReceivedAt, timeZone));
    }

    private static object? ParseJsonValue(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<object>(json);
        }
        catch (JsonException)
        {
            return json;
        }
    }

    private bool TryParseQueryTimestamp(
        string? value,
        string parameterName,
        QueryTimeZoneSetting queryTimeZone,
        out DateTimeOffset? timestamp,
        out QueryTimeZoneSetting? containedTimeZone,
        out string? error)
    {
        timestamp = null;
        containedTimeZone = null;
        error = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        var trimmed = value.Trim();
        if (HasTimeZoneInfo(trimmed))
        {
            trimmed = NormalizeTimestampOffset(trimmed);
            if (TryParseOffsetTimestamp(trimmed, out var parsed))
            {
                timestamp = parsed;
                containedTimeZone = QueryTimeZoneParser.CreateFixedOffset(parsed.Offset);
                return true;
            }
        }
        else if (TryParseLocalTimestamp(trimmed, out var localTime))
        {
            localTime = DateTime.SpecifyKind(localTime, DateTimeKind.Unspecified);
            if (queryTimeZone.TimeZoneInfo is not null && queryTimeZone.TimeZoneInfo.IsInvalidTime(localTime))
            {
                error = $"{parameterName} is invalid in time_zone '{queryTimeZone.Id}' because it falls within a daylight-saving transition gap.";
                return false;
            }

            timestamp = new DateTimeOffset(
                localTime,
                queryTimeZone.GetUtcOffset(localTime));
            return true;
        }

        error = $"{parameterName} must be an ISO8601 timestamp. If no offset is provided, time_zone or AlarmLogger:QueryDefaultTimeZone is used.";
        return false;
    }

    private bool TryResolveQueryTimeZone(
        string? value,
        out QueryTimeZoneSetting queryTimeZone,
        out string? error)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            queryTimeZone = _queryDefaultTimeZone;
            error = null;
            return true;
        }

        if (QueryTimeZoneParser.TryParse(value, out var parsed, out error))
        {
            queryTimeZone = parsed!;
            return true;
        }

        queryTimeZone = _queryDefaultTimeZone;
        return false;
    }

    private static bool TryNormalizeOrder(string? value, out string order, out string error)
    {
        order = string.IsNullOrWhiteSpace(value) ? "desc" : value.Trim().ToLowerInvariant();
        error = "";
        if (order is "asc" or "desc")
        {
            return true;
        }

        error = "order must be asc or desc.";
        return false;
    }

    private static DateTimeOffset ConvertTimestampToTimeZone(
        DateTimeOffset timestamp,
        QueryTimeZoneSetting queryTimeZone)
    {
        if (queryTimeZone.FixedOffset.HasValue)
        {
            return timestamp.ToOffset(queryTimeZone.FixedOffset.Value);
        }

        return TimeZoneInfo.ConvertTime(timestamp, queryTimeZone.TimeZoneInfo!);
    }

    private static bool HasTimeZoneInfo(string value) => TimestampOffsetSuffixRegex.IsMatch(value);

    private static bool TryParseOffsetTimestamp(string value, out DateTimeOffset timestamp)
    {
        if (DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out timestamp))
        {
            return true;
        }

        var normalized = value.EndsWith('Z') || value.EndsWith('z')
            ? value[..^1] + "+00:00"
            : value;

        return DateTimeOffset.TryParseExact(
            normalized,
            OffsetTimestampFormats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces,
            out timestamp);
    }

    private static bool TryParseLocalTimestamp(string value, out DateTime timestamp)
    {
        if (DateTime.TryParseExact(
                value,
                LocalTimestampFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out timestamp))
        {
            return true;
        }

        return DateTime.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces,
            out timestamp);
    }

    private static string NormalizeTimestampOffset(string value)
    {
        var match = TimestampOffsetSuffixRegex.Match(value);
        if (!match.Success)
        {
            return value;
        }

        var offsetGroup = match.Groups["offset"];
        var offset = offsetGroup.Value;
        if (offset.Equals("Z", StringComparison.OrdinalIgnoreCase))
        {
            return value[..offsetGroup.Index] + "Z" + value[(offsetGroup.Index + offsetGroup.Length)..];
        }

        if (!QueryTimeZoneParser.TryParse(offset, out var parsed, out _)
            || parsed is null
            || !parsed.IsFixedOffset)
        {
            return value;
        }

        return value[..offsetGroup.Index] + parsed.Id + value[(offsetGroup.Index + offsetGroup.Length)..];
    }

    private static QueryTimeZoneSetting ParseConfiguredQueryTimeZone(string value)
    {
        if (QueryTimeZoneParser.TryParse(value, out var parsed, out _))
        {
            return parsed!;
        }

        throw new InvalidOperationException("AlarmLogger:QueryDefaultTimeZone is invalid.");
    }
}

public sealed record QueryOutcome<T>(
    T? Result,
    string? Error,
    int StatusCode)
    where T : class
{
    public static QueryOutcome<T> Ok(T result) => new(result, null, 200);
    public static QueryOutcome<T> BadRequest(string error) => new(null, error, 400);
}
