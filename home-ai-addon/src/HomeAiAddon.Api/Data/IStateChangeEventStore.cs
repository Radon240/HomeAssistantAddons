using HomeAiAddon.Api.HomeAssistant;

namespace HomeAiAddon.Api.Data;

public interface IStateChangeEventStore
{
    Task<long> AddAsync(NormalizedStateChangedEvent evt, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StateChangeEventDto>> QueryAsync(
        int limit,
        string? entityId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<HourlyEventBucket>> GetHourlyStatsAsync(
        int hours,
        CancellationToken cancellationToken = default);

    Task<AnalysisEventBatch> GetRecentForAnalysisAsync(
        int limit,
        Func<string, bool> includeEntity,
        CancellationToken cancellationToken = default);
}

public sealed record AnalysisEventBatch(
    IReadOnlyList<StateChangeEventDto> Events,
    int ScannedCount,
    int ExcludedCount);

public sealed record StateChangeEventDto(
    long Id,
    string EntityId,
    string? OldState,
    string? NewState,
    string? FriendlyName,
    DateTimeOffset TimeFiredUtc,
    DateTimeOffset ReceivedAtUtc,
    string? ContextId,
    string? ContextUserId,
    string? ContextParentId);

public sealed record HourlyEventBucket(DateTimeOffset HourUtc, int Count);
