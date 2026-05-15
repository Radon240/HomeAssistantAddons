using HomeAiAddon.Api.BehaviorAnalysis;

namespace HomeAiAddon.Api.Data;

public interface IAnomalyAlertStore
{
    Task<int> UpsertBatchAsync(
        IReadOnlyList<AnomalyItemPayload> anomalies,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AnomalyAlertDto>> GetRecentAsync(
        int limit,
        DateTimeOffset? sinceUtc = null,
        CancellationToken cancellationToken = default);

    Task<int> PruneOlderThanAsync(
        DateTimeOffset cutoffUtc,
        CancellationToken cancellationToken = default);
}

public sealed record AnomalyAlertDto(
    long Id,
    string DetectionId,
    string EntityId,
    string AnomalyType,
    string Severity,
    double Score,
    string Method,
    string Title,
    string Explanation,
    DateTimeOffset DetectedAtUtc,
    DateTimeOffset PersistedAtUtc,
    IReadOnlyList<long> RelatedEventIds,
    IReadOnlyDictionary<string, object> Metrics);
