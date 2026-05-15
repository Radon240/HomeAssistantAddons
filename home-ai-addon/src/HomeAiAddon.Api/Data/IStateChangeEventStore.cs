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

    Task<IReadOnlyList<StateChangeEventDto>> GetRecentForAnalysisAsync(
        int limit,
        CancellationToken cancellationToken = default);
}

public sealed record StateChangeEventDto(
    long Id,
    string EntityId,
    string? OldState,
    string? NewState,
    string? FriendlyName,
    DateTimeOffset TimeFiredUtc,
    DateTimeOffset ReceivedAtUtc);

public sealed record HourlyEventBucket(DateTimeOffset HourUtc, int Count);
