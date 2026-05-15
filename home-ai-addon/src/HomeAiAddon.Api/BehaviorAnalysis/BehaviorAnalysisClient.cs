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
