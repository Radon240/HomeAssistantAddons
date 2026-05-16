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

        var entityMetadata = await AnalyzeEventPayloadFactory.LoadEntityMetadataAsync(
            entitiesService,
            cancellationToken);
        var request = new AnomalyDetectRequestPayload(
            batch.Events.Select(e => AnalyzeEventPayloadFactory.ToPayload(e, entityMetadata)).ToList(),
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

}

public sealed record AnomalyDetectionRunResult(
    bool Success,
    int ScannedEventCount,
    int ExcludedEventCount,
    int AnalyzedEventCount,
    int PersistedCount,
    string? Message);
