using System.Text.Json;
using HomeAiAddon.Api.BehaviorAnalysis;
using HomeAiAddon.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace HomeAiAddon.Api.Data;

public sealed class AnomalyAlertStore(ApplicationDbContext db) : IAnomalyAlertStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task<int> UpsertBatchAsync(
        IReadOnlyList<AnomalyItemPayload> anomalies,
        CancellationToken cancellationToken = default)
    {
        if (anomalies.Count == 0)
        {
            return 0;
        }

        var detectionIds = anomalies.Select(a => a.Id).Distinct().ToList();
        var existing = await db.AnomalyAlerts
            .Where(a => detectionIds.Contains(a.DetectionId))
            .ToDictionaryAsync(a => a.DetectionId, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var upserted = 0;

        foreach (var item in anomalies)
        {
            var relatedJson = JsonSerializer.Serialize(item.RelatedEventIds, JsonOptions);
            var metricsJson = JsonSerializer.Serialize(item.Metrics, JsonOptions);

            if (existing.TryGetValue(item.Id, out var row))
            {
                row.EntityId = item.EntityId;
                row.AnomalyType = item.AnomalyType;
                row.Severity = item.Severity;
                row.Score = item.Score;
                row.Method = item.Method;
                row.Title = item.Title;
                row.Explanation = item.Explanation;
                row.DetectedAtUtc = item.DetectedAtUtc;
                row.PersistedAtUtc = now;
                row.RelatedEventIdsJson = relatedJson;
                row.MetricsJson = metricsJson;
            }
            else
            {
                db.AnomalyAlerts.Add(new AnomalyAlertRecord
                {
                    DetectionId = item.Id,
                    EntityId = item.EntityId,
                    AnomalyType = item.AnomalyType,
                    Severity = item.Severity,
                    Score = item.Score,
                    Method = item.Method,
                    Title = item.Title,
                    Explanation = item.Explanation,
                    DetectedAtUtc = item.DetectedAtUtc,
                    PersistedAtUtc = now,
                    RelatedEventIdsJson = relatedJson,
                    MetricsJson = metricsJson
                });
            }

            upserted++;
        }

        await db.SaveChangesAsync(cancellationToken);
        return upserted;
    }

    public async Task<IReadOnlyList<AnomalyAlertDto>> GetRecentAsync(
        int limit,
        DateTimeOffset? sinceUtc = null,
        CancellationToken cancellationToken = default)
    {
        limit = Math.Clamp(limit, 1, 500);
        var query = db.AnomalyAlerts.AsNoTracking();

        if (sinceUtc.HasValue)
        {
            query = query.Where(a => a.DetectedAtUtc >= sinceUtc.Value);
        }

        var rows = await query
            .OrderByDescending(a => a.DetectedAtUtc)
            .ThenByDescending(a => a.Score)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return rows.Select(Map).ToList();
    }

    public async Task<int> PruneOlderThanAsync(
        DateTimeOffset cutoffUtc,
        CancellationToken cancellationToken = default)
    {
        var stale = await db.AnomalyAlerts
            .Where(a => a.DetectedAtUtc < cutoffUtc)
            .ToListAsync(cancellationToken);

        if (stale.Count == 0)
        {
            return 0;
        }

        db.AnomalyAlerts.RemoveRange(stale);
        await db.SaveChangesAsync(cancellationToken);
        return stale.Count;
    }

    private static AnomalyAlertDto Map(AnomalyAlertRecord row)
    {
        IReadOnlyList<long> relatedIds;
        try
        {
            relatedIds = JsonSerializer.Deserialize<List<long>>(row.RelatedEventIdsJson, JsonOptions)
                ?? [];
        }
        catch (JsonException)
        {
            relatedIds = [];
        }

        IReadOnlyDictionary<string, object> metrics;
        try
        {
            metrics = JsonSerializer.Deserialize<Dictionary<string, object>>(row.MetricsJson, JsonOptions)
                ?? new Dictionary<string, object>();
        }
        catch (JsonException)
        {
            metrics = new Dictionary<string, object>();
        }

        return new AnomalyAlertDto(
            row.Id,
            row.DetectionId,
            row.EntityId,
            row.AnomalyType,
            row.Severity,
            row.Score,
            row.Method,
            row.Title,
            row.Explanation,
            row.DetectedAtUtc,
            row.PersistedAtUtc,
            relatedIds,
            metrics);
    }
}
