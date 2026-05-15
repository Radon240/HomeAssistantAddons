using HomeAiAddon.Api.AnomalyDetection;
using HomeAiAddon.Api.BehaviorAnalysis;
using HomeAiAddon.Api.Data;
using HomeAiAddon.Api.Options;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace HomeAiAddon.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class AnomaliesController(
    IAnomalyAlertStore alertStore,
    AnomalyDetectionService detectionService,
    IBehaviorAnalysisClient analysisClient,
    IOptions<AnomalyDetectionOptions> options,
    ILogger<AnomaliesController> logger) : ControllerBase
{
    [HttpGet("status")]
    public async Task<ActionResult<AnomaliesStatusResponse>> GetStatus(
        CancellationToken cancellationToken = default)
    {
        var opts = options.Value;
        var healthy = await analysisClient.IsHealthyAsync(cancellationToken);
        return Ok(new AnomaliesStatusResponse(
            opts.Enabled,
            healthy,
            opts.IntervalMinutes,
            healthy ? "ML service is reachable" : "ML service is not reachable"));
    }

    [HttpGet]
    public async Task<ActionResult<AnomaliesListResponse>> Get(
        [FromQuery] int? limit,
        [FromQuery] DateTimeOffset? since,
        CancellationToken cancellationToken = default)
    {
        var opts = options.Value;
        var effectiveLimit = limit ?? opts.ListLimit;
        var alerts = await alertStore.GetRecentAsync(effectiveLimit, since, cancellationToken);

        return Ok(new AnomaliesListResponse(
            alerts.Count,
            alerts.Select(Map).ToList()));
    }

    [HttpPost("detect")]
    public async Task<ActionResult<AnomalyDetectRunResponse>> Detect(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await detectionService.RunDetectionAsync(cancellationToken);
            if (!result.Success)
            {
                return StatusCode(503, new AnomalyDetectRunResponse(
                    false,
                    result.ScannedEventCount,
                    result.ExcludedEventCount,
                    result.AnalyzedEventCount,
                    result.PersistedCount,
                    result.Message));
            }

            var alerts = await alertStore.GetRecentAsync(
                options.Value.ListLimit,
                cancellationToken: cancellationToken);

            return Ok(new AnomalyDetectRunResponse(
                true,
                result.ScannedEventCount,
                result.ExcludedEventCount,
                result.AnalyzedEventCount,
                result.PersistedCount,
                result.Message,
                alerts.Select(Map).ToList()));
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Anomaly detection request failed");
            return StatusCode(503, new { error = "ML service unavailable", detail = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Anomaly detection failed");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    private static AnomalyAlertResponse Map(AnomalyAlertDto dto) =>
        new(
            dto.Id,
            dto.DetectionId,
            dto.EntityId,
            dto.AnomalyType,
            dto.Severity,
            dto.Score,
            dto.Method,
            dto.Title,
            dto.Explanation,
            dto.DetectedAtUtc,
            dto.PersistedAtUtc,
            dto.RelatedEventIds,
            dto.Metrics);
}

public sealed record AnomaliesStatusResponse(
    bool Enabled,
    bool MlHealthy,
    int IntervalMinutes,
    string Message);

public sealed record AnomaliesListResponse(
    int Count,
    IReadOnlyList<AnomalyAlertResponse> Anomalies);

public sealed record AnomalyDetectRunResponse(
    bool Success,
    int ScannedEventCount,
    int ExcludedEventCount,
    int AnalyzedEventCount,
    int PersistedCount,
    string? Message,
    IReadOnlyList<AnomalyAlertResponse>? Anomalies = null);

public sealed record AnomalyAlertResponse(
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
