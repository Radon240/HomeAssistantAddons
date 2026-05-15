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
            ReceivedAtUtc = evt.ReceivedAtUtc
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

        var query = db.StateChangeEvents.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(entityId))
        {
            query = query.Where(e => e.EntityId == entityId);
        }

        // SQLite (EF) не поддерживает ORDER BY по DateTimeOffset — сортируем по Id (монотонный).
        var rows = await query
            .OrderByDescending(e => e.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return rows.Select(Map).ToList();
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
            row.ReceivedAtUtc);
}
