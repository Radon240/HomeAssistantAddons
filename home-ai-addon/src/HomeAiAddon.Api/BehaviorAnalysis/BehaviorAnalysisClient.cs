using System.Net.Http.Json;
using System.Text.Json;
namespace HomeAiAddon.Api.BehaviorAnalysis;

public sealed class BehaviorAnalysisClient(
    HttpClient httpClient,
    ILogger<BehaviorAnalysisClient> logger) : IBehaviorAnalysisClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public async Task<AnalyzeResponsePayload> AnalyzeAsync(
        AnalyzeRequestPayload request,
        CancellationToken cancellationToken = default)
    {
        const int maxAttempts = 3;
        Exception? lastError = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                using var response = await httpClient.PostAsJsonAsync(
                    "/api/v1/analyze",
                    request,
                    JsonOptions,
                    cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync(cancellationToken);
                    logger.LogWarning(
                        "ML analyze failed: {StatusCode} {Body}",
                        (int)response.StatusCode,
                        body);
                    response.EnsureSuccessStatusCode();
                }

                var payload = await response.Content.ReadFromJsonAsync<AnalyzeResponsePayload>(
                    JsonOptions,
                    cancellationToken);

                return payload ?? throw new InvalidOperationException("ML service returned empty analyze response.");
            }
            catch (Exception ex) when (attempt < maxAttempts && IsRetryable(ex))
            {
                lastError = ex;
                logger.LogWarning(
                    ex,
                    "ML analyze attempt {Attempt}/{MaxAttempts} failed, retrying",
                    attempt,
                    maxAttempts);
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
        }

        var detail = FormatError(lastError);
        logger.LogError("ML analyze failed after retries: {Detail}", detail);
        throw new HttpRequestException($"ML analyze failed: {detail}", lastError);
    }

    public async Task<FeedbackResponsePayload> SubmitFeedbackAsync(
        FeedbackRequestPayload request,
        CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync(
            "/api/v1/feedback",
            request,
            JsonOptions,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            logger.LogWarning(
                "ML feedback failed: {StatusCode} {Body}",
                (int)response.StatusCode,
                body);
            response.EnsureSuccessStatusCode();
        }

        var payload = await response.Content.ReadFromJsonAsync<FeedbackResponsePayload>(
            JsonOptions,
            cancellationToken);

        return payload ?? throw new InvalidOperationException("ML service returned empty feedback response.");
    }

    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await httpClient.GetAsync("/health", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            var health = await response.Content.ReadFromJsonAsync<MlHealthPayload>(
                JsonOptions,
                cancellationToken);

            return health is not null
                && string.Equals(health.Status, "Healthy", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "ML service health check failed");
            return false;
        }
    }

    private static bool IsRetryable(Exception ex) =>
        ex is TaskCanceledException
        or HttpRequestException
        or IOException;

    private static string FormatError(Exception? ex)
    {
        if (ex is null)
        {
            return "unknown error";
        }

        var parts = new List<string> { ex.Message };
        var inner = ex.InnerException;
        while (inner is not null)
        {
            parts.Add(inner.Message);
            inner = inner.InnerException;
        }

        return string.Join(" | ", parts.Distinct());
    }
}
