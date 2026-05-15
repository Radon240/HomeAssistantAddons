namespace HomeAiAddon.Api.BehaviorAnalysis;

public interface IBehaviorAnalysisClient
{
    Task<AnalyzeResponsePayload> AnalyzeAsync(
        AnalyzeRequestPayload request,
        CancellationToken cancellationToken = default);

    Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default);
}
