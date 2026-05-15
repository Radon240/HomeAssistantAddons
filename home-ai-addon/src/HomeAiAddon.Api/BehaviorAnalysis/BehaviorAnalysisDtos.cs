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
    double MinCadenceConfidence,
    bool RequirePeriodic,
    int MaxGapSeconds,
    int MaxSequenceLength,
    int LookbackHours,
    int FeedbackDismissDays = 14);

public sealed record AnalyzeResponsePayload(
    int AnalyzedEventCount,
    int SessionCount,
    int PatternCandidates,
    int FeedbackTrainingSamples,
    IReadOnlyList<RecommendationPayload> Recommendations,
    IReadOnlyDictionary<string, object>? OptionsUsed);

public sealed record RecommendationPayload(
    string Id,
    string PatternKey,
    IReadOnlyList<SequenceStepPayload> Sequence,
    int SupportCount,
    int SessionCount,
    double Confidence,
    double BaseConfidence,
    double FeedbackScore,
    double FrequencyScore,
    string Cadence,
    double CadenceConfidence,
    string CadenceLabel,
    string ScheduleHint,
    string Title,
    string Description,
    SuggestedAutomationPayload SuggestedAutomation);

public sealed record FeedbackRequestPayload(
    string RecommendationId,
    string PatternKey,
    string Verdict,
    string Cadence,
    int SupportCount,
    double Confidence,
    double FrequencyScore,
    IReadOnlyList<string> EntityIds);

public sealed record FeedbackResponsePayload(
    bool Accepted,
    int TrainingSamples,
    string Message);

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

public sealed record AnomalyDetectRequestPayload(
    IReadOnlyList<AnalyzeEventPayload> Events,
    AnomalyDetectionOptionsPayload? Options);

public sealed record AnomalyDetectionOptionsPayload(
    int MinEvents,
    int MinEventsPerEntity,
    int MinHourlySamples,
    int RollingWindowHours,
    double ZScoreThreshold,
    double UnusualHourMaxRatio,
    double MinScore,
    double MediumSeverityThreshold,
    double HighSeverityThreshold,
    int MaxResults,
    int IsolationForestEstimators,
    double IsolationForestContamination,
    int IsolationForestMinSamples);

public sealed record AnomalyDetectResponsePayload(
    int AnalyzedEventCount,
    int AnomalyCount,
    IReadOnlyList<AnomalyItemPayload> Anomalies,
    IReadOnlyDictionary<string, object>? OptionsUsed);

public sealed record AnomalyItemPayload(
    string Id,
    string EntityId,
    string AnomalyType,
    string Severity,
    double Score,
    string Method,
    string Title,
    string Explanation,
    DateTimeOffset DetectedAtUtc,
    IReadOnlyList<long> RelatedEventIds,
    IReadOnlyDictionary<string, object> Metrics);
