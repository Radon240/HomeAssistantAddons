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
        const int maxAttempts = 5;
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
            catch (HttpRequestException ex) when (attempt < maxAttempts && IsTransientConnectionError(ex))
            {
                lastError = ex;
                logger.LogDebug(ex, "ML service not ready, retry {Attempt}/{MaxAttempts}", attempt, maxAttempts);
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
        }

        throw lastError ?? new HttpRequestException("ML service unavailable");
    }

    private static bool IsTransientConnectionError(HttpRequestException ex) =>
        ex.InnerException is System.Net.Sockets.SocketException
        || ex.Message.Contains("Connection refused", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("actively refused", StringComparison.OrdinalIgnoreCase);

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
}
