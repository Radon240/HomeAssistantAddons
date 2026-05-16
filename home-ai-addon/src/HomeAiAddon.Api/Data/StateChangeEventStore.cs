using HomeAiAddon.Api.Data.Entities;
using HomeAiAddon.Api.HomeAssistant;
using Microsoft.EntityFrameworkCore;

namespace HomeAiAddon.Api.Data;

public sealed class StateChangeEventStore(ApplicationDbContext db) : IStateChangeEventStore
{
    public async Task<long> AddAsync(NormalizedStateChangedEvent evt, CancellationToken cancellationToken = default)
    {
        var row = new StateChangeEventRecord
        {
            EntityId = evt.EntityId,
            OldState = evt.OldState,
            NewState = evt.NewState,
            FriendlyName = evt.FriendlyName,
            TimeFiredUtc = evt.TimeFiredUtc,
            ReceivedAtUtc = evt.ReceivedAtUtc,
            ContextId = evt.ContextId,
            ContextUserId = evt.ContextUserId,
            ContextParentId = evt.ContextParentId
        };

        db.StateChangeEvents.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        return row.Id;
    }

    public async Task<IReadOnlyList<StateChangeEventDto>> QueryAsync(
        int limit,
        string? entityId,
        CancellationToken cancellationToken = default)
    {
        limit = Math.Clamp(limit, 1, 500);
        const int scanLimit = 5000;

        // SQLite: без LIKE по шаблонам — берём последние записи и фильтруем в памяти (поддержка * и domain).
        var rows = await db.StateChangeEvents
            .AsNoTracking()
            .OrderByDescending(e => e.Id)
            .Take(scanLimit)
            .ToListAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(entityId))
        {
            rows = rows
                .Where(e => EntityPatternMatcher.MatchesEntityFilter(e.EntityId, entityId))
                .ToList();
        }

        return rows
            .Take(limit)
            .Select(Map)
            .ToList();
    }

    public async Task<AnalysisEventBatch> GetRecentForAnalysisAsync(
        int limit,
        Func<string, bool> includeEntity,
        CancellationToken cancellationToken = default)
    {
        limit = Math.Clamp(limit, 100, 10000);
        var scanLimit = Math.Clamp(limit * 4, limit, 15000);

        var rows = await db.StateChangeEvents
            .AsNoTracking()
            .OrderByDescending(e => e.Id)
            .Take(scanLimit)
            .ToListAsync(cancellationToken);

        var excluded = 0;
        var included = new List<StateChangeEventRecord>(limit);
        foreach (var row in rows)
        {
            if (!includeEntity(row.EntityId))
            {
                excluded++;
                continue;
            }

            included.Add(row);
            if (included.Count >= limit)
            {
                break;
            }
        }

        var events = included
            .OrderBy(e => e.TimeFiredUtc)
            .ThenBy(e => e.Id)
            .Select(Map)
            .ToList();

        return new AnalysisEventBatch(events, rows.Count, excluded);
    }

    public async Task<IReadOnlyList<HourlyEventBucket>> GetHourlyStatsAsync(
        int hours,
        CancellationToken cancellationToken = default)
    {
        hours = Math.Clamp(hours, 1, 48);
        var since = DateTimeOffset.UtcNow.AddHours(-hours);

        var timestamps = await db.StateChangeEvents
            .AsNoTracking()
            .Select(e => e.ReceivedAtUtc)
            .ToListAsync(cancellationToken);

        return timestamps
            .Where(d => d >= since)
            .GroupBy(d => new DateTimeOffset(d.Year, d.Month, d.Day, d.Hour, 0, 0, TimeSpan.Zero))
            .OrderBy(g => g.Key)
            .Select(g => new HourlyEventBucket(g.Key, g.Count()))
            .ToList();
    }

    private static StateChangeEventDto Map(StateChangeEventRecord row) =>
        new(
            row.Id,
            row.EntityId,
            row.OldState,
            row.NewState,
            row.FriendlyName,
            row.TimeFiredUtc,
            row.ReceivedAtUtc,
            row.ContextId,
            row.ContextUserId,
            row.ContextParentId);
}
