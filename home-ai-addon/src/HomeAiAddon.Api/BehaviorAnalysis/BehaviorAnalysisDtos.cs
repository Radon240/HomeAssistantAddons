namespace HomeAiAddon.Api.BehaviorAnalysis;

public sealed record AnalyzeRequestPayload(
    IReadOnlyList<AnalyzeEventPayload> Events,
    AnalyzeOptionsPayload? Options);

public sealed record AnalyzeEventPayload(
    long Id,
    string EntityId,
    string? OldState,
    string? NewState,
    string? FriendlyName,
    DateTimeOffset TimeFiredUtc,
    DateTimeOffset ReceivedAtUtc);

public sealed record AnalyzeOptionsPayload(
    int MinSupport,
    double MinConfidence,
    int MaxGapSeconds,
    int MaxSequenceLength,
    int LookbackHours);

public sealed record AnalyzeResponsePayload(
    int AnalyzedEventCount,
    int SessionCount,
    int PatternCandidates,
    IReadOnlyList<RecommendationPayload> Recommendations,
    IReadOnlyDictionary<string, object>? OptionsUsed);

public sealed record RecommendationPayload(
    string Id,
    IReadOnlyList<SequenceStepPayload> Sequence,
    int SupportCount,
    int SessionCount,
    double Confidence,
    double FrequencyScore,
    string Title,
    string Description,
    SuggestedAutomationPayload SuggestedAutomation);

public sealed record SequenceStepPayload(
    string Label,
    string EntityId,
    string? NewState,
    string? FriendlyName);

public sealed record SuggestedAutomationPayload(
    string TriggerEntityId,
    string? TriggerToState,
    IReadOnlyList<string> ActionEntityIds,
    IReadOnlyList<string?> ActionToStates);

public sealed record MlHealthPayload(string Status, string Service);
