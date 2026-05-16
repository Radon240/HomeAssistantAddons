using HomeAiAddon.Api.BehaviorAnalysis;
using HomeAiAddon.Api.Data;
using HomeAiAddon.Api.HomeAssistant;
using HomeAiAddon.Api.Options;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace HomeAiAddon.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class RecommendationsController(
    IStateChangeEventStore store,
    IBehaviorAnalysisClient analysisClient,
    AnalysisEntityFilter analysisEntityFilter,
    HomeAssistantEntitiesService entitiesService,
    IOptions<BehaviorAnalysisOptions> options,
    ILogger<RecommendationsController> logger) : ControllerBase
{
    [HttpGet("status")]
    public async Task<ActionResult<RecommendationsStatusResponse>> GetStatus(
        CancellationToken cancellationToken = default)
    {
        var healthy = await analysisClient.IsHealthyAsync(cancellationToken);
        return Ok(new RecommendationsStatusResponse(
            healthy,
            options.Value.BaseUrl,
            healthy ? "ML service is reachable" : "ML service is not reachable"));
    }

    [HttpGet]
    public async Task<ActionResult<RecommendationsResponse>> Get(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!await analysisClient.IsHealthyAsync(cancellationToken))
            {
                return StatusCode(503, new
                {
                    error = "ML service unavailable",
                    detail = "Health check failed. Check /data/logs/ml-service.log and restart the add-on."
                });
            }

            var opts = options.Value;
            var exclusionSnapshot = analysisEntityFilter.GetSnapshot();
            var batch = await store.GetRecentForAnalysisAsync(
                opts.EventLimit,
                analysisEntityFilter.ShouldInclude,
                cancellationToken);

            if (batch.Events.Count == 0)
            {
                var hint = exclusionSnapshot.HasExclusions
                    ? " После исключений не осталось событий — уменьшите список исключений в UI."
                    : " Дождитесь накопления данных из Home Assistant.";
                return Ok(new RecommendationsResponse(
                    0,
                    0,
                    0,
                    0,
                    batch.ScannedCount,
                    batch.ExcludedCount,
                    Array.Empty<RecommendationPayload>(),
                    "Недостаточно событий для анализа." + hint,
                    exclusionSnapshot.EffectiveExcludeEntities,
                    exclusionSnapshot.EffectiveExcludeDomains));
            }

            logger.LogInformation(
                "Sending {EventCount} events to ML service (scanned {Scanned}, excluded {Excluded})",
                batch.Events.Count,
                batch.ScannedCount,
                batch.ExcludedCount);

            var entityMetadata = await AnalyzeEventPayloadFactory.LoadEntityMetadataAsync(entitiesService, cancellationToken);
            var request = new AnalyzeRequestPayload(
                batch.Events.Select(e => AnalyzeEventPayloadFactory.ToPayload(e, entityMetadata)).ToList(),
                BuildAnalyzeOptionsPayload(opts));

            var result = await analysisClient.AnalyzeAsync(request, cancellationToken);
            return Ok(new RecommendationsResponse(
                result.AnalyzedEventCount,
                result.SessionCount,
                result.PatternCandidates,
                result.FeedbackTrainingSamples,
                batch.ScannedCount,
                batch.ExcludedCount,
                result.Recommendations,
                null,
                exclusionSnapshot.EffectiveExcludeEntities,
                exclusionSnapshot.EffectiveExcludeDomains));
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "ML service request failed");
            return StatusCode(503, new { error = "ML service unavailable", detail = ex.Message });
        }
        catch (TaskCanceledException ex)
        {
            logger.LogWarning(ex, "ML service request timed out");
            return StatusCode(503, new
            {
                error = "ML service timeout",
                detail = $"Analysis exceeded {options.Value.TimeoutSeconds}s. Reduce event volume or increase BehaviorAnalysis:TimeoutSeconds."
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Recommendations failed");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpGet("diagnostics")]
    public async Task<ActionResult<RecommendationDiagnosticsResponse>> GetDiagnostics(
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!await analysisClient.IsHealthyAsync(cancellationToken))
            {
                return StatusCode(503, new { error = "ML service unavailable" });
            }

            var opts = options.Value;
            var batch = await store.GetRecentForAnalysisAsync(
                opts.EventLimit,
                analysisEntityFilter.ShouldInclude,
                cancellationToken);

            if (batch.Events.Count == 0)
            {
                return Ok(new RecommendationDiagnosticsResponse(
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    batch.ScannedCount,
                    batch.ExcludedCount,
                    [],
                    [],
                    [],
                    [],
                    [],
                    "Недостаточно событий для диагностики."));
            }

            var entityMetadata = await AnalyzeEventPayloadFactory.LoadEntityMetadataAsync(entitiesService, cancellationToken);
            var request = new AnalyzeRequestPayload(
                batch.Events.Select(e => AnalyzeEventPayloadFactory.ToPayload(e, entityMetadata)).ToList(),
                BuildAnalyzeOptionsPayload(opts));

            var result = await analysisClient.AnalyzeDiagnosticsAsync(request, cancellationToken);
            return Ok(new RecommendationDiagnosticsResponse(
                result.AnalyzedEventCount,
                result.EligibleEventCount,
                result.SessionCount,
                result.RawSequenceCandidateCount,
                result.SemanticRejectedCandidateCount,
                result.SensorToSensorCandidateCount,
                result.MeaningfulCandidateCount,
                result.QualityFilteredCandidateCount,
                result.RecommendationCount,
                batch.ScannedCount,
                batch.ExcludedCount,
                result.FilterReasons,
                result.SemanticRoles,
                result.SemanticIntents,
                result.OriginTypes,
                result.WeightBuckets,
                null));
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "ML diagnostics request failed");
            return StatusCode(503, new { error = "ML service unavailable", detail = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Recommendation diagnostics failed");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpPost("{id}/feedback")]
    public async Task<ActionResult<FeedbackResponsePayload>> SubmitFeedback(
        string id,
        [FromBody] RecommendationFeedbackRequest body,
        CancellationToken cancellationToken = default)
    {
        var verdict = body.Verdict?.Trim().ToLowerInvariant();
        if (verdict is not ("useful" or "not_useful"))
        {
            return BadRequest(new { error = "verdict must be 'useful' or 'not_useful'" });
        }

        try
        {
            if (!await analysisClient.IsHealthyAsync(cancellationToken))
            {
                return StatusCode(503, new { error = "ML service unavailable" });
            }

            var patternKey = body.PatternKey.Trim();
            if (string.IsNullOrWhiteSpace(patternKey) && body.EntityIds is { Count: > 0 })
            {
                patternKey = string.Join('|', body.EntityIds);
            }

            if (string.IsNullOrWhiteSpace(patternKey))
            {
                return BadRequest(new { error = "patternKey is required" });
            }

            var entityIds = body.EntityIds?.Count > 0
                ? body.EntityIds
                : Array.Empty<string>();

            var result = await analysisClient.SubmitFeedbackAsync(
                new FeedbackRequestPayload(
                    id,
                    patternKey,
                    verdict,
                    string.IsNullOrWhiteSpace(body.Cadence) ? "irregular" : body.Cadence.Trim(),
                    body.SupportCount ?? 0,
                    body.Confidence ?? 0,
                    body.FrequencyScore ?? 0,
                    entityIds),
                cancellationToken);

            return Ok(result);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "ML feedback request failed");
            return StatusCode(503, new { error = "ML service unavailable", detail = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Feedback failed for {RecommendationId}", id);
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpGet("feedback/state")]
    public async Task<ActionResult<FeedbackStateResponse>> GetFeedbackState(
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!await analysisClient.IsHealthyAsync(cancellationToken))
            {
                return StatusCode(503, new { error = "ML service unavailable" });
            }

            var state = await analysisClient.GetFeedbackStateAsync(cancellationToken);
            return Ok(new FeedbackStateResponse(
                state.TrainingSamples,
                ToEntries(state.PatternUseful),
                ToEntries(state.PatternNotUseful),
                ToEntries(state.EntityUseful),
                ToEntries(state.EntityNotUseful),
                state.DismissedUntil
                    .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(kv => new DismissedFeedbackEntry(kv.Key, kv.Value))
                    .ToList()));
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "ML feedback state request failed");
            return StatusCode(503, new { error = "ML service unavailable", detail = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Feedback state failed");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpDelete("feedback")]
    public async Task<ActionResult<FeedbackResponsePayload>> ResetFeedback(
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!await analysisClient.IsHealthyAsync(cancellationToken))
            {
                return StatusCode(503, new { error = "ML service unavailable" });
            }

            return Ok(await analysisClient.ResetFeedbackAsync(cancellationToken));
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "ML feedback reset request failed");
            return StatusCode(503, new { error = "ML service unavailable", detail = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Feedback reset failed");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpPost("feedback/reset-items")]
    public async Task<ActionResult<FeedbackResponsePayload>> ResetFeedbackItems(
        [FromBody] ResetFeedbackItemsRequest body,
        CancellationToken cancellationToken = default)
    {
        var patternKeys = Normalize(body.PatternKeys);
        var recommendationIds = Normalize(body.RecommendationIds);
        var entityIds = Normalize(body.EntityIds);
        if (patternKeys.Count == 0 && recommendationIds.Count == 0 && entityIds.Count == 0)
        {
            return BadRequest(new { error = "At least one patternKeys, recommendationIds or entityIds item is required" });
        }

        try
        {
            if (!await analysisClient.IsHealthyAsync(cancellationToken))
            {
                return StatusCode(503, new { error = "ML service unavailable" });
            }

            var result = await analysisClient.ResetFeedbackItemsAsync(
                new FeedbackResetItemsRequestPayload(
                    patternKeys,
                    recommendationIds,
                    entityIds,
                    body.ClearPositive ?? false,
                    body.ClearNegative ?? true,
                    body.ClearDismissals ?? true),
                cancellationToken);

            return Ok(result);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "ML feedback item reset request failed");
            return StatusCode(503, new { error = "ML service unavailable", detail = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Feedback item reset failed");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    private static AnalyzeOptionsPayload BuildAnalyzeOptionsPayload(BehaviorAnalysisOptions opts) =>
        new(
            opts.MinSupport,
            opts.MinConfidence,
            opts.MinCadenceConfidence,
            opts.RequirePeriodic,
            opts.MaxGapSeconds,
            opts.MaxSequenceLength,
            opts.LookbackHours,
            opts.FeedbackDismissDays,
            opts.MinLift,
            opts.MinSupportRatio,
            opts.MaxStepGapSeconds);

    private static IReadOnlyList<FeedbackCounterEntry> ToEntries(IReadOnlyDictionary<string, int> values) =>
        values
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kv => new FeedbackCounterEntry(kv.Key, kv.Value))
            .ToList();

    private static IReadOnlyList<string> Normalize(IReadOnlyList<string>? values) =>
        values?
            .Select(v => v.Trim())
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(100)
            .ToList()
        ?? [];
}

public sealed record RecommendationFeedbackRequest(
    string Verdict,
    string PatternKey,
    string? Cadence,
    int? SupportCount,
    double? Confidence,
    double? FrequencyScore,
    IReadOnlyList<string>? EntityIds);

public sealed record ResetFeedbackItemsRequest(
    IReadOnlyList<string>? PatternKeys,
    IReadOnlyList<string>? RecommendationIds,
    IReadOnlyList<string>? EntityIds,
    bool? ClearPositive,
    bool? ClearNegative,
    bool? ClearDismissals);

public sealed record FeedbackStateResponse(
    int TrainingSamples,
    IReadOnlyList<FeedbackCounterEntry> PatternUseful,
    IReadOnlyList<FeedbackCounterEntry> PatternNotUseful,
    IReadOnlyList<FeedbackCounterEntry> EntityUseful,
    IReadOnlyList<FeedbackCounterEntry> EntityNotUseful,
    IReadOnlyList<DismissedFeedbackEntry> DismissedUntil);

public sealed record FeedbackCounterEntry(string Key, int Count);

public sealed record DismissedFeedbackEntry(string Key, string Until);

public sealed record RecommendationsStatusResponse(
    bool MlHealthy,
    string MlBaseUrl,
    string Message);

public sealed record RecommendationsResponse(
    int AnalyzedEventCount,
    int SessionCount,
    int PatternCandidates,
    int FeedbackTrainingSamples,
    int ScannedEventCount,
    int ExcludedEventCount,
    IReadOnlyList<RecommendationPayload> Recommendations,
    string? Message,
    IReadOnlyList<string> AnalysisExcludeEntities,
    IReadOnlyList<string> AnalysisExcludeDomains);

public sealed record RecommendationDiagnosticsResponse(
    int AnalyzedEventCount,
    int EligibleEventCount,
    int SessionCount,
    int RawSequenceCandidateCount,
    int SemanticRejectedCandidateCount,
    int SensorToSensorCandidateCount,
    int MeaningfulCandidateCount,
    int QualityFilteredCandidateCount,
    int RecommendationCount,
    int ScannedEventCount,
    int ExcludedEventCount,
    IReadOnlyList<DiagnosticsCounterPayload> FilterReasons,
    IReadOnlyList<DiagnosticsCounterPayload> SemanticRoles,
    IReadOnlyList<DiagnosticsCounterPayload> SemanticIntents,
    IReadOnlyList<DiagnosticsCounterPayload> OriginTypes,
    IReadOnlyList<DiagnosticsCounterPayload> WeightBuckets,
    string? Message);
