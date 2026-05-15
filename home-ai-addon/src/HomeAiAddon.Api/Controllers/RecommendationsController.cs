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
    IOptions<BehaviorAnalysisOptions> options) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<RecommendationsResponse>> Get(CancellationToken cancellationToken = default)
    {
        try
        {
            var opts = options.Value;
            var events = await store.GetRecentForAnalysisAsync(opts.EventLimit, cancellationToken);
            if (events.Count == 0)
            {
                return Ok(new RecommendationsResponse(
                    0,
                    0,
                    0,
                    Array.Empty<RecommendationPayload>(),
                    "Недостаточно событий для анализа. Дождитесь накопления данных из Home Assistant."));
            }

            var request = new AnalyzeRequestPayload(
                events.Select(e => new AnalyzeEventPayload(
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
                    opts.MaxGapSeconds,
                    opts.MaxSequenceLength,
                    opts.LookbackHours));

            var result = await analysisClient.AnalyzeAsync(request, cancellationToken);
            return Ok(new RecommendationsResponse(
                result.AnalyzedEventCount,
                result.SessionCount,
                result.PatternCandidates,
                result.Recommendations,
                null));
        }
        catch (HttpRequestException ex)
        {
            return StatusCode(503, new { error = "ML service unavailable", detail = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }
}

public sealed record RecommendationsResponse(
    int AnalyzedEventCount,
    int SessionCount,
    int PatternCandidates,
    IReadOnlyList<RecommendationPayload> Recommendations,
    string? Message);
