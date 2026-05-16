using HomeAiAddon.Api.BehaviorAnalysis;
using HomeAiAddon.Api.Data;
using HomeAiAddon.Api.HomeAssistant;
using HomeAiAddon.Api.Options;
using Microsoft.Extensions.Options;

namespace HomeAiAddon.Api.AnomalyDetection;

public sealed class AnomalyDetectionService(
    IStateChangeEventStore eventStore,
    IBehaviorAnalysisClient analysisClient,
    IAnomalyAlertStore alertStore,
    AnalysisEntityFilter analysisEntityFilter,
    HomeAssistantEntitiesService entitiesService,
    IOptions<AnomalyDetectionOptions> options,
    ILogger<AnomalyDetectionService> logger)
{
    public async Task<AnomalyDetectionRunResult> RunDetectionAsync(
        CancellationToken cancellationToken = default)
    {
        var opts = options.Value;
        if (!opts.Enabled)
        {
            return new AnomalyDetectionRunResult(
                false,
                0,
                0,
                0,
                0,
                "Anomaly detection is disabled in configuration.");
        }

        if (!await analysisClient.IsHealthyAsync(cancellationToken))
        {
            return new AnomalyDetectionRunResult(
                false,
                0,
                0,
                0,
                0,
                "ML service is not reachable.");
        }

        var batch = await eventStore.GetRecentForAnalysisAsync(
            opts.EventLimit,
            analysisEntityFilter.ShouldInclude,
            cancellationToken);

        if (batch.Events.Count < opts.MinEvents)
        {
            return new AnomalyDetectionRunResult(
                true,
                batch.ScannedCount,
                batch.ExcludedCount,
                batch.Events.Count,
                0,
                $"Недостаточно событий для анализа ({batch.Events.Count}/{opts.MinEvents}).");
        }

        logger.LogInformation(
            "Running anomaly detection on {EventCount} events (scanned {Scanned}, excluded {Excluded})",
            batch.Events.Count,
            batch.ScannedCount,
            batch.ExcludedCount);

        var entityMetadata = await LoadEntityMetadataAsync(cancellationToken);
        var request = new AnomalyDetectRequestPayload(
            batch.Events.Select(e => ToAnalyzeEventPayload(e, entityMetadata)).ToList(),
            BuildMlOptions(opts));

        var result = await analysisClient.DetectAnomaliesAsync(request, cancellationToken);
        var persisted = await alertStore.UpsertBatchAsync(result.Anomalies, cancellationToken);

        var pruned = await alertStore.PruneOlderThanAsync(
            DateTimeOffset.UtcNow.AddDays(-opts.RetentionDays),
            cancellationToken);

        if (pruned > 0)
        {
            logger.LogInformation("Pruned {Count} stale anomaly alerts", pruned);
        }

        return new AnomalyDetectionRunResult(
            true,
            batch.ScannedCount,
            batch.ExcludedCount,
            result.AnalyzedEventCount,
            persisted,
            null);
    }

    private static AnomalyDetectionOptionsPayload BuildMlOptions(AnomalyDetectionOptions opts) =>
        new(
            opts.MinEvents,
            opts.MinEventsPerEntity,
            opts.MinHourlySamples,
            opts.RollingWindowHours,
            opts.ZScoreThreshold,
            opts.UnusualHourMaxRatio,
            opts.MinNumericDelta,
            opts.MinScore,
            opts.MediumSeverityThreshold,
            opts.HighSeverityThreshold,
            opts.MaxResults,
            opts.IsolationForestEstimators,
            opts.IsolationForestContamination,
            opts.IsolationForestMinSamples);

    private async Task<IReadOnlyDictionary<string, HomeAssistantEntityDto>> LoadEntityMetadataAsync(
        CancellationToken cancellationToken)
    {
        var entities = await entitiesService.GetEntitiesAsync(cancellationToken);
        return entities.ToDictionary(e => e.EntityId, StringComparer.OrdinalIgnoreCase);
    }

    private static AnalyzeEventPayload ToAnalyzeEventPayload(
        StateChangeEventDto e,
        IReadOnlyDictionary<string, HomeAssistantEntityDto> metadata)
    {
        metadata.TryGetValue(e.EntityId, out var meta);
        return new AnalyzeEventPayload(
            e.Id,
            e.EntityId,
            e.OldState,
            e.NewState,
            e.FriendlyName ?? meta?.FriendlyName,
            e.TimeFiredUtc,
            e.ReceivedAtUtc,
            meta?.Domain,
            meta?.DeviceClass,
            meta?.UnitOfMeasurement,
            meta?.EntityCategory,
            meta?.SupportedFeatures);
    }
}

public sealed record AnomalyDetectionRunResult(
    bool Success,
    int ScannedEventCount,
    int ExcludedEventCount,
    int AnalyzedEventCount,
    int PersistedCount,
    string? Message);
