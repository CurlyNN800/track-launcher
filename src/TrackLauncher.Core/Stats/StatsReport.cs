namespace TrackLauncher.Core.Stats;

/// <summary>Считает статистику для экрана «Статистика». Сессии через полночь делятся по дням.</summary>
public static class StatsReport
{
    public static object Build(StatsStore store, int days, Func<string, string?> colorOf)
    {
        days = Math.Clamp(days, 1, 366);
        var nowLocal = DateTime.Now;
        var todayLocal = nowLocal.Date;
        var fromLocal = todayLocal.AddDays(-(days - 1));
        var streakFromLocal = todayLocal.AddDays(-365);

        var rows = store.GetSessions(streakFromLocal.ToUniversalTime(), DateTime.UtcNow.AddMinutes(1));

        // Минуты по дням и трекам (локальное время).
        var byDay = new Dictionary<DateTime, Dictionary<string, double>>();
        foreach (var row in rows)
        {
            var start = row.StartedUtc.ToLocalTime();
            var end = row.EndedUtc.ToLocalTime();
            if (end > nowLocal) end = nowLocal;
            while (start < end)
            {
                var dayEnd = start.Date.AddDays(1);
                var chunkEnd = end < dayEnd ? end : dayEnd;
                var minutes = (chunkEnd - start).TotalMinutes;
                if (!byDay.TryGetValue(start.Date, out var tracks))
                    byDay[start.Date] = tracks = new Dictionary<string, double>();
                tracks[row.TrackId] = tracks.GetValueOrDefault(row.TrackId) + minutes;
                start = chunkEnd;
            }
        }

        var names = rows.GroupBy(r => r.TrackId).ToDictionary(g => g.Key, g => g.Last().TrackName);

        var periodRows = rows.Where(r => r.EndedUtc.ToLocalTime() > fromLocal).ToList();
        var perTrack = byDay
            .Where(d => d.Key >= fromLocal)
            .SelectMany(d => d.Value)
            .GroupBy(kv => kv.Key)
            .Select(g => new
            {
                trackId = g.Key,
                name = names.GetValueOrDefault(g.Key, g.Key),
                color = colorOf(g.Key),
                minutes = Math.Round(g.Sum(kv => kv.Value)),
                sessions = periodRows.Count(r => r.TrackId == g.Key),
            })
            .Where(t => t.minutes > 0)
            .OrderByDescending(t => t.minutes)
            .ToList();

        var perDay = Enumerable.Range(0, days)
            .Select(i => fromLocal.AddDays(i))
            .Select(day =>
            {
                var tracks = byDay.GetValueOrDefault(day) ?? new Dictionary<string, double>();
                return new
                {
                    date = day.ToString("yyyy-MM-dd"),
                    total = Math.Round(tracks.Values.Sum()),
                    tracks = tracks.ToDictionary(kv => kv.Key, kv => Math.Round(kv.Value)),
                };
            })
            .ToList();

        // Серия: сколько дней подряд (до сегодня включительно) был хотя бы один трек от 10 минут.
        int streak = 0;
        var cursor = byDay.ContainsKey(todayLocal) ? todayLocal : todayLocal.AddDays(-1);
        while (byDay.TryGetValue(cursor, out var t) && t.Values.Sum() >= 10)
        {
            streak++;
            cursor = cursor.AddDays(-1);
        }

        var longest = periodRows.Count == 0 ? 0 : periodRows.Max(r => (r.EndedUtc - r.StartedUtc).TotalMinutes);

        var recent = rows
            .OrderByDescending(r => r.StartedUtc)
            .Take(12)
            .Select(r => new
            {
                trackId = r.TrackId,
                name = r.TrackName,
                color = colorOf(r.TrackId),
                started = r.StartedUtc,
                minutes = Math.Round((r.EndedUtc - r.StartedUtc).TotalMinutes),
                open = r.Open,
            })
            .ToList();

        return new
        {
            days,
            totalMinutes = Math.Round(perTrack.Sum(t => t.minutes)),
            sessions = periodRows.Count,
            longestMinutes = Math.Round(longest),
            streakDays = streak,
            perTrack,
            perDay,
            recent,
        };
    }
}
