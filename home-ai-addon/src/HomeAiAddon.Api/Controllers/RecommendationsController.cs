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

            var entityMetadata = await LoadEntityMetadataAsync(entitiesService, cancellationToken);
            var request = new AnalyzeRequestPayload(
                batch.Events.Select(e => ToAnalyzeEventPayload(e, entityMetadata)).ToList(),
                new AnalyzeOptionsPayload(
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
                    opts.MaxStepGapSeconds));

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

    private static async Task<IReadOnlyDictionary<string, HomeAssistantEntityDto>> LoadEntityMetadataAsync(
        HomeAssistantEntitiesService entitiesService,
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

public sealed record RecommendationFeedbackRequest(
    string Verdict,
    string PatternKey,
    string? Cadence,
    int? SupportCount,
    double? Confidence,
    double? FrequencyScore,
    IReadOnlyList<string>? EntityIds);

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
