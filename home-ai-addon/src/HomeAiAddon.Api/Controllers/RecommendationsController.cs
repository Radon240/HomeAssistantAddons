using HomeAiAddon.Api.BehaviorAnalysis;
using HomeAiAddon.Api.Data;
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

            var request = new AnalyzeRequestPayload(
                batch.Events.Select(e => new AnalyzeEventPayload(
                    e.Id,
                    e.EntityId,
                    e.OldState,
                    e.NewState,
                    e.FriendlyName,
                    e.TimeFiredUtc,
                    e.ReceivedAtUtc)).ToList(),
                new AnalyzeOptionsPayload(
                    opts.MinSupport,
                    opts.MinConfidence,
                    opts.MinCadenceConfidence,
                    opts.RequirePeriodic,
                    opts.MaxGapSeconds,
                    opts.MaxSequenceLength,
                    opts.LookbackHours));

            var result = await analysisClient.AnalyzeAsync(request, cancellationToken);
            return Ok(new RecommendationsResponse(
                result.AnalyzedEventCount,
                result.SessionCount,
                result.PatternCandidates,
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
}

public sealed record RecommendationsStatusResponse(
    bool MlHealthy,
    string MlBaseUrl,
    string Message);

public sealed record RecommendationsResponse(
    int AnalyzedEventCount,
    int SessionCount,
    int PatternCandidates,
    int ScannedEventCount,
    int ExcludedEventCount,
    IReadOnlyList<RecommendationPayload> Recommendations,
    string? Message,
    IReadOnlyList<string> AnalysisExcludeEntities,
    IReadOnlyList<string> AnalysisExcludeDomains);
