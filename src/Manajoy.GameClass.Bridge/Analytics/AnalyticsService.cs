using System.Globalization;
using Manajoy.GameClass.Bridge.Setup;

namespace Manajoy.GameClass.Bridge.Analytics;

/// <summary>
/// The owner's reports for the setup window: validates the request, loads the rows and keeps the result for a
/// minute (switching tabs or re-opening a period does not hit the GameClass database again).
/// </summary>
internal sealed class AnalyticsService(Func<IAnalyticsSource> source, TimeProvider? clock = null)
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(60);
    private static readonly HashSet<string> Groups = ["auto", "hour", "day", "week", "month"];

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly Dictionary<string, (DateTimeOffset At, object Value)> _cache = [];
    private readonly SemaphoreSlim _lock = new(1, 1);

    public Task<AnalyticsReport> ReportAsync(string? from, string? to, string? group, int dayStartHour, CancellationToken cancellationToken)
    {
        var start = ParseDate(from);
        var end = ParseDate(to);
        if (end < start || end.DayNumber - start.DayNumber + 1 > AnalyticsBuilder.MaxDays)
        {
            throw new SetupException("analytics_range", $"{start:yyyy-MM-dd}..{end:yyyy-MM-dd}");
        }

        var query = new AnalyticsQuery(start, end, Groups.Contains(group ?? "") ? group! : "auto", dayStartHour);
        return CachedAsync($"r|{start}|{end}|{query.Group}|{dayStartHour}", async () =>
        {
            var (loadFrom, loadTo) = AnalyticsBuilder.LoadRange(query);
            return AnalyticsBuilder.Build(query, await source().LoadAsync(loadFrom, loadTo, cancellationToken));
        });
    }

    public Task<AnalyticsDay> DayAsync(string? date, int dayStartHour, CancellationToken cancellationToken)
    {
        var day = ParseDate(date);
        return CachedAsync($"d|{day}|{dayStartHour}", async () =>
        {
            var (from, to) = AnalyticsBuilder.Window(new AnalyticsQuery(day, day, "hour", dayStartHour));
            return AnalyticsBuilder.Day(day, dayStartHour, await source().LoadAsync(from, to, cancellationToken));
        });
    }

    public void Clear()
    {
        lock (_cache)
        {
            _cache.Clear();
        }
    }

    private async Task<T> CachedAsync<T>(string key, Func<Task<T>> load) where T : class
    {
        // One load at a time: a year report on MSDE takes seconds, a double click must not run it twice.
        await _lock.WaitAsync();
        try
        {
            var now = _clock.GetUtcNow();
            lock (_cache)
            {
                if (_cache.TryGetValue(key, out var hit) && now - hit.At < CacheFor)
                {
                    return (T)hit.Value;
                }
            }

            T value;
            try
            {
                value = await load();
            }
            catch (Exception ex) when (ex is Data.GameClassUnavailableException or System.Data.Common.DbException)
            {
                // A year on a slow MSDE can hit the command timeout too; the UI suggests a shorter period.
                throw new SetupException("analytics_db", ex.Message, ex);
            }

            lock (_cache)
            {
                foreach (var stale in _cache.Where(e => now - e.Value.At >= CacheFor).Select(e => e.Key).ToList())
                {
                    _cache.Remove(stale);
                }

                _cache[key] = (now, value);
            }

            return value;
        }
        finally
        {
            _lock.Release();
        }
    }

    private static DateOnly ParseDate(string? value) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
        && date.Year is >= 2000 and <= 2100
            ? date
            : throw new SetupException("analytics_range", value);
}
