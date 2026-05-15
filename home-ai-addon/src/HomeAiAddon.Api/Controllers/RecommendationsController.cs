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
    [HttpGet("filters")]
    public ActionResult<AnalysisFiltersResponse> GetFilters()
    {
        var settings = analysisEntityFilter.GetSettings();
        return Ok(new AnalysisFiltersResponse(
            settings.ExcludeEntities,
            settings.ExcludeDomains,
            settings.HasExclusions,
            "Настройте analysis_exclude_entities и analysis_exclude_domains в конфигурации add-on."));
    }

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
            var batch = await store.GetRecentForAnalysisAsync(
                opts.EventLimit,
                analysisEntityFilter.ShouldInclude,
                cancellationToken);

            if (batch.Events.Count == 0)
            {
                var filterSettings = analysisEntityFilter.GetSettings();
                var hint = filterSettings.HasExclusions
                    ? " После исключений по analysis_exclude_* не осталось событий — ослабьте фильтры."
                    : " Дождитесь накопления данных из Home Assistant.";
                return Ok(new RecommendationsResponse(
                    0,
                    0,
                    0,
                    batch.ScannedCount,
                    batch.ExcludedCount,
                    Array.Empty<RecommendationPayload>(),
                    "Недостаточно событий для анализа." + hint,
                    filterSettings.ExcludeEntities,
                    filterSettings.ExcludeDomains));
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
            var filters = analysisEntityFilter.GetSettings();
            return Ok(new RecommendationsResponse(
                result.AnalyzedEventCount,
                result.SessionCount,
                result.PatternCandidates,
                batch.ScannedCount,
                batch.ExcludedCount,
                result.Recommendations,
                null,
                filters.ExcludeEntities,
                filters.ExcludeDomains));
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

public sealed record AnalysisFiltersResponse(
    IReadOnlyList<string> ExcludeEntities,
    IReadOnlyList<string> ExcludeDomains,
    bool HasExclusions,
    string Hint);

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
